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

        public Vector2 calmDuration = new(70f, 110f);
        public float warningDuration = 30f;
        public float stormDuration = 40f;
        public float stormColdMultiplier = 3.5f;

        [Header("Atmosphere")]
        public ParticleSystem snow;
        public float calmSnowRate = 60f;
        public float stormSnowRate = 900f;
        public AudioSource wind;
        public Light sun;
        public Color calmFog = new(0.72f, 0.78f, 0.85f);
        public Color stormFog = new(0.86f, 0.88f, 0.9f);
        public float calmFogEnd = 65f;
        public float stormFogEnd = 22f;
        public Camera playerCamera;

        public Phase CurrentPhase { get; private set; } = Phase.Calm;
        public float PhaseTimeLeft { get; private set; }
        /// <summary>0 = 맑음, 1 = 최대 눈보라. 연출 보간에 쓴다.</summary>
        public float Intensity { get; private set; }
        public float ColdMultiplier => Mathf.Lerp(1f, stormColdMultiplier, CurrentPhase == Phase.Storm ? Intensity : 0f);

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
                    GameManager.Instance?.Say("바람이 거세져요. 30초 뒤 눈보라! 모닥불이나 바위 그늘을 찾으세요.");
                    break;
                case Phase.Warning:
                    CurrentPhase = Phase.Storm;
                    PhaseTimeLeft = stormDuration;
                    GameManager.Instance?.Say("눈보라가 몰아칩니다!");
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
            RenderSettings.fogStartDistance = Mathf.Lerp(8f, 2f, Intensity);
            if (playerCamera) playerCamera.backgroundColor = fog;

            if (snow)
            {
                var emission = snow.emission;
                emission.rateOverTime = Mathf.Lerp(calmSnowRate, stormSnowRate, Intensity);
            }
            if (wind) wind.volume = Mathf.Lerp(0.15f, 0.9f, Mathf.Max(Intensity, CurrentPhase == Phase.Warning ? 0.45f : 0f));
            if (sun) sun.intensity = Mathf.Lerp(m_SunBase, m_SunBase * 0.35f, Intensity);
        }
    }
}
