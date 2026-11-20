using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

namespace Untrodden
{
    /// <summary>
    /// 스키 이동.
    /// - 양손 폴을 눈에 대고 뒤로 밀면 가속
    /// - 한 번 밀면 계속 미끄러진다(폴이 눈에 닿아 있기만 해서는 감속하지 않음)
    /// - 양손(주먹)이 가리키는 방향으로 스키가 천천히 돈다. 옆으로 크게 꺾을수록 감속
    /// - 경사를 따라 중력으로 미끄러지고 maxSpeed(시속 45km)까지 가속. 폴로 계속 밀면 hardMaxSpeed까지 넘어가고, 넘은 속도는 천천히 줄어든다
    /// - 나무·바위에 부딪히면 멈추고 속도에 비례해 체력이 깎인다
    /// 거의 멈춰 있을 때만 걷기(왼쪽 스틱)·회전(오른쪽 스틱)을 켠다.
    /// LocomotionProvider를 상속해 이동 중에 터널링 비네팅이 켜지게 한다.
    /// </summary>
    public class SkiController : LocomotionProvider
    {
        public static SkiController Instance { get; private set; }

        [Header("References")]
        public XROrigin rig;
        public SkiPole leftPole;
        public SkiPole rightPole;
        public Transform skis;
        public Behaviour[] walkProviders;
        public AudioSource windAudio;
        public AudioSource crashAudio;

        [Header("Speed")]
        [Tooltip("속도 상한(m/s). 비탈 중력으로는 여기까지만. 12.5 = 시속 45km")]
        public float maxSpeed = 12.5f;
        [Tooltip("폴로 계속 밀면 상한을 넘어 여기까지 올라간다. 18 = 시속 약 65km")]
        public float hardMaxSpeed = 18f;
        [Tooltip("상한을 넘은 속도가 초당 이 비율만큼 상한 쪽으로 천천히 줄어든다")]
        public float overSpeedDecay = 0.35f;
        public float friction = 0.03f;
        public float airDrag = 0.006f;
        [Tooltip("이 속도 아래에서는 정지 마찰로 멈춰 있고, 폴로 밀어야 출발한다")]
        public float startSpeed = 0.35f;
        [Tooltip("이 속도 아래면 걷기 모드")]
        public float walkModeSpeed = 0.5f;

        [Header("Poles")]
        public float pushThreshold = 0.45f;
        public float pushGain = 3f;
        [Tooltip("한쪽 폴로만 밀면 반대쪽으로 돈다. 양손 미는 세기 차이(m/s) 1당 초당 회전 각도")]
        public float pushTurnGain = 22f;
        public float maxPushTurnRate = 45f;

        [Header("Steering")]
        [Tooltip("양손(컨트롤러)이 가리키는 방향이 스키 방향과 이 각도 이상 벌어지면 그쪽으로 돈다")]
        public float steerDeadzone = 8f;
        [Tooltip("벌어진 각도 1°당 초당 회전 각도")]
        public float steerGain = 2.5f;
        public float maxTurnRate = 70f;
        [Tooltip("살짝 꺾을 때의 감속(초당 속도 비율, 꺾는 각도에 비례)")]
        public float turnSlowLight = 0.4f;
        [Tooltip("크게 꺾을 때의 감속(각도의 제곱에 비례). 60° 이상 벌어지면 light + hard")]
        public float turnSlowHard = 1.4f;

        [Header("Comfort")]
        [Tooltip("이 속도(m/s)부터 비네팅이 생기기 시작")]
        public float vignetteStartSpeed = 2f;
        [Tooltip("hardMaxSpeed일 때의 비네팅 구멍 크기(1 = 없음, 작을수록 시야가 좁아짐)")]
        public float vignetteMinAperture = 0.55f;

        [Header("Crash")]
        public float crashMinSpeed = 1.6f;
        public float crashDamagePerSpeed = 4f; // 시속 45km 충돌 ≈ 47, 65km ≈ 69 (체력 100)

        public float Speed { get; private set; }
        public int CrashCount { get; private set; }
        public bool IsSkiing => Speed >= walkModeSpeed;

        CharacterController m_Cc;
        Transform m_Head;
        Terrain m_Terrain;
        float m_VerticalSpeed;
        float m_CrashCooldown;
        bool m_Moving;
        Vector3 m_Heading = Vector3.forward; // 스키가 향한 방향(월드, 수평). 멈춰 있으면 양손 방향을 따라간다
        XRBaseInputInteractor m_LeftHaptics, m_RightHaptics;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            if (rig == null) rig = FindFirstObjectByType<XROrigin>();
            m_Cc = rig.GetComponent<CharacterController>();
            m_Head = rig.Camera.transform;
            m_Terrain = Terrain.activeTerrain;
            if (leftPole) { leftPole.Init(rig.transform); m_LeftHaptics = Haptics(leftPole); }
            if (rightPole) { rightPole.Init(rig.transform); m_RightHaptics = Haptics(rightPole); }
        }

