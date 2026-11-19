using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Untrodden
{
    /// <summary>왼손 손목 UI: 체력·체온·허기 막대, 경과 시간, 눈보라 예고, 알림 메시지.</summary>
    public class WristHUD : MonoBehaviour
    {
        public Image healthFill;
        public Image warmthFill;
        public Image foodFill;
        public TMP_Text timeText;
        public TMP_Text statusText;
        public TMP_Text noticeText;
        [Tooltip("시야 아래쪽에 잠깐 뜨는 알림(손목을 안 봐도 보이도록)")]
        public TMP_Text toastText;
        public AudioSource alertAudio;
        public float noticeDuration = 5f;

        float m_NoticeTimer;
        Blizzard.Phase m_LastPhase;

        void OnEnable()
        {
            if (GameManager.Instance != null) GameManager.Instance.Notice += OnNotice;
        }

        void Start()
        {
            // GameManager.Awake가 먼저 실행되지만 OnEnable 순서를 보장하기 위해 한 번 더 연결
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Notice -= OnNotice;
                GameManager.Instance.Notice += OnNotice;
            }
            if (noticeText) noticeText.text = "";
            if (toastText) toastText.text = "";
        }

        void OnDisable()
        {
            if (GameManager.Instance != null) GameManager.Instance.Notice -= OnNotice;
        }

        void OnNotice(string message)
        {
            if (noticeText) noticeText.text = message;
            if (toastText) toastText.text = message;
            m_NoticeTimer = noticeDuration;
            if (alertAudio) alertAudio.Play();
        }

        void Update()
        {
            var v = PlayerVitals.Instance;
            if (v != null)
            {
                Set(healthFill, v.Health / v.maxHealth);
                Set(warmthFill, v.Warmth / v.maxWarmth);
                Set(foodFill, v.Food / v.maxFood);
            }

            var gm = GameManager.Instance;
            if (gm != null && timeText)
            {
                int t = Mathf.FloorToInt(gm.ElapsedTime);
                timeText.text = $"{t / 60:00}:{t % 60:00}";
            }

            if (statusText)
            {
                string status = "";
                var b = Blizzard.Instance;
                if (b != null)
                {
                    if (b.CurrentPhase == Blizzard.Phase.Warning)
                        status = $"<color=#FFB347>눈보라까지 {Mathf.CeilToInt(b.PhaseTimeLeft)}초</color>";
                    else if (b.CurrentPhase == Blizzard.Phase.Storm)
                        status = "<color=#9FD8FF>눈보라!</color>";
                }
                if (v != null)
                {
                    if (v.NearFire) status += (status.Length > 0 ? "  " : "") + "<color=#FF9A3C>모닥불</color>";
                    else if (v.InShelter) status += (status.Length > 0 ? "  " : "") + "그늘";
                    if (v.Warmth <= 0f) status += "  <color=#7FC8FF>얼어붙는 중</color>";
                    if (v.Food <= 0f) status += "  <color=#E0C060>굶주림</color>";
                }
                statusText.text = status;
            }

            if (m_NoticeTimer > 0f)
            {
                m_NoticeTimer -= Time.deltaTime;
                if (m_NoticeTimer <= 0f)
                {
                    if (noticeText) noticeText.text = "";
                    if (toastText) toastText.text = "";
                }
            }
        }

        static void Set(Image img, float t)
        {
            if (img == null) return;
            img.fillAmount = Mathf.Clamp01(t);
        }
    }
}
