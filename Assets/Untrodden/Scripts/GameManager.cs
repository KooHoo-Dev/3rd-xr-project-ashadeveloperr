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
    /// 게임 흐름(시작 → 플레이 → 결과)과 스테이지 이동을 관리한다.
    /// 스테이지마다 제한시간이 있고, 주행 시간 + 관문 벌점이 제한시간을 넘으면 실패다.
    /// 부딪히거나 자갈에서 느려지면 그만큼 시간을 잃으므로 결국 실패로 이어진다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Stage")]
        [Tooltip("이 씬의 스테이지 번호(0부터). 빌드 설정의 씬 순서와 같다")]
        public int stageIndex;
        [Tooltip("제한시간(초). 주행 시간 + 관문 벌점이 이 값을 넘으면 실패")]
        public float timeLimit = 90f;

        [Header("References")]
        public XROrigin rig;
        public Transform startPoint;
        public Transform goalPoint;
        public float goalRadius = 8f;
        public GameObject titleCanvas;
        public ResultScreen resultScreen;

        public GameState State { get; private set; } = GameState.Title;
        public float ElapsedTime { get; private set; }
        public bool Cleared { get; private set; }

        /// <summary>남은 시간 = 제한시간 − (주행 시간 + 관문 벌점)</summary>
        public float TimeLeft => timeLimit - ElapsedTime - (CourseTracker.Instance != null ? CourseTracker.Instance.Penalty : 0f);
        public bool HasNextStage => stageIndex + 1 < Stages.Count;

        public event Action<GameState> StateChanged;
        public event Action<string> Notice;
        /// <summary>리그가 순간 이동했을 때(코스 복귀 등). 위치 기반 판정 초기화용.</summary>
        public event Action Teleported;

        // 다른 스테이지로 넘어갈 때 시작 화면을 건너뛰고 바로 출발할지
        static bool s_AutoStart;

        Transform m_Head;

        public Transform Head => m_Head;

        void Awake()
        {
            Instance = this;
            if (rig == null) rig = FindFirstObjectByType<XROrigin>();
            m_Head = rig != null && rig.Camera != null ? rig.Camera.transform : Camera.main.transform;
        }

        void Start()
        {
            Time.timeScale = 1f; // 일시정지 중에 씬이 바뀌었더라도 새 씬은 정상 속도로
            if (resultScreen) resultScreen.gameObject.SetActive(false);
            SetState(GameState.Title);

            // 헤드셋에서 '화면 재조정'을 하면 시작 화면 앞으로 다시 맞춘다
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.trackingOriginUpdated += OnTrackingOriginUpdated;

            if (s_AutoStart)
            {
                s_AutoStart = false;
                if (titleCanvas) titleCanvas.SetActive(false);
                StartCoroutine(AutoStartWhenTracked());
            }
            else
            {
                if (titleCanvas) titleCanvas.SetActive(true);
                RequestFaceStart("시작");
            }
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
        void OnApplicationFocus(bool focus)
        {
            // Meta 버튼으로 시스템 메뉴를 열면 앱은 계속 돌지만 조작할 수 없다.
            // 그동안 시간이 흐르거나 스키가 미끄러지지 않도록 게임 시간을 멈춘다.
            // 에디터에서는 다른 창을 클릭할 때마다 멈추면 테스트가 불편하므로 기기에서만 멈춘다.
            if (!Application.isEditor) SetFocusPause(!focus);
            if (focus) RequestFaceStart("포커스 복귀");
        }
        void OnApplicationPause(bool paused) { if (!paused) RequestFaceStart("일시정지 해제"); }

        bool m_FocusPaused;

        void SetFocusPause(bool pause)
        {
            if (pause == m_FocusPaused) return;
            m_FocusPaused = pause;
            Time.timeScale = pause ? 0f : 1f;
            Debug.Log($"[Untrodden] {(pause ? "포커스 잃음, 게임 일시정지" : "포커스 복귀, 게임 재개")}");
        }

        // ── 시작 방향 맞추기 ──
        // 출발할 때 슬로프를 보게 하는 건 StartGame(시작 버튼)이 확실히 맡는다.
        // 여기서는 시작 화면에 있는 동안 배경 풍경도 대체로 정면이 되도록 보조로 맞춘다.
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

        IEnumerator WaitForTracking()
        {
            float wait = 0f;
            while (m_Head.localPosition == Vector3.zero && wait < 3f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
            // 포커스가 막 돌아온 직후에는 자세 값이 아직 이전 값일 수 있어 잠깐 기다린다
            yield return new WaitForSecondsRealtime(0.3f);
        }

        IEnumerator FaceStartWhenTracked(string reason)
        {
            yield return WaitForTracking();
            m_FaceRoutine = null;
            if (State != GameState.Title) yield break;
            ApplyEyeHeight();
            Teleport(startPoint);
            m_FaceAwayTime = 0f;
            Debug.Log($"[Untrodden] 시작 방향 맞춤 ({reason})");
        }

        IEnumerator AutoStartWhenTracked()
        {
            yield return WaitForTracking();
            StartGame();
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

            if (goalPoint != null)
            {
                var toGoal = goalPoint.position - m_Head.position;
                toGoal.y = 0f;
                if (toGoal.magnitude < goalRadius)
                {
                    // 코스 복귀 등으로 건너뛴 관문은 판정이 안 난 채 남아 있으므로 누락으로 처리해 벌점을 매긴다
                    if (CourseTracker.Instance != null) CourseTracker.Instance.ResolveRemainingAsMissed();
                    Finish(TimeLeft >= 0f);
                    return;
                }
            }
            if (TimeLeft <= 0f) Finish(false);
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
            Debug.Log($"[Untrodden] 스테이지 {stageIndex + 1} 시작, 제한시간 {timeLimit:0}초 (시작 방향 맞춤)");
            ElapsedTime = 0f;
            SetState(GameState.Playing);
            Say($"{Stages.Titles[stageIndex]} · 제한시간 {timeLimit:0}초\n폴 끝을 눈에 대고 양손을 뒤로 쭉 밀어 출발하세요!");
        }

        // ── 스테이지 이동 ──

        /// <summary>스테이지 씬을 연다. autoStart면 시작 화면 없이 바로 출발한다.</summary>
        public static void LoadStage(int index, bool autoStart)
        {
            index = Mathf.Clamp(index, 0, Stages.Count - 1);
            s_AutoStart = autoStart;
            SceneManager.LoadScene(index);
        }

        /// <summary>시작 화면의 스테이지 버튼. 지금 씬이면 바로 출발, 다른 스테이지면 그 씬을 열고 바로 출발.</summary>
        public void SelectStage(int index)
        {
            if (index == stageIndex) StartGame();
            else LoadStage(index, true);
        }

        public void Restart() => LoadStage(stageIndex, true);
        public void NextStage() => LoadStage(stageIndex + 1, true);
        public void BackToTitle() => LoadStage(stageIndex, false);

        /// <summary>길을 잃었을 때: 가장 가까운 코스 위로 돌려보낸다. 시간은 계속 흐른다.</summary>
        public void ReturnToCourse(string message)
        {
            var path = CoursePath.Instance;
            if (path != null && path.TryGetReturnPose(m_Head.position, out var pos, out var yaw))
                Teleport(pos, yaw);
            else
                Teleport(startPoint);
            Say(message);
        }

        public void Say(string message) => Notice?.Invoke(message);

        void Finish(bool cleared)
        {
            Cleared = cleared;
            SetState(GameState.Result);
            Debug.Log($"[Untrodden] 스테이지 {stageIndex + 1} {(cleared ? "클리어" : "실패")} · 주행 {ElapsedTime:0.0}초 · 남은 시간 {TimeLeft:0.0}초");
            if (resultScreen)
            {
                var course = CourseTracker.Instance;
                var ski = SkiController.Instance;
                resultScreen.Show(cleared, stageIndex, ElapsedTime,
                    course != null ? course.Penalty : 0f,
                    timeLimit,
                    course != null ? course.Passed : 0,
                    course != null ? course.Total : 0,
                    ski != null ? ski.CrashCount : 0,
                    HasNextStage);
            }
        }

        void SetState(GameState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Teleport(Transform target)
        {
            if (target == null) return;
            Teleport(target.position, target.eulerAngles.y);
        }

        /// <summary>리그를 지정 위치로 옮기고 머리가 yaw 방향을 보게 한다. CharacterController가 위치를 되돌리지 않도록 잠시 끈다.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            if (rig == null) return;
            var cc = rig.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;

            var headOffset = m_Head.position - rig.transform.position;
            headOffset.y = 0f;
            rig.transform.position = position - headOffset;

            float turn = yaw - m_Head.eulerAngles.y;
            rig.transform.RotateAround(position, Vector3.up, turn);

            Physics.SyncTransforms();
            if (cc) cc.enabled = true;
            SkiController.Instance?.Stop();
            Teleported?.Invoke();
        }
    }
}