        // 비네팅: XRI 기본은 '움직이면 항상 같은 세기'라서, 스키 항목만 세기를 직접 정하는 파라미터로 바꿔 둔다
        VignetteParameters m_Vignette;

        // 헤드셋 로그로 확인하기 위한 1초 단위 기록
        float m_StatTimer, m_StatPush, m_StatTurnLoss, m_StatMaxSpeed;

        void Start()
        {
            var ctrl = FindFirstObjectByType<TunnelingVignetteController>();
            if (ctrl == null) return;
            foreach (var p in ctrl.locomotionVignetteProviders)
            {
                if (p.locomotionProvider != this) continue;
                m_Vignette = new VignetteParameters();
                m_Vignette.CopyFrom(ctrl.defaultParameters);
                m_Vignette.apertureSize = 1f;
                p.overrideParameters = m_Vignette;
                p.overrideDefaultParameters = true;
            }
        }

        static XRBaseInputInteractor Haptics(SkiPole pole) =>
            pole.transform.parent != null ? pole.transform.parent.GetComponentInChildren<XRBaseInputInteractor>(true) : null;

        /// <summary>리스폰·순간 이동 후 속도를 0으로.</summary>
        public void Stop()
        {
            Speed = 0f;
            m_VerticalSpeed = 0f;
            leftPole?.ResetTracking();
            rightPole?.ResetTracking();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || m_Cc == null) return;
            m_CrashCooldown -= dt;

            var gm = GameManager.Instance;
            bool playing = gm != null && gm.State == GameState.Playing;

            PoleHaptics(leftPole, m_LeftHaptics);
            PoleHaptics(rightPole, m_RightHaptics);

            // 조향 기준은 고개가 아니라 양손(폴을 쥔 주먹)이 가리키는 방향. 둘러봐도 스키 방향은 그대로다.
            var hands = HandsYawForward();
            if (Speed <= 0f) m_Heading = hands; // 멈춰 있을 때는 손이 가리키는 쪽으로 스키를 돌려 둔다
            var heading = m_Heading;
            var normal = GroundNormal(m_Head.position);

