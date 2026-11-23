using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Untrodden
{
    /// <summary>
    /// 왼손 손목 UI: 남은 시간(막대·숫자), 관문, 속도, 눈보라, 나침반, 알림.
    /// 나침반은 "지금 바라보는 방향 = 위쪽" 기준으로 북쪽(N)과 다음 목표(화살표)를 보여준다.
    /// </summary>
    public class WristHUD : MonoBehaviour
    {
        [Tooltip("남은 시간 막대(제한시간 대비)")]
        public Image timeFill;
        public TMP_Text timeText;
        public TMP_Text gateText;
        public TMP_Text speedText;
        public TMP_Text statusText;
        public TMP_Text noticeText;
        [Tooltip("시야 아래쪽에 잠깐 뜨는 알림(손목을 안 봐도 보이도록)")]
        public TMP_Text toastText;
        [Header("Cockpit HUD (시야 옆에 고정)")]
        [Tooltip("폴을 움직이느라 손목을 볼 수 없으므로 시야 옆에 남은 시간·속도를 띄운다")]
        public TMP_Text cockpitTimeText;
        public Image cockpitTimeFill;
        public TMP_Text cockpitSpeedText;
        public TMP_Text cockpitGateText;
        public TMP_Text cockpitStatusText;

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
            var course = CourseTracker.Instance;
            var ski = SkiController.Instance;

            if (gm != null)
            {
                // 남은 시간 = 제한시간 − (주행 + 관문 벌점). 10초 아래면 붉게.
                float left = gm.State == GameState.Title ? gm.timeLimit : Mathf.Max(0f, gm.TimeLeft);
                float ratio = gm.timeLimit > 0f ? Mathf.Clamp01(left / gm.timeLimit) : 0f;
                if (timeFill)
                {
                    timeFill.fillAmount = ratio;
                    timeFill.color = left < 10f ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.35f, 0.75f, 1f);
                }
                int mm = Mathf.FloorToInt(left / 60f);
                string col = left < 10f ? "#FF6B5E" : "#FFFFFF";
                string timeStr = $"<color={col}>{mm}:{left - mm * 60f:00.0}</color>";
                if (timeText) timeText.text = timeStr;
                if (cockpitTimeText) cockpitTimeText.text = timeStr;
                if (cockpitTimeFill)
                {
                    cockpitTimeFill.fillAmount = ratio;
                    cockpitTimeFill.color = left < 10f ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.35f, 0.75f, 1f);
                }
            }

            if (course != null)
            {
                string gates = $"관문 {course.Passed + course.Missed}/{course.Total}";
                if (gateText) gateText.text = gates;
                if (cockpitGateText) cockpitGateText.text = gates;
            }

            if (ski != null)
            {
                string spd = $"{ski.Speed * 3.6f:0}";
                if (speedText) speedText.text = spd + " km/h";
                if (cockpitSpeedText) cockpitSpeedText.text = $"{spd}<size=50%> km/h</size>";
            }

            if (statusText)
            {
                string status = "";
                var b = Blizzard.Instance;
                if (b != null)
                {
                    if (b.CurrentPhase == Blizzard.Phase.Storm)
                        status = "<color=#9FD8FF>눈보라!</color>";
                }
                statusText.text = status;
                if (cockpitStatusText) cockpitStatusText.text = status;
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
