using System.Collections.Generic;
using Untrodden.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Untrodden.Items
{
    /// <summary>
    /// A ring of stones with three log sockets. Three logs + fire steel sparks = fire.
    /// A lit fire warms (stops cold damage, heals), shelters from the blizzard and becomes the respawn point.
    /// </summary>
    public class FirePit : MonoBehaviour
    {
        public const int LogsNeeded = 3;
        public const float WarmRadius = 3.5f;
        public const float BurnSeconds = 150f;
        const int k_StrikesToLight = 2;

        static readonly List<FirePit> s_All = new List<FirePit>();

        readonly List<TypedSocket> m_Sockets = new List<TypedSocket>();
        ParticleSystem m_Fire;
        ParticleSystem m_Sparks;
        Light m_Light;
        AudioSource m_Crackle;
        GameObject m_Burning;
        float m_BurnLeft;
        int m_Strikes;

        public bool IsLit { get; private set; }
        public float BurnFraction => IsLit ? m_BurnLeft / BurnSeconds : 0f;

        public int LogCount
        {
            get
            {
                var n = 0;
                foreach (var s in m_Sockets)
                    if (s.hasSelection) n++;
                return n;
            }
        }

        public static FirePit FindNearest(Vector3 p, float radius)
        {
            FirePit best = null;
            var bestD = radius * radius;
            foreach (var pit in s_All)
            {
                var d = (pit.transform.position - p);
                d.y = 0f;
                if (d.sqrMagnitude < bestD)
                {
                    bestD = d.sqrMagnitude;
                    best = pit;
                }
            }
            return best;
        }

        /// <summary>Nearest burning fire whose warmth reaches this point, or null.</summary>
        public static FirePit WarmthAt(Vector3 p)
        {
            foreach (var pit in s_All)
            {
                if (!pit.IsLit)
                    continue;
                var d = pit.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude < WarmRadius * WarmRadius)
                    return pit;
            }
            return null;
        }

        public static bool AnyLitWithin(Vector3 p, float radius)
        {
            foreach (var pit in s_All)
                if (pit.IsLit && (pit.transform.position - p).sqrMagnitude < radius * radius)
                    return true;
            return false;
        }

        public static FirePit Create(Vector3 groundPosition, GameAssets assets)
        {
            var go = new GameObject("Fire Pit");
            go.transform.position = groundPosition;
            var pit = go.AddComponent<FirePit>();
            pit.Build(assets);
            return pit;
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        void Build(GameAssets assets)
        {
            // stone ring
            for (var k = 0; k < 7; k++)
            {
                var a = k * Mathf.PI * 2f / 7f;
                Meshes.Part(transform, PrimitiveType.Sphere, assets.rock,
                    new Vector3(Mathf.Cos(a) * 0.45f, 0.05f, Mathf.Sin(a) * 0.45f), new Vector3(0.22f, 0.14f, 0.2f));
            }
            Meshes.Part(transform, PrimitiveType.Cylinder, assets.ash, new Vector3(0f, 0.01f, 0f), new Vector3(0.7f, 0.01f, 0.7f));

            // three sockets leaning into a teepee
            for (var k = 0; k < LogsNeeded; k++)
            {
                var a = k * Mathf.PI * 2f / LogsNeeded;
                var s = new GameObject($"Log Socket {k}");
                s.transform.SetParent(transform, false);
                s.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.15f, 0.22f, Mathf.Sin(a) * 0.15f);
                // log's long axis is local X; tilt it up towards the centre
                s.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 180f, 55f);
                var col = s.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.35f;
                var socket = s.AddComponent<TypedSocket>();
                socket.showInteractableHoverMeshes = false;
                socket.Accept = i => !IsLit && i.transform.GetComponent<Firewood>() != null;
                m_Sockets.Add(socket);
            }

            // burning logs shown while lit (the real logs are consumed)
            m_Burning = new GameObject("Burning Logs");
            m_Burning.transform.SetParent(transform, false);
            if (assets.campfirePrefab != null)
            {
                // The pack's camp fire model (pivot at its base), sized to sit inside the stone ring.
                var fire = Instantiate(assets.campfirePrefab, m_Burning.transform);
                var bounds = new Bounds(fire.transform.position, Vector3.zero);
                foreach (var r in fire.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(r.bounds);
                var width = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
                fire.transform.localScale = Vector3.one * (0.85f / width);
                fire.transform.localPosition = Vector3.zero;
            }
            else
            {
                for (var k = 0; k < LogsNeeded; k++)
                {
                    var a = k * 360f / LogsNeeded;
                    Meshes.Part(m_Burning.transform, PrimitiveType.Cylinder, assets.wood,
                        new Vector3(0f, 0.18f, 0f), new Vector3(0.1f, 0.28f, 0.1f), new Vector3(35f, a, 0f));
                }
            }
            m_Burning.SetActive(false);

            m_Fire = CreateParticles("Flames", assets.fireParticle, false);
            m_Sparks = CreateParticles("Sparks", assets.fireParticle, true);

            var lightGo = new GameObject("Fire Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            m_Light = lightGo.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.color = new Color(1f, 0.55f, 0.2f);
            m_Light.range = 9f;
            m_Light.intensity = 0f;
            m_Light.shadows = LightShadows.None;
            m_Light.enabled = false;

            m_Crackle = gameObject.AddComponent<AudioSource>();
            m_Crackle.clip = Sounds.Clip(SoundId.Crackle);
            m_Crackle.loop = true;
            m_Crackle.spatialBlend = 1f;
            m_Crackle.minDistance = 1f;
            m_Crackle.maxDistance = 15f;
            m_Crackle.volume = 0.6f;
        }

        ParticleSystem CreateParticles(string name, Material mat, bool sparks)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            if (sparks)
            {
                main.loop = false;
                main.duration = 0.2f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
                main.startColor = new Color(1f, 0.8f, 0.4f);
                main.gravityModifier = 0.6f;
                main.maxParticles = 60;
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 50f;
                shape.radius = 0.02f;
            }
            else
            {
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.7f));
                main.maxParticles = 80;
                var emission = ps.emission;
                emission.rateOverTime = 40f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 10f;
                shape.radius = 0.15f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        public bool TryAdd(Firewood wood)
        {
            if (IsLit || wood == null)
                return false;
            foreach (var socket in m_Sockets)
            {
                if (socket.hasSelection)
                    continue;
                var manager = socket.interactionManager;
                if (manager == null)
                    return false;
                manager.SelectEnter(socket, (IXRSelectInteractable)wood.Grab);
                Sounds.Play(SoundId.Wood, transform.position, 0.6f);
                return true;
            }
            return false;
        }

        /// <summary>Called by the fire steel when scraped above the pit.</summary>
        public void Strike(Vector3 at)
        {
            m_Sparks.transform.position = at;
            m_Sparks.Emit(25);
            Sounds.Play(SoundId.Strike, at, 0.7f);

            if (IsLit || LogCount < LogsNeeded)
                return;
            m_Strikes++;
            if (m_Strikes >= k_StrikesToLight)
                Ignite();
        }

        void Ignite()
        {
            IsLit = true;
            m_Strikes = 0;
            m_BurnLeft = BurnSeconds;

            // The logs turn into the burning stack.
            foreach (var socket in m_Sockets)
            {
                if (!socket.hasSelection)
                    continue;
                var wood = socket.firstInteractableSelected?.transform.GetComponent<Firewood>();
                if (wood != null)
                    wood.Consume();
            }

            m_Burning.SetActive(true);
            m_Fire.Play();
            m_Light.enabled = true;
            m_Crackle.Play();
            Sounds.Play(SoundId.Ignite, transform.position, 0.9f);
            GameManager.Instance?.SetCheckpoint(this);
        }

        void Update()
        {
            if (!IsLit)
                return;

            m_BurnLeft -= Time.deltaTime;
            var strength = Mathf.Clamp01(m_BurnLeft / 20f); // dims over the last 20 s
            m_Light.intensity = (2.2f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 0.8f) * strength;
            var emission = m_Fire.emission;
            emission.rateOverTime = 40f * Mathf.Max(0.2f, strength);

            if (m_BurnLeft <= 0f)
                Extinguish();
        }

        void Extinguish()
        {
            IsLit = false;
            m_Fire.Stop();
            m_Light.enabled = false;
            m_Crackle.Stop();
            m_Burning.SetActive(false);
        }
    }
}