            if (playing)
            {
                // ── 경사에 따른 가속 ──
                var downhill = Vector3.ProjectOnPlane(Vector3.down, normal); // 길이 = sin(경사)
                float along = Vector3.Dot(downhill, heading) * 9.81f;
                float cos = Mathf.Max(0.2f, normal.y);

                // ── 폴 ──
                float lPush = ReadPole(leftPole, m_LeftHaptics, heading);
                float rPush = ReadPole(rightPole, m_RightHaptics, heading);
                float push = lPush + rPush;

                if (Speed < startSpeed && push <= 0f)
                {
                    Speed = 0f; // 정지 마찰: 밀지 않으면 출발하지 않는다
                }
                else
                {
                    if (Speed < maxSpeed)
                    {
                        // 상한 아래: 중력·마찰·공기저항을 그대로 계산
                        Speed += along * dt;
                        Speed -= friction * 9.81f * cos * dt;
                        Speed -= airDrag * Speed * Speed * dt;
                    }
                    else
                    {
                        // 상한 위: 중력·마찰·공기저항 대신 '상한 쪽으로 천천히 돌아오는 힘'만 받는다.
                        // (예전에는 상한에서 중력만 끊고 마찰·공기저항은 남겨서, 상한에 닿는 순간 약 1.2m/s²의
                        //  감속 벽이 생겼다. 반대로 폴 추진은 상한 근처에서 30%만 먹혀 밀어도 벽을 못 넘었다.)
                        if (along < 0f) Speed += along * dt; // 오르막이면 그대로 느려짐
                        Speed -= overSpeedDecay * (Speed - maxSpeed) * dt;
                    }
                    // 폴 추진: 상한 아래에서는 빠를수록 조금 덜 먹히고(상한에서 50%), 상한 위에서는 hardMaxSpeed로 갈수록 0
                    float pushEff = Speed < maxSpeed
                        ? 1f - 0.5f * Speed / maxSpeed
                        : 0.5f * Mathf.Clamp01(1f - (Speed - maxSpeed) / (hardMaxSpeed - maxSpeed));
                    Speed += push * pushGain * pushEff * dt;
                    m_StatPush += push * dt;
                    // 한쪽만 세게 밀면 반대쪽으로 돈다(오른쪽을 밀면 왼쪽으로). 양쪽이 같으면 직진.
                    // 손 방향 조향과 달리 감속이 없고, 화면도 같이 돌려 손 방향 조향과 서로 싸우지 않게 한다.
                    if (push > 0f)
                    {
                        float turn = Mathf.Clamp((lPush - rPush) * pushTurnGain, -maxPushTurnRate, maxPushTurnRate) * dt;
                        rig.transform.RotateAround(m_Head.position, Vector3.up, turn);
                        heading = Quaternion.AngleAxis(turn, Vector3.up) * heading;
                        m_Heading = heading;
                        hands = Quaternion.AngleAxis(turn, Vector3.up) * hands;
                    }
                    // ── 조향: 스키 방향이 손 방향을 천천히 따라간다. 화면은 돌리지 않는다(몸으로 돈다) ──
                    // 폴로 미는 동안에는 손목이 꺾여 컨트롤러 방향이 흔들리므로 조향(과 그 감속)을 쉰다
                    float delta = Vector3.SignedAngle(heading, hands, Vector3.up);
                    float excess = Mathf.Abs(delta) - steerDeadzone;
                    if (excess > 0f && push <= 0f)
                    {
                        float rate = Mathf.Min(excess * steerGain, maxTurnRate);
                        float yaw = Mathf.Sign(delta) * Mathf.Min(rate * dt, excess);
                        heading = Quaternion.AngleAxis(yaw, Vector3.up) * heading;
                        m_Heading = heading;
                        // 꺾으면 조금, 크게 꺾으면 확실히 감속(눈을 긁는 느낌)
                        float t = Mathf.Clamp01(excess / 60f);
                        float slow = Speed * (turnSlowLight * t + turnSlowHard * t * t) * dt;
                        Speed -= slow;
                        m_StatTurnLoss += slow;
                    }
                    Speed = Mathf.Clamp(Speed, 0f, hardMaxSpeed);
                }

                // ── 이동 ──
                if (Speed > 0f)
                {
                    var surfaceDir = Vector3.ProjectOnPlane(heading, normal).normalized;
                    if (m_Cc.isGrounded) m_VerticalSpeed = -2f;
                    else m_VerticalSpeed -= 9.81f * dt;
                    var intended = surfaceDir * (Speed * dt);
                    var before = rig.transform.position;
                    var flags = m_Cc.Move(intended + Vector3.up * (m_VerticalSpeed * dt));
                    var moved = rig.transform.position - before;

                    // 옆면 충돌: 의도한 거리의 절반도 못 갔으면 부딪힌 것
                    float wantH = new Vector2(intended.x, intended.z).magnitude;
                    float gotH = new Vector2(moved.x, moved.z).magnitude;
                    if ((flags & CollisionFlags.Sides) != 0 && wantH > 0.001f && gotH < wantH * 0.5f)
                        Crash();
                }
            }
            else
            {
                Speed = 0f;
            }

            UpdateModes();
            UpdateSkis(heading, normal);
            if (playing) LogStats(dt);

            // 비네팅 세기는 속도에 비례(느리면 없음, 빠를수록 시야가 좁아짐)
            if (m_Vignette != null)
                m_Vignette.apertureSize = Mathf.Lerp(1f, vignetteMinAperture, Mathf.InverseLerp(vignetteStartSpeed, hardMaxSpeed, Speed));

