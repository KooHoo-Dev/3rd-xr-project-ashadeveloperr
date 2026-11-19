using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        }

        void Update()
        {
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
            ElapsedTime = 0f;
            RespawnCount = 0;
            DistanceWalked = 0f;
            m_LastHeadPos = m_Head.position;
            SetState(GameState.Playing);
            Say("모닥불에 장작을 끼우고 파이어스틱을 부딪쳐 불을 붙이세요.");
        }

        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void SetCheckpoint(Transform point)
        {
            if (point == null || point == Checkpoint) return;
            Checkpoint = point;
            Say("체크포인트 저장! 쓰러지면 여기서 다시 시작해요.");
        }

        public void Respawn()
        {
            RespawnCount++;
            Teleport(Checkpoint != null ? Checkpoint : startPoint);
            Say("추위에 쓰러졌어요. 마지막 모닥불에서 다시 시작합니다.");
        }

        public void Say(string message) => Notice?.Invoke(message);

        void Finish()
        {
            SetState(GameState.Result);
            if (resultScreen)
            {
                resultScreen.Show(ElapsedTime, RespawnCount, DistanceWalked);
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
            m_SkipDistanceFrame = true;
            m_LastHeadPos = m_Head.position;
        }
    }
}
