using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Untrodden
{
    /// <summary>
    /// 왼손 손목 UI: 체력, 기록, 관문, 속도, 눈보라 예고, 나침반, 알림.
    /// 나침반은 "지금 바라보는 방향 = 위쪽" 기준으로 북쪽(N)과 다음 목표(화살표)를 보여준다.
    /// </summary>
    public class WristHUD : MonoBehaviour
    {
        public Image healthFill;
        public TMP_Text timeText;
        public TMP_Text gateText;
        public TMP_Text speedText;
        public TMP_Text statusText;
        public TMP_Text noticeText;
        [Tooltip("시야 아래쪽에 잠깐 뜨는 알림(손목을 안 봐도 보이도록)")]
        public TMP_Text toastText;
        public RectTransform compassNorth;
        public RectTransform compassTarget;
        public AudioSource alertAudio;
        public float noticeDuration = 4f;

        float m_NoticeTimer;

        void OnEnable()
        {
            if (GameManager.Instance != null) GameManager.Instance.Notice += OnNotice;
        }

        void Start()
        {
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
            var gm = GameManager.Instance;
            var v = PlayerVitals.Instance;
            var course = CourseTracker.Instance;
            var ski = SkiController.Instance;

            if (healthFill && v != null) healthFill.fillAmount = Mathf.Clamp01(v.Health / v.maxHealth);

            if (timeText && gm != null)
            {
                float t = gm.ElapsedTime;
                int m = Mathf.FloorToInt(t / 60f);
                string pen = course != null && course.Penalty > 0f ? $" <color=#FF8A7A>+{course.Penalty:0}</color>" : "";
                timeText.text = $"{m}:{t - m * 60f:00.0}{pen}";
            }

            if (gateText && course != null)
                gateText.text = $"관문 {course.Passed + course.Missed}/{course.Total}";

            if (speedText && ski != null)
                speedText.text = $"{ski.Speed * 3.6f:0} km/h";

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
                statusText.text = status;
            }

            UpdateCompass(gm, course);

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

        void UpdateCompass(GameManager gm, CourseTracker course)
        {
            if (gm == null || gm.Head == null) return;
            var fwd = Vector3.ProjectOnPlane(gm.Head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();

            if (compassNorth)
                compassNorth.localRotation = Quaternion.Euler(0f, 0f, Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up) * -1f);

            if (compassTarget)
            {
                var target = course != null ? course.NextTarget() : null;
                if (target.HasValue)
                {
                    var to = Vector3.ProjectOnPlane(target.Value - gm.Head.position, Vector3.up);
                    compassTarget.gameObject.SetActive(true);
                    compassTarget.localRotation = Quaternion.Euler(0f, 0f, Vector3.SignedAngle(fwd, to, Vector3.up) * -1f);
                }
                else compassTarget.gameObject.SetActive(false);
            }
        }
    }
}