            // 속도감: 바람 소리가 커지고 높아지며, 빠르면 스키가 눈 위에서 떨리는 진동
            float speed01 = Speed / maxSpeed; // 상한 넘으면 1 이상
            if (windAudio)
            {
                windAudio.volume = Mathf.Lerp(0f, 0.9f, Mathf.Clamp01(speed01));
                windAudio.pitch = Mathf.Lerp(0.8f, 1.6f, Mathf.Clamp01(Speed / hardMaxSpeed));
            }
            if (speed01 > 0.4f)
            {
                float amp = Mathf.Lerp(0.03f, 0.15f, Mathf.InverseLerp(0.4f, 1.4f, speed01));
                m_LeftHaptics?.SendHapticImpulse(amp, 0.05f);
                m_RightHaptics?.SendHapticImpulse(amp, 0.05f);
            }
        }

        /// <summary>폴이 눈에 닿은 채 손이 스키 방향 뒤로 빠르게 움직이면 추진. 닿아만 있는 건 아무 효과 없음.</summary>
        /// <returns>이 폴로 미는 세기(손이 뒤로 가는 속도, m/s). 안 밀면 0.</returns>
        float ReadPole(SkiPole pole, XRBaseInputInteractor haptics, Vector3 heading)
        {
            if (pole == null || !pole.InContact || !pole.isActiveAndEnabled) return 0f;
            var v = rig.transform.TransformDirection(pole.LocalHandVelocity); // 몸 기준 손 속도(월드 방향)
            float back = -Vector3.Dot(v, heading);
            if (back <= pushThreshold) return 0f;
            // 미는 동안 계속 약하게 떨려서 "밀고 있다"는 걸 알 수 있게
            haptics?.SendHapticImpulse(Mathf.Clamp(0.15f + back * 0.15f, 0f, 0.5f), 0.05f);
            return back;
        }

        /// <summary>1초마다 속도·평균 미는 세기·조향으로 잃은 속도를 로그로 남긴다(adb logcat에서 확인).</summary>
        void LogStats(float dt)
        {
            m_StatTimer += dt;
            m_StatMaxSpeed = Mathf.Max(m_StatMaxSpeed, Speed);
            if (m_StatTimer < 1f) return;
            if (m_StatMaxSpeed > 1f)
                Debug.Log($"[Untrodden] 속도 {Speed * 3.6f:0}km/h (1초 최고 {m_StatMaxSpeed * 3.6f:0}) · 평균 밀기 {m_StatPush / m_StatTimer:0.00}m/s · 조향 감속 {m_StatTurnLoss * 3.6f:0.0}km/h");
            m_StatTimer = m_StatPush = m_StatTurnLoss = m_StatMaxSpeed = 0f;
        }

        /// <summary>폴 끝이 눈에 막 닿으면 짧게 톡.</summary>
        static void PoleHaptics(SkiPole pole, XRBaseInputInteractor haptics)
        {
            if (pole != null && pole.JustTouched) haptics?.SendHapticImpulse(0.35f, 0.04f);
        }

        /// <summary>양손 컨트롤러가 가리키는 수평 방향의 평균. 손을 못 읽으면 고개 방향.</summary>
        Vector3 HandsYawForward()
        {
            var sum = HandYaw(leftPole) + HandYaw(rightPole);
            if (sum.sqrMagnitude > 0.04f) return sum.normalized;
            var f = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f) f = Vector3.ProjectOnPlane(m_Head.up, Vector3.up); // 바로 아래를 볼 때
            return f.sqrMagnitude < 1e-6f ? m_Heading : f.normalized;
        }

        static Vector3 HandYaw(SkiPole pole)
        {
            var hand = pole != null ? pole.transform.parent : null;
            if (hand == null) return Vector3.zero;
            var f = Vector3.ProjectOnPlane(hand.forward, Vector3.up);
            return f.sqrMagnitude < 0.04f ? Vector3.zero : f.normalized; // 손을 수직으로 세우면 방향을 읽지 않는다
        }

        void Crash()
        {
            if (m_CrashCooldown > 0f) return;
            m_CrashCooldown = 1f;
            float impact = Speed;
            Speed = 0f;
            if (impact < crashMinSpeed) return;

            CrashCount++;
            float damage = (impact - crashMinSpeed * 0.5f) * crashDamagePerSpeed;
            PlayerVitals.Instance?.TakeDamage(damage);
            if (crashAudio) crashAudio.Play();
            float amp = Mathf.Clamp01(impact / maxSpeed);
            m_LeftHaptics?.SendHapticImpulse(0.4f + 0.6f * amp, 0.3f);
            m_RightHaptics?.SendHapticImpulse(0.4f + 0.6f * amp, 0.3f);
            GameManager.Instance?.Say(impact > 8f ? "쾅! 크게 부딪혔어요. 폴로 밀어 다시 출발하세요." : "쿵! 부딪혔어요.");
        }

        void UpdateModes()
        {
            bool moving = IsSkiing;
            foreach (var w in walkProviders)
                if (w != null && w.enabled == moving) w.enabled = !moving;

            if (moving && !m_Moving) TryStartLocomotionImmediately();
            else if (!moving && m_Moving) TryEndLocomotion();
            m_Moving = moving;
        }

        void UpdateSkis(Vector3 heading, Vector3 normal)
        {
            if (skis == null) return;
            var p = m_Head.position;
            p.y = m_Terrain != null ? m_Terrain.SampleHeight(p) + m_Terrain.transform.position.y + 0.03f : rig.transform.position.y;
            p -= heading * 0.15f;
            var fwd = Vector3.ProjectOnPlane(heading, normal).normalized;
            skis.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, normal));
        }

        Vector3 GroundNormal(Vector3 p)
        {
            if (m_Terrain == null) return Vector3.up;
            var d = m_Terrain.terrainData;
            var local = p - m_Terrain.transform.position;
            return d.GetInterpolatedNormal(local.x / d.size.x, local.z / d.size.z);
        }
    }
}
