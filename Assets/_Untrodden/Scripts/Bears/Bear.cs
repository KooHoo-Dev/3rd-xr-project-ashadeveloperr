using Untrodden.Audio;
using Untrodden.Player;
using UnityEngine;

namespace Untrodden.Bears
{
    /// <summary>
    /// Wander → Alert (growls, stares) → Charge → Attack, or Leave.
    /// Backing off during Alert makes it leave. A shot near it may scare it off.
    /// A body hit makes it flee or charge; only a head hit brings it down. Nothing to gain from killing it.
    /// </summary>
    public class Bear : MonoBehaviour
    {
        public const string HeadColliderName = "Bear Head";

        enum State { Wander, Alert, Charge, Leave, Dead }

        const float k_NoticeDistance = 22f;
        const float k_ChargeDistance = 12f;
        const float k_AttackDistance = 1.8f;
        const float k_AttackDamage = 35f;
        const float k_AlertSeconds = 4f;

        State m_State;
        float m_StateTime;
        float m_AlertStartDistance;
        Vector3 m_WanderTarget;
        float m_BobPhase;
        Transform m_Body;
        AudioSource m_Voice;
        BearSpawner m_Spawner;
        System.Random m_Rng;

        public bool IsDead => m_State == State.Dead;
        public bool IsGone { get; private set; }

        public static Bear Create(Vector3 position, BearSpawner spawner, GameAssets assets, int seed)
        {
            var go = new GameObject("Bear");
            go.transform.position = position;
            var bear = go.AddComponent<Bear>();
            bear.m_Spawner = spawner;
            bear.m_Rng = new System.Random(seed);
            bear.Build(assets);
            bear.Enter(State.Wander);
            return bear;
        }

        void Build(GameAssets assets)
        {
            m_Body = new GameObject("Body").transform;
            m_Body.SetParent(transform, false);

            Meshes.Part(m_Body, PrimitiveType.Capsule, assets.bearFur, new Vector3(0f, 0.95f, 0f), new Vector3(1.1f, 0.95f, 1.1f), new Vector3(90f, 0f, 0f));
            var legs = new[] { new Vector3(0.35f, 0.35f, 0.65f), new Vector3(-0.35f, 0.35f, 0.65f), new Vector3(0.35f, 0.35f, -0.65f), new Vector3(-0.35f, 0.35f, -0.65f) };
            foreach (var l in legs)
                Meshes.Part(m_Body, PrimitiveType.Cylinder, assets.bearFur, l, new Vector3(0.28f, 0.38f, 0.28f));
            var head = Meshes.Part(m_Body, PrimitiveType.Sphere, assets.bearFur, new Vector3(0f, 1.2f, 1.15f), new Vector3(0.6f, 0.55f, 0.6f), default, true);
            head.name = HeadColliderName;
            Meshes.Part(head.transform, PrimitiveType.Sphere, assets.bearDark, new Vector3(0f, -0.15f, 0.45f), new Vector3(0.45f, 0.35f, 0.45f));
            Meshes.Part(head.transform, PrimitiveType.Sphere, assets.bearFur, new Vector3(0.32f, 0.42f, 0f), Vector3.one * 0.3f);
            Meshes.Part(head.transform, PrimitiveType.Sphere, assets.bearFur, new Vector3(-0.32f, 0.42f, 0f), Vector3.one * 0.3f);

            var body = gameObject.AddComponent<CapsuleCollider>();
            body.direction = 2;
            body.center = new Vector3(0f, 0.95f, 0f);
            body.radius = 0.55f;
            body.height = 2f; // solid: rifle rays ignore triggers

            m_Voice = gameObject.AddComponent<AudioSource>();
            m_Voice.spatialBlend = 1f;
            m_Voice.minDistance = 3f;
            m_Voice.maxDistance = 60f;
            m_Voice.rolloffMode = AudioRolloffMode.Linear;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || IsGone)
                return;

            m_StateTime += Time.deltaTime;
            var player = gm.Head.position;
            var toPlayer = player - transform.position;
            toPlayer.y = 0f;
            var distance = toPlayer.magnitude;

