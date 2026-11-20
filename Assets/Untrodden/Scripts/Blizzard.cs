using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 눈보라가 예고 없이 들이닥쳤다가 금방 지나간다(짧고 자주). 눈보라 중에는 안개가 짙어져 앞이 잘 안 보이고
    /// 눈이 옆으로 거세게 날린다. 체력은 없으므로 피해는 '시야'뿐이고, 그만큼 느려지면 제한시간을 잃는다.
    /// </summary>
    public class Blizzard : MonoBehaviour
    {
        public enum Phase { Calm, Storm }

        public static Blizzard Instance { get; private set; }

        [Tooltip("맑은 시간(초) 범위. 이 사이에서 무작위로 정해진다")]
        public Vector2 calmDuration = new(12f, 25f);
        [Tooltip("눈보라가 이어지는 시간(초) 범위")]
        public Vector2 stormDuration = new(8f, 12f);
        [Tooltip("눈보라가 들이닥치는 빠르기(초당 강도 변화). 클수록 갑자기 덮친다")]
        public float onsetSpeed = 0.8f;
        [Tooltip("눈보라가 걷히는 빠르기(초당 강도 변화)")]
        public float clearSpeed = 0.35f;

        /// <summary>눈 파티클 한 겹. 평소 ↔ 눈보라 사이에서 양과 바람을 보간한다.</summary>
        [System.Serializable]
        public class SnowLayer
        {
            public ParticleSystem system;
            public float calmRate;
            public float stormRate;
            [Tooltip("바람 세기 배율(눈가루 덩어리는 더 빨리 날린다)")]
            public float windScale = 1f;
        }
        [Header("Atmosphere")]
        [Tooltip("Blizzard 셰이더(원본: 3rd-study-shader 02_Blizzard)를 쓰는 눈 레이어들")]
        public SnowLayer[] snowLayers;
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

            bool storm = CurrentPhase == Phase.Storm;
            Intensity = Mathf.MoveTowards(Intensity, storm ? 1f : 0f, Time.deltaTime * (storm ? onsetSpeed : clearSpeed));
            ApplyAtmosphere();
        }

        void Advance()
        {
            switch (CurrentPhase)
            {
                case Phase.Calm:
                    // 예고 없이 들이닥친다
                    CurrentPhase = Phase.Storm;
                    PhaseTimeLeft = Random.Range(stormDuration.x, stormDuration.y);
                    // 알림 문구는 띄우지 않는다: 콕핏 HUD에 '눈보라!'가 표시되고, 짙어지는 안개와 바람 소리로 충분하다
                    break;
                default:
                    CurrentPhase = Phase.Calm;
                    PhaseTimeLeft = Random.Range(calmDuration.x, calmDuration.y);
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

            if (snowLayers != null)
            {
                float drift = Mathf.Lerp(windDrift.x, windDrift.y, Intensity);
                foreach (var layer in snowLayers)
                {
                    if (layer == null || layer.system == null) continue;
                    var emission = layer.system.emission;
                    emission.rateOverTime = Mathf.Lerp(layer.calmRate, layer.stormRate, Intensity);
                    var vel = layer.system.velocityOverLifetime;
                    float d = drift * layer.windScale;
                    vel.x = new ParticleSystem.MinMaxCurve(d * 0.5f, d * 1.3f);
                }
            }
            if (wind) wind.volume = Mathf.Lerp(0.15f, 0.9f, Mathf.Max(Intensity, 0f));
            if (sun) sun.intensity = Mathf.Lerp(m_SunBase, m_SunBase * 0.35f, Intensity);
        }
    }
}
