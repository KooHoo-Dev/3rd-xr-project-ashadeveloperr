using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 랜덤 타이머로 눈보라가 찾아온다. 30초 전에 예고(바람 소리 증가·하늘 어두워짐·손목 알림)하고,
    /// 눈보라 중에는 체온 감소 배율이 커지고 안개가 짙어진다.
    /// </summary>
    public class Blizzard : MonoBehaviour
    {
        public enum Phase { Calm, Warning, Storm }

        public static Blizzard Instance { get; private set; }

        public Vector2 calmDuration = new(35f, 60f);
        public float warningDuration = 20f;
        public float stormDuration = 30f;

        [Header("Atmosphere")]
        public ParticleSystem snow;
        public float calmSnowRate = 60f;
        public float stormSnowRate = 900f;
        public AudioSource wind;
        public Light sun;
        public Color calmFog = new(0.72f, 0.78f, 0.85f);
        public Color stormFog = new(0.86f, 0.88f, 0.9f);
        public float calmFogStart = 4f;
        public float calmFogEnd = 42f;
        public float stormFogEnd = 16f;
        [Tooltip("눈 파티클이 바람에 옆으로 날리는 속도(평소 → 눈보라)")]
        public Vector2 windDrift = new(0.8f, 7f);
        public Camera playerCamera;

        public Phase CurrentPhase { get; private set; } = Phase.Calm;
        public float PhaseTimeLeft { get; private set; }
        /// <summary>0 = 맑음, 1 = 최대 눈보라. 연출 보간에 쓴다.</summary>
        public float Intensity { get; private set; }
        /// <summary>눈보라 피해 강도(0~1). 눈보라 단계에서만 0보다 크다.</summary>
        public float StormStrength => CurrentPhase == Phase.Storm ? Intensity : 0f;

        float m_SunBase;

        void Awake()
        {
            Instance = this;
            PhaseTimeLeft = Random.Range(calmDuration.x, calmDuration.y);
            if (sun) m_SunBase = sun.intensity;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            bool playing = gm != null && gm.State == GameState.Playing;

            if (playing)
            {
                PhaseTimeLeft -= Time.deltaTime;
                if (PhaseTimeLeft <= 0f) Advance();
            }

            float target = CurrentPhase switch
            {
                Phase.Storm => 1f,
                Phase.Warning => 0.25f * (1f - PhaseTimeLeft / warningDuration),
                _ => 0f,
            };
            Intensity = Mathf.MoveTowards(Intensity, target, Time.deltaTime * 0.15f);
            ApplyAtmosphere();
        }

        void Advance()
        {
            switch (CurrentPhase)
            {
                case Phase.Calm:
                    CurrentPhase = Phase.Warning;
                    PhaseTimeLeft = warningDuration;
                    GameManager.Instance?.Say($"바람이 거세져요. {warningDuration:0}초 뒤 눈보라! 서둘러 내려가세요.");
                    break;
                case Phase.Warning:
                    CurrentPhase = Phase.Storm;
                    PhaseTimeLeft = stormDuration;
                    GameManager.Instance?.Say("눈보라가 몰아칩니다! 체력이 줄어요.");
                    break;
                default:
                    CurrentPhase = Phase.Calm;
                    PhaseTimeLeft = Random.Range(calmDuration.x, calmDuration.y);
                    GameManager.Instance?.Say("눈보라가 지나갔어요.");
                    break;
            }
        }

        void ApplyAtmosphere()
        {
            var fog = Color.Lerp(calmFog, stormFog, Intensity);
            RenderSettings.fogColor = fog;
            RenderSettings.fogEndDistance = Mathf.Lerp(calmFogEnd, stormFogEnd, Intensity);
            RenderSettings.fogStartDistance = Mathf.Lerp(calmFogStart, 1.5f, Intensity);
            if (playerCamera)
            {
                playerCamera.backgroundColor = fog;
                // 안개 너머는 어차피 안 보이므로 렌더링 거리도 함께 줄인다
                playerCamera.farClipPlane = RenderSettings.fogEndDistance + 4f;
            }

            if (snow)
            {
                var emission = snow.emission;
                emission.rateOverTime = Mathf.Lerp(calmSnowRate, stormSnowRate, Intensity);
                var vel = snow.velocityOverLifetime;
                float drift = Mathf.Lerp(windDrift.x, windDrift.y, Intensity);
                vel.x = new ParticleSystem.MinMaxCurve(drift * 0.5f, drift * 1.3f);
            }
            if (wind) wind.volume = Mathf.Lerp(0.15f, 0.9f, Mathf.Max(Intensity, CurrentPhase == Phase.Warning ? 0.45f : 0f));
            if (sun) sun.intensity = Mathf.Lerp(m_SunBase, m_SunBase * 0.35f, Intensity);
        }
    }
}
