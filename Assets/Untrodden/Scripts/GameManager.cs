using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Untrodden
{
    public enum GameState { Title, Playing, Result }

    /// <summary>
    /// 게임 흐름(시작 → 플레이 → 결과), 체크포인트, 결과 집계(시간·리스폰·이동 거리)를 관리한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("References")]
        public XROrigin rig;
        public Transform startPoint;
        public Transform goalPoint;
        public float goalRadius = 8f;
        public GameObject titleCanvas;
        public ResultScreen resultScreen;

        public GameState State { get; private set; } = GameState.Title;
        public float ElapsedTime { get; private set; }
        public int RespawnCount { get; private set; }
        public float DistanceWalked { get; private set; }
        public Transform Checkpoint { get; private set; }

        public event Action<GameState> StateChanged;
        public event Action<string> Notice;
        /// <summary>리그가 순간 이동했을 때(리스폰·복귀). 위치 기반 판정 초기화용.</summary>
        public event Action Teleported;

        Transform m_Head;
        Vector3 m_LastHeadPos;
        bool m_SkipDistanceFrame;

        public Transform Head => m_Head;

        void Awake()
        {
            Instance = this;
            if (rig == null) rig = FindFirstObjectByType<XROrigin>();
            m_Head = rig != null && rig.Camera != null ? rig.Camera.transform : Camera.main.transform;
            Checkpoint = startPoint;
        }

        void Start()
        {
            if (titleCanvas) titleCanvas.SetActive(true);
            if (resultScreen) resultScreen.gameObject.SetActive(false);
            m_LastHeadPos = m_Head.position;
            SetState(GameState.Title);
            RequestFaceStart("시작");

            // 헤드셋에서 '화면 재조정'을 하면 시작 화면 앞으로 다시 맞춘다
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        void OnDestroy()
        {
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
        }

        void OnTrackingOriginUpdated(XRInputSubsystem _)
        {
            // 재조정되면 XR Origin이 카메라 높이를 다시 초기화하므로 게임 중에도 눈높이를 다시 맞춘다
            StartCoroutine(ApplyEyeHeightSoon());
            RequestFaceStart("화면 재조정");
        }

        // ── 눈높이 고정 ──
        // 트래킹 기준이 '앱을 켠 순간의 헤드셋 위치'라서, 헤드셋이 책상 위에 있을 때 켜면 쓰고 난 뒤 시점이 높아지고
        // 서서/앉아서에 따라서도 달라진다. 그래서 실제 머리 높이와 상관없이 눈이 지면에서 eyeHeight에 오도록
        // 카메라 오프셋을 직접 맞춘다.
        [Tooltip("지면에서 눈까지 높이(m). 앉아서 하든 서서 하든 이 높이로 보인다")]
        public float eyeHeight = 1.6f;

        void ApplyEyeHeight()
        {
            if (rig == null || rig.CameraFloorOffsetObject == null) return;
            var off = rig.CameraFloorOffsetObject.transform;
            var p = off.localPosition;
            p.y = eyeHeight - m_Head.localPosition.y;
            off.localPosition = p;
            Debug.Log($"[Untrodden] 눈높이 {eyeHeight:0.00}m로 맞춤 (실제 머리 높이 {m_Head.localPosition.y:0.00}m)");
        }

        IEnumerator ApplyEyeHeightSoon()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (State != GameState.Title) ApplyEyeHeight(); // 시작 화면이면 FaceStartWhenTracked가 맞춘다
        }

        // 헤드셋을 벗었다 쓰거나 권한 팝업 같은 시스템 창이 닫히면 앱이 포커스를 다시 받는다.
        void OnApplicationFocus(bool focus) { if (focus) RequestFaceStart("포커스 복귀"); }
        void OnApplicationPause(bool paused) { if (!paused) RequestFaceStart("일시정지 해제"); }

        // ── 시작 방향 맞추기 ──
        // 방향은 '맞추는 그 순간' 고개가 향한 쪽을 기준으로 정해진다. 앱을 켠 순간 한 번만 맞추면,
        // 그때 헤드셋이 책상 위에 있었거나 시스템 팝업을 보고 있었을 경우 실제로 쓰고 나서는 엉뚱한 쪽을 보게 된다.
        // 그래서 시작 화면에 있는 동안은 (1) 포커스 복귀·화면 재조정 때마다 다시 맞추고
        // (2) 시작 화면이 시야 뒤쪽에 1.5초 넘게 있으면 다시 맞춘다.
        const float FaceAwayAngle = 100f;
        const float FaceAwaySeconds = 1.5f;
        Coroutine m_FaceRoutine;
        float m_FaceAwayTime;

        void RequestFaceStart(string reason)
        {
            if (State != GameState.Title || m_Head == null || !isActiveAndEnabled) return;
            if (m_FaceRoutine != null) StopCoroutine(m_FaceRoutine);
            m_FaceRoutine = StartCoroutine(FaceStartWhenTracked(reason));
        }

        /// <summary>
        /// 헤드셋 트래킹이 안정되면 플레이어를 출발 지점에 세우고 슬로프 아래(시작 화면 쪽)를 보게 한다.
        /// 실제 방에서 어느 쪽을 보고 있든 고개를 돌리지 않아도 된다.
        /// </summary>
        IEnumerator FaceStartWhenTracked(string reason)
        {
            float wait = 0f;
            while (m_Head.localPosition == Vector3.zero && wait < 3f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
            // 포커스가 막 돌아온 직후에는 자세 값이 아직 이전 값일 수 있어 잠깐 기다린다
            yield return new WaitForSecondsRealtime(0.3f);
            m_FaceRoutine = null;
            if (State != GameState.Title) yield break;
            ApplyEyeHeight();
            Teleport(startPoint);
            m_FaceAwayTime = 0f;
            Debug.Log($"[Untrodden] 시작 방향 맞춤 ({reason})");
        }

        /// <summary>시작 화면에서 출발 방향을 오래 등지고 있으면 다시 맞춘다(안전망).</summary>
        void WatchFacingOnTitle()
        {
            if (startPoint == null || m_FaceRoutine != null) return;
            var f = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f) return; // 바로 위·아래를 볼 때는 판단하지 않음
            float off = Mathf.Abs(Mathf.DeltaAngle(Quaternion.LookRotation(f).eulerAngles.y, startPoint.eulerAngles.y));
            m_FaceAwayTime = off > FaceAwayAngle ? m_FaceAwayTime + Time.unscaledDeltaTime : 0f;
            if (m_FaceAwayTime > FaceAwaySeconds) RequestFaceStart("등지고 있음");
        }

        void Update()
        {
            if (State == GameState.Title) WatchFacingOnTitle();
            if (State != GameState.Playing) return;

            ElapsedTime += Time.deltaTime;

            var pos = m_Head.position;
            var delta = pos - m_LastHeadPos;
            delta.y = 0f;
            if (!m_SkipDistanceFrame && delta.magnitude < 30f)
                DistanceWalked += delta.magnitude;
            m_SkipDistanceFrame = false;
            m_LastHeadPos = pos;

            if (goalPoint != null)
            {
                var toGoal = goalPoint.position - pos;
                toGoal.y = 0f;
                if (toGoal.magnitude < goalRadius)
                    Finish();
            }
        }

        public void StartGame()
        {
            if (State != GameState.Title) return;
            if (titleCanvas) titleCanvas.SetActive(false);
            // 시작 버튼을 누르는 이 순간에는 확실히 헤드셋을 쓰고 화면을 보고 있다.
            // 이벤트로 맞춘 방향이 그 뒤에 시스템(정지형 경계 재설정 등)에 의해 알림 없이 틀어졌더라도
            // 여기서 한 번 더 맞추면 출발할 때는 항상 슬로프 아래를 보게 된다.
            if (m_FaceRoutine != null) { StopCoroutine(m_FaceRoutine); m_FaceRoutine = null; }
            ApplyEyeHeight();
            Teleport(startPoint);
            Debug.Log("[Untrodden] 시작 방향 맞춤 (시작 버튼)");
            ElapsedTime = 0f;
            RespawnCount = 0;
            DistanceWalked = 0f;
            m_LastHeadPos = m_Head.position;
            SetState(GameState.Playing);
            Say("손을 내려 폴 끝을 눈에 대면 손이 톡 떨려요. 닿은 채로 양손을 뒤로 쭉 밀어 출발하세요!");
        }

        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void SetCheckpoint(Transform point, bool silent = false)
        {
            if (point == null || point == Checkpoint) return;
            Checkpoint = point;
            if (!silent) Say("체크포인트 저장! 쓰러지면 여기서 다시 시작해요.");
        }

        public void Respawn()
        {
            RespawnCount++;
            Teleport(Checkpoint != null ? Checkpoint : startPoint);
            Say("쓰러졌어요. 마지막 모닥불에서 다시 출발합니다.");
        }

        /// <summary>쓰러진 게 아니라 길을 잃었을 때: 리스폰 횟수는 세지 않고 마지막 체크포인트로 옮긴다.</summary>
        public void ReturnToCheckpoint(string message)
        {
            Teleport(Checkpoint != null ? Checkpoint : startPoint);
            Say(message);
        }

        public void Say(string message) => Notice?.Invoke(message);

        void Finish()
        {
            SetState(GameState.Result);
            if (resultScreen)
            {
                var course = CourseTracker.Instance;
                var ski = SkiController.Instance;
                resultScreen.Show(ElapsedTime,
                    course != null ? course.Penalty : 0f,
                    course != null ? course.Passed : 0,
                    course != null ? course.Total : 0,
                    ski != null ? ski.CrashCount : 0,
                    RespawnCount);
            }
        }

        void SetState(GameState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        /// <summary>리그를 지정 위치로 옮긴다. CharacterController가 위치를 되돌리지 않도록 잠시 끈다.</summary>
        public void Teleport(Transform target)
        {
            if (rig == null || target == null) return;
            var cc = rig.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;

            var headOffset = m_Head.position - rig.transform.position;
            headOffset.y = 0f;
            rig.transform.position = target.position - headOffset;

            var yaw = target.eulerAngles.y - m_Head.eulerAngles.y;
            rig.transform.RotateAround(target.position, Vector3.up, yaw);

            Physics.SyncTransforms();
            if (cc) cc.enabled = true;
            SkiController.Instance?.Stop();
            Teleported?.Invoke();
            m_SkipDistanceFrame = true;
            m_LastHeadPos = m_Head.position;
        }
    }
}
