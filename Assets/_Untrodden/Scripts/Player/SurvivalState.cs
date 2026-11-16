using System;
using Untrodden.Items;
using Untrodden.Weather;
using Untrodden.World;
using UnityEngine;

namespace Untrodden.Player
{
    public enum Cause { None, Cold, Hunger, Thirst, Bear }

    /// <summary>
    /// One health bar, three slow drains (cold, hunger, thirst) and bear hits.
    /// Each cause has a different fix in a different place: fire, food, water.
    /// </summary>
    public class SurvivalState : MonoBehaviour
    {
        [Header("Cold (health per second)")]
        [SerializeField] float m_BaseCold = 0.06f;
        [SerializeField] float m_RidgeColdMultiplier = 1.6f;
        [SerializeField] float m_WaterColdMultiplier = 5f;
        [SerializeField] float m_BlizzardColdMultiplier = 3.5f;
        [SerializeField] float m_ChillCold = 0.25f;
        [SerializeField] float m_FireHeal = 1.2f;

        [Header("Hunger / thirst (seconds from 0 to 100)")]
        [SerializeField] float m_HungerSeconds = 300f;
        [SerializeField] float m_ThirstSeconds = 200f;
        [SerializeField] float m_StarveThreshold = 60f;
        [SerializeField] Vector2 m_StarveDamage = new Vector2(0.04f, 0.18f);

        WorldMap m_World;
        WeatherController m_Weather;
        Transform m_Head;
        Transform m_Feet;
        float m_BiomeTimer;
        readonly float[] m_DamageByCause = new float[5];

        public float Health { get; private set; } = 100f;
        public float Hunger { get; private set; }
        public float Thirst { get; private set; }
        public float Chill { get; private set; }
        public float ColdRate { get; private set; }
        public float HungerRate { get; private set; }
        public float ThirstRate { get; private set; }
        public bool IsWarm { get; private set; }
        public bool InWater { get; private set; }
        public Biome CurrentBiome { get; private set; }
        public bool Paused { get; set; } = true;

        public event Action<Cause> Died;
        public event Action<float> Hurt;

        public void Init(WorldMap world, WeatherController weather, Transform head, Transform feet)
        {
            m_World = world;
            m_Weather = weather;
            m_Head = head;
            m_Feet = feet;
        }

        void Update()
        {
            if (Paused || m_World == null || Health <= 0f)
                return;

            var dt = Time.deltaTime;
            var p = m_Head.position;

            m_BiomeTimer -= dt;
            if (m_BiomeTimer <= 0f)
            {
                m_BiomeTimer = 0.5f;
                CurrentBiome = m_World.GetBiome(p.x, p.z);
                InWater = m_World.TryGetWater(p.x, p.z, out var surface) && m_Feet.position.y < surface - 0.15f;
            }

            IsWarm = FirePit.WarmthAt(p) != null || CurrentBiome == Biome.Village;

            // cold
            var mult = 1f;
            if (CurrentBiome == Biome.Ridge) mult *= m_RidgeColdMultiplier;
            if (CurrentBiome == Biome.Coast) mult *= 1.3f;
            if (InWater) mult *= m_WaterColdMultiplier;
            var blizzard = m_Weather != null ? m_Weather.Intensity : 0f;
            mult *= 1f + blizzard * m_BlizzardColdMultiplier;
            ColdRate = IsWarm ? 0f : m_BaseCold * mult + (Chill > 0f ? m_ChillCold : 0f);
            Chill = Mathf.Max(0f, Chill - dt);

            // hunger and thirst
            Hunger = Mathf.Min(100f, Hunger + dt * 100f / m_HungerSeconds);
            Thirst = Mathf.Min(100f, Thirst + dt * 100f / m_ThirstSeconds);
            HungerRate = StarveRate(Hunger);
            ThirstRate = StarveRate(Thirst) * 1.3f;

            Apply(ColdRate * dt, Cause.Cold);
            Apply(HungerRate * dt, Cause.Hunger);
            Apply(ThirstRate * dt, Cause.Thirst);

            if (IsWarm)
                Health = Mathf.Min(100f, Health + m_FireHeal * dt);

            CheckDeath();
        }

        float StarveRate(float meter) =>
            meter <= m_StarveThreshold ? 0f :
            Mathf.Lerp(m_StarveDamage.x, m_StarveDamage.y, (meter - m_StarveThreshold) / (100f - m_StarveThreshold));

        void Apply(float amount, Cause cause)
        {
            if (amount <= 0f)
                return;
            Health -= amount;
            m_DamageByCause[(int)cause] += amount;
        }

        public void Damage(float amount, Cause cause)
        {
            if (Paused || Health <= 0f)
                return;
            Apply(amount, cause);
            Hurt?.Invoke(amount);
            CheckDeath();
        }

        void CheckDeath()
        {
            if (Health > 0f)
                return;
            Health = 0f;
            Paused = true;

            var worst = Cause.Cold;
            for (var c = 1; c < m_DamageByCause.Length; c++)
                if (m_DamageByCause[c] > m_DamageByCause[(int)worst])
                    worst = (Cause)c;
            Died?.Invoke(worst);
        }

        public void Eat(ItemKind kind)
        {
            switch (kind)
            {
                case ItemKind.Fish:
                    Hunger = Mathf.Max(0f, Hunger - 35f);
                    Thirst = Mathf.Max(0f, Thirst - 5f);
                    break;
                case ItemKind.Can:
                    // Salty emergency food: very filling, but makes you thirsty.
                    Hunger = Mathf.Max(0f, Hunger - 55f);
                    Thirst = Mathf.Min(100f, Thirst + 15f);
                    break;
            }
            Health = Mathf.Min(100f, Health + 5f);
        }

        public void Drink() => Thirst = Mathf.Max(0f, Thirst - 45f);

        /// <summary>Eating snow quenches thirst a little but chills the body.</summary>
        public void EatSnow()
        {
            Thirst = Mathf.Max(0f, Thirst - 20f);
            Chill += 12f;
        }

        public void Respawn()
        {
            Health = 60f;
            Hunger = Mathf.Min(Hunger, 40f);
            Thirst = Mathf.Min(Thirst, 40f);
            Chill = 0f;
            Array.Clear(m_DamageByCause, 0, m_DamageByCause.Length);
        }

        /// <summary>The cause draining health the fastest right now (for the wrist display).</summary>
        public Cause MainDrain
        {
            get
            {
                if (ColdRate <= 0.0001f && HungerRate <= 0f && ThirstRate <= 0f)
                    return Cause.None;
                if (ColdRate >= HungerRate && ColdRate >= ThirstRate) return Cause.Cold;
                return HungerRate >= ThirstRate ? Cause.Hunger : Cause.Thirst;
            }
        }
    }
}
