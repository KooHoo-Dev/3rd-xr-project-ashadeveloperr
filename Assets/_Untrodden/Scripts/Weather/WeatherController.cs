using UnityEngine;

namespace Untrodden.Weather
{
    /// <summary>
    /// Fog and snow for the taiga. Two states, Calm and Blizzard, blended over time.
    /// The fog distance doubles as the "draw distance" that later chunk streaming will hide behind.
    /// </summary>
    public class WeatherController : MonoBehaviour
    {
        [System.Serializable]
        public struct WeatherPreset
        {
            public Color fogColor;
            [Tooltip("Linear fog start (m).")] public float fogStart;
            [Tooltip("Linear fog end (m). Nothing past this is visible.")] public float fogEnd;
            [Tooltip("Snow particles spawned per second.")] public float snowRate;
            [Tooltip("Sideways wind speed (m/s) applied to snow.")] public float wind;
            [Tooltip("Directional light intensity.")] public float sunIntensity;
        }

        [Header("References")]
        [Tooltip("Head camera. Found automatically when empty.")]
        [SerializeField] Camera m_Camera;
        [SerializeField] ParticleSystem m_Snow;
        [SerializeField] Light m_Sun;

        [Header("Presets")]
        [SerializeField] WeatherPreset m_Calm = new WeatherPreset
        {
            fogColor = new Color(0.78f, 0.82f, 0.87f),
            fogStart = 4f,
            fogEnd = 60f,
            snowRate = 250f,
            wind = 0.4f,
            sunIntensity = 0.7f,
        };

        [SerializeField] WeatherPreset m_Blizzard = new WeatherPreset
        {
            fogColor = new Color(0.62f, 0.66f, 0.72f),
            fogStart = 0.5f,
            fogEnd = 16f,
            snowRate = 2200f,
            wind = 7f,
            sunIntensity = 0.25f,
        };

        [Header("Behaviour")]
        [Tooltip("Seconds to blend between Calm and Blizzard.")]
        [SerializeField] float m_TransitionSeconds = 8f;
        [Tooltip("Snow emitter height above the head (m).")]
        [SerializeField] float m_SnowHeight = 8f;

        [Header("Test only")]
        [Tooltip("Cycle Calm -> Blizzard on a timer. The game turns this off and schedules storms itself.")]
        [SerializeField] bool m_AutoCycle;
        [SerializeField] float m_CalmSeconds = 25f;
        [SerializeField] float m_BlizzardSeconds = 20f;

        float m_Blend;          // 0 = calm, 1 = blizzard
        float m_TargetBlend;
        float m_CycleTimer;

        public bool IsBlizzard => m_TargetBlend > 0.5f;

        /// <summary>0 = calm, 1 = full blizzard. Other systems (cold damage, sounds) read this.</summary>
        public float Intensity => m_Blend;

        public bool AutoCycle
        {
            get => m_AutoCycle;
            set => m_AutoCycle = value;
        }

        public void SetBlizzard(bool on, bool instant = false) => SetTarget(on ? 1f : 0f, instant ? 0f : m_TransitionSeconds);

        /// <summary>Blend towards an intensity (0 calm … 1 blizzard) over the given time.</summary>
        public void SetTarget(float intensity, float seconds)
        {
            m_TargetBlend = Mathf.Clamp01(intensity);
            m_TransitionSeconds = seconds;
            if (seconds <= 0f)
            {
                m_Blend = m_TargetBlend;
                Apply(m_Blend);
            }
        }

        void Awake()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            if (m_Camera != null)
                m_Camera.clearFlags = CameraClearFlags.SolidColor;

            Apply(m_Blend);
        }

        void Update()
        {
            if (m_AutoCycle)
            {
                m_CycleTimer += Time.deltaTime;
                var limit = IsBlizzard ? m_BlizzardSeconds : m_CalmSeconds;
                if (m_CycleTimer >= limit)
                {
                    m_CycleTimer = 0f;
                    SetBlizzard(!IsBlizzard);
                }
            }

            if (!Mathf.Approximately(m_Blend, m_TargetBlend))
            {
                var step = m_TransitionSeconds > 0f ? Time.deltaTime / m_TransitionSeconds : 1f;
                m_Blend = Mathf.MoveTowards(m_Blend, m_TargetBlend, step);
                Apply(m_Blend);
            }
        }

        void LateUpdate()
        {
            // Keep the snow volume above the player. Particles simulate in world space,
            // so flakes do not swing around when the head turns.
            if (m_Snow != null && m_Camera != null)
                m_Snow.transform.position = m_Camera.transform.position + Vector3.up * m_SnowHeight;
        }

        void Apply(float t)
        {
            // Smoothstep so the change starts and ends gently.
            var s = t * t * (3f - 2f * t);

            var fogColor = Color.Lerp(m_Calm.fogColor, m_Blizzard.fogColor, s);
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = Mathf.Lerp(m_Calm.fogStart, m_Blizzard.fogStart, s);
            RenderSettings.fogEndDistance = Mathf.Lerp(m_Calm.fogEnd, m_Blizzard.fogEnd, s);
            RenderSettings.ambientLight = fogColor * 0.9f;

            if (m_Camera != null)
            {
                // Background matches the fog so the horizon disappears instead of showing a seam.
                m_Camera.backgroundColor = fogColor;
                // Nothing past the fog is visible, so do not render it.
                m_Camera.farClipPlane = m_Calm.fogEnd + 10f;
            }

            if (m_Snow != null)
            {
                var emission = m_Snow.emission;
                emission.rateOverTime = Mathf.Lerp(m_Calm.snowRate, m_Blizzard.snowRate, s);

                var velocity = m_Snow.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                var wind = Mathf.Lerp(m_Calm.wind, m_Blizzard.wind, s);
                velocity.x = new ParticleSystem.MinMaxCurve(wind * 0.8f, wind * 1.2f);
                velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.z = new ParticleSystem.MinMaxCurve(-wind * 0.2f, wind * 0.2f);
            }

            if (m_Sun != null)
                m_Sun.intensity = Mathf.Lerp(m_Calm.sunIntensity, m_Blizzard.sunIntensity, s);
        }
    }
}