            switch (m_State)
            {
                case State.Wander:
                    MoveTowards(m_WanderTarget, 1.2f);
                    if ((transform.position - m_WanderTarget).sqrMagnitude < 4f || m_StateTime > 12f)
                        PickWanderTarget();
                    if (gm.IsPlaying && distance < k_NoticeDistance)
                        Enter(State.Alert);
                    break;

                case State.Alert:
                    Face(toPlayer);
                    if (m_StateTime < 0.1f)
                        m_AlertStartDistance = distance;
                    if (!gm.IsPlaying || distance > m_AlertStartDistance + 4f || distance > k_NoticeDistance + 8f)
                        Enter(State.Leave);                       // the player backed off
                    else if (distance < k_ChargeDistance || m_StateTime > k_AlertSeconds)
                        Enter(State.Charge);
                    break;

                case State.Charge:
                    MoveTowards(player, 7f);
                    if (!gm.IsPlaying)
                        Enter(State.Leave);
                    else if (distance < k_AttackDistance)
                    {
                        gm.Survival.Damage(k_AttackDamage, Cause.Bear);
                        Sounds.Play(SoundId.BearAttack, gm.Head.position, 1f);
                        Enter(State.Leave);
                    }
                    else if (m_StateTime > 8f)
                        Enter(State.Leave);
                    break;

                case State.Leave:
                    MoveTowards(transform.position - toPlayer.normalized * 10f, 4.5f);
                    if (distance > 70f || m_StateTime > 15f)
                        Despawn();
                    break;

                case State.Dead:
                    m_Body.localRotation = Quaternion.Slerp(m_Body.localRotation, Quaternion.Euler(0f, 0f, 90f), Time.deltaTime * 4f);
                    if (m_StateTime > 12f)
                        Despawn();
                    break;
            }
        }

        void Enter(State state)
        {
            m_State = state;
            m_StateTime = 0f;
            switch (state)
            {
                case State.Wander:
                    PickWanderTarget();
                    break;
                case State.Alert:
                case State.Charge:
                    PlayVoice(SoundId.Growl, state == State.Charge ? 1f : 0.8f);
                    break;
            }
        }

        public void OnShot(bool head)
        {
            if (m_State == State.Dead || IsGone)
                return;
            if (head)
            {
                PlayVoice(SoundId.BearHurt, 1f);
                Enter(State.Dead);
                return;
            }
            PlayVoice(SoundId.BearHurt, 0.8f);
            Enter(m_Rng.NextDouble() < 0.5 ? State.Leave : State.Charge);
        }

        /// <summary>A shot went past: often enough to scare it away, unless it is already charging.</summary>
        public void OnNearMiss()
        {
            if (m_State == State.Dead || m_State == State.Leave || m_State == State.Charge || IsGone)
                return;
            if (m_Rng.NextDouble() < 0.6)
                Enter(State.Leave);
        }

        void PickWanderTarget()
        {
            var a = (float)m_Rng.NextDouble() * Mathf.PI * 2f;
            m_WanderTarget = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 12f;
        }

        void MoveTowards(Vector3 target, float speed)
        {
            var dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                return;
            Face(dir);
            var p = transform.position + dir.normalized * speed * Time.deltaTime;
            p.y = GameManager.Instance.World.Height(p.x, p.z);
            transform.position = p;

            m_BobPhase += Time.deltaTime * speed * 2.5f;
            m_Body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(m_BobPhase)) * 0.06f * Mathf.Min(speed, 3f), 0f);
        }

        void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);
        }

        void PlayVoice(SoundId id, float volume)
        {
            m_Voice.clip = Sounds.Clip(id);
            m_Voice.volume = volume;
            m_Voice.pitch = 0.9f + (float)m_Rng.NextDouble() * 0.2f;
            m_Voice.Play();
        }

        public void Despawn()
        {
            if (IsGone)
                return;
            IsGone = true;
            m_Spawner?.Forget(this);
            Destroy(gameObject);
        }
    }
}
