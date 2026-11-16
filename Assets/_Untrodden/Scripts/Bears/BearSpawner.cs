using System.Collections.Generic;
using Untrodden.Items;
using Untrodden.World;
using UnityEngine;

namespace Untrodden.Bears
{
    /// <summary>
    /// Rolls for a bear every few seconds. The chance depends on where the player is:
    /// rare in the forest, common by the river, most common on the lower river near the sea.
    /// Bears appear inside the fog, so they are heard before they are seen.
    /// </summary>
    public class BearSpawner : MonoBehaviour
    {
        const float k_CheckSeconds = 6f;
        const int k_MaxBears = 1;

        static BearSpawner s_Instance;

        readonly List<Bear> m_Bears = new List<Bear>();
        GameAssets m_Assets;
        WorldMap m_World;
        Transform m_Head;
        System.Random m_Rng;
        float m_Timer = 20f; // a calm start

        public void Init(WorldMap world, Transform head, GameAssets assets)
        {
            s_Instance = this;
            m_World = world;
            m_Head = head;
            m_Assets = assets;
            m_Rng = new System.Random(world.Seed + 99);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsPlaying || m_World == null)
                return;

            m_Timer -= Time.deltaTime;
            if (m_Timer > 0f)
                return;
            m_Timer = k_CheckSeconds;

            if (m_Bears.Count >= k_MaxBears)
                return;

            var p = m_Head.position;
            if (FirePit.AnyLitWithin(p, 15f))
                return;

            var biome = m_World.GetBiome(p.x, p.z);
            var chance = Chance(biome, WorldMap.Downstream(p.z));
            if (m_Rng.NextDouble() > chance)
                return;

            // spawn out of sight in the fog, ahead or to the side
            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up).normalized;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var angle = (float)(m_Rng.NextDouble() * 220.0 - 110.0);
                var dir = Quaternion.Euler(0f, angle, 0f) * forward;
                var pos = p + dir * (float)(32.0 + m_Rng.NextDouble() * 12.0);
                if (pos.x < 5f || pos.x > WorldMap.Width - 5f || pos.z < 5f || pos.z > WorldMap.CoastZ - 10f)
                    continue;
                if (m_World.TryGetWater(pos.x, pos.z, out _) || m_World.IsInsideVillage(pos))
                    continue;
                pos.y = m_World.Height(pos.x, pos.z);
                m_Bears.Add(Bear.Create(pos, this, m_Assets, m_Rng.Next()));
                break;
            }
        }

        static float Chance(Biome biome, float downstream)
        {
            switch (biome)
            {
                case Biome.Downstream: return 0.12f;
                case Biome.River: return 0.06f + 0.06f * downstream;
                case Biome.Stream: return 0.035f;
                case Biome.Valley: return 0.02f;
                case Biome.Forest: return 0.01f;
                case Biome.Ridge: return 0.004f;
                case Biome.Coast: return 0.02f;
                default: return 0f;
            }
        }

        public void Forget(Bear bear) => m_Bears.Remove(bear);

        public void ClearAll()
        {
            foreach (var bear in m_Bears.ToArray())
                if (bear != null)
                    bear.Despawn();
            m_Bears.Clear();
        }

        /// <summary>A shot that hit nothing: bears near its path may be scared off.</summary>
        public static void NotifyMiss(Vector3 from, Vector3 to)
        {
            if (s_Instance == null)
                return;
            foreach (var bear in s_Instance.m_Bears.ToArray())
            {
                if (bear == null || bear.IsDead)
                    continue;
                var b = bear.transform.position + Vector3.up;
                var seg = to - from;
                var t = Mathf.Clamp01(Vector3.Dot(b - from, seg) / Mathf.Max(seg.sqrMagnitude, 0.001f));
                if (Vector3.Distance(b, from + seg * t) < 8f)
                    bear.OnNearMiss();
            }
        }
    }
}
