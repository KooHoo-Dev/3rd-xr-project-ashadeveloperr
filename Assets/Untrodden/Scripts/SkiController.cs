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
    /// - 나무·바위에 부딪히면 멈춘다(시간을 잃는다). 자갈 위에서는 크게 덜컹이며 느려진다
    /// - 점프대 끝에서 땅이 사라지면 공중에 뜬다. 공중에서는 조향·추진이 안 되고, 착지 순간
    ///   폴을 들고(끝을 뒤로) 양손이 진행 방향을 가리키면 깔끔한 착지(가속), 아니면 휘청(감속)·넘어짐
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
        [Tooltip("자갈 위를 지날 때 나는 소리(반복 재생, 음량만 조절)")]
        public AudioSource gravelAudio;

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

        [Header("Gravel")]
        [Tooltip("자갈 위에서의 감속(m/s²)")]
        public float gravelDecel = 6f;
        [Tooltip("자갈 위 진동 세기(0~1)")]
        public float gravelHaptic = 0.8f;
        [Tooltip("자갈이 깎아 내리는 최저 속도(m/s). 이 아래로는 깎지 않아 자갈 한가운데서도 빠져나올 수 있다")]
        public float gravelMinSpeed = 2.5f;

        [Header("Start Boost")]
        [Tooltip("출발 지점에서 처음 밀 때 이 속도(m/s)로 슬로프까지 바로 밀어 준다")]
        public float startBoostSpeed = 7f;
        [Tooltip("출발 지점에서 이 거리(m) 안에서 처음 밀 때만 부스트")]
        public float startBoostRadius = 14f;
        [Tooltip("부스트 후 이 시간(초) 동안은 조향을 막아 슬로프 쪽으로 곧게 간다")]
        public float startBoostSteerLock = 1.5f;

        [Header("Air / Landing")]
        [Tooltip("발밑 땅이 이 거리(m)보다 멀어지면 공중에 뜬 것으로 본다")]
        public float airThreshold = 0.5f;
        [Tooltip("이 속도(m/s) 이상일 때만 뜬다(작은 둔덕에서 튀지 않게)")]
        public float takeoffMinSpeed = 4f;
        [Tooltip("이보다 짧게 뜬 건 착지 판정을 하지 않는다(초)")]
        public float minAirTimeForJudge = 0.35f;
        [Tooltip("깔끔한 착지: 양손 방향과 진행 방향의 차이 허용 각도")]
        public float cleanLandingAngle = 20f;
        [Tooltip("이 각도를 넘으면 넘어진다. 그 사이는 휘청")]
        public float fallLandingAngle = 50f;
        [Tooltip("깔끔한 착지 보너스 속도(m/s)")]
        public float cleanLandingBoost = 1.5f;
        [Tooltip("휘청일 때 남는 속도 비율")]
        public float wobbleSpeedKeep = 0.55f;
        [Tooltip("공중에서 받는 중력 배율. 1이면 실제 중력이라 툭 떨어지는 느낌이라, 줄여서 '활공'하듯 오래 뜨게 한다")]
        public float airGravityScale = 0.55f;

        public float Speed { get; private set; }
        public int CrashCount { get; private set; }
        public int CleanLandings { get; private set; }
        public bool Airborne { get; private set; }
        public bool OnGravel { get; private set; }
        /// <summary>최근 충돌 정도(0~1). 붉은 테두리 연출용.</summary>
        public float CrashPulse { get; private set; }
        public bool IsSkiing => Speed >= walkModeSpeed || Airborne;

        CharacterController m_Cc;
        Transform m_Head;
        Terrain m_Terrain;
        float m_VerticalSpeed;
        float m_CrashCooldown;
        bool m_Moving;
        Vector3 m_Heading = Vector3.forward; // 스키가 향한 방향(월드, 수평). 멈춰 있으면 양손 방향을 따라간다
        Vector3 m_LastSurfaceVel;            // 땅 위에서 마지막으로 움직인 속도(경사 방향 포함) → 이륙 속도
        Vector3 m_AirVel;
        float m_AirTime;
        bool m_StartBoostUsed;
        float m_SteerLock;
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
            Airborne = false;
            m_AirVel = Vector3.zero;
            m_LastSurfaceVel = Vector3.zero;
            m_StartBoostUsed = false;
            m_SteerLock = 0f;
            leftPole?.ResetTracking();
            rightPole?.ResetTracking();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || m_Cc == null) return;
            m_CrashCooldown -= dt;
            CrashPulse = Mathf.MoveTowards(CrashPulse, 0f, dt * 1.2f);

            var gm = GameManager.Instance;
            bool playing = gm != null && gm.State == GameState.Playing;

            PoleHaptics(leftPole, m_LeftHaptics);
            PoleHaptics(rightPole, m_RightHaptics);

            // 조향 기준은 고개가 아니라 양손(폴을 쥔 주먹)이 가리키는 방향. 둘러봐도 스키 방향은 그대로다.
            var hands = HandsYawForward();
            if (Speed <= 0f) m_Heading = hands; // 멈춰 있을 때는 손이 가리키는 쪽으로 스키를 돌려 둔다
            var heading = m_Heading;
            // 발밑 지면: 지형뿐 아니라 점프대 같은 물체 위도 따라가도록 레이캐스트로 잰다
            bool hasGround = ProbeGround(out var groundHit);
            float groundGap = hasGround ? groundHit.distance - ProbeLift : float.PositiveInfinity;
            var normal = hasGround && groundGap < 1f ? groundHit.normal : GroundNormal(m_Head.position);
            OnGravel = false;

            if (playing && Airborne)
            {
                UpdateAir(dt);
                heading = m_Heading;
            }
            else if (playing)
            {
                // ── 경사에 따른 가속 ──
                // 경사 가속은 지형 기준으로 계산한다. 점프대 위에서 점프대 기울기(오르막)로 계산하면
                // 높은 점프대를 오르는 동안 속도를 크게 잃어 오히려 덜 날게 된다.
                var slopeNormal = hasGround && !(groundHit.collider is TerrainCollider) ? GroundNormal(m_Head.position) : normal;
                var downhill = Vector3.ProjectOnPlane(Vector3.down, slopeNormal); // 길이 = sin(경사)
                float along = Vector3.Dot(downhill, heading) * 9.81f;
                float cos = Mathf.Max(0.2f, normal.y);

                // ── 폴 ──
                float lPush = ReadPole(leftPole, m_LeftHaptics, heading);
                float rPush = ReadPole(rightPole, m_RightHaptics, heading);
                float push = lPush + rPush;
                m_SteerLock -= dt;

                // ── 출발 부스트: 출발 지점에서 처음 밀면 슬로프 쪽으로 곧게 밀어 준다(평지를 여러 번 밀지 않게) ──
                var startPt = gm.startPoint;
                if (!m_StartBoostUsed && push > 0f && startPt != null)
                {
                    var toStart = startPt.position - m_Head.position;
                    toStart.y = 0f;
                    if (toStart.magnitude < startBoostRadius)
                    {
                        m_StartBoostUsed = true;
                        var dir = Vector3.ProjectOnPlane(startPt.forward, Vector3.up).normalized;
                        heading = m_Heading = dir;
                        Speed = Mathf.Max(Speed, startBoostSpeed);
                        m_SteerLock = startBoostSteerLock;
                        Debug.Log("[Untrodden] 출발 부스트");
                    }
                }

                if (Speed < startSpeed && push <= 0f && !(OnGravelNow() && Speed > 0f))
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
                    if (push > 0f && m_SteerLock <= 0f)
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
                    if (excess > 0f && push <= 0f && m_SteerLock <= 0f)
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
                    // ── 자갈: 크게 덜컹이며 느려진다 ──
                    if (Speed > 0f && GravelPatch.Contains(m_Head.position))
                    {
                        OnGravel = true;
                        // 최저 속도 아래로는 깎지 않는다(한가운데 빠져도 천천히 빠져나올 수 있게)
                        if (Speed > gravelMinSpeed) Speed = Mathf.Max(gravelMinSpeed, Speed - gravelDecel * dt);
                        float amp = gravelHaptic * (0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 18f, 0f));
                        m_LeftHaptics?.SendHapticImpulse(amp, 0.06f);
                        m_RightHaptics?.SendHapticImpulse(amp, 0.06f);
                    }
                    Speed = Mathf.Clamp(Speed, 0f, hardMaxSpeed);
                }

                // ── 이동 ──
                if (Speed > 0f)
                {
                    var surfaceDir = Vector3.ProjectOnPlane(heading, normal).normalized;

                    // ── 이륙: 빠르게 달리다 발밑 땅이 사라지면(점프대 끝) 공중으로 ──
                    if (!m_Cc.isGrounded && Speed >= takeoffMinSpeed && groundGap > airThreshold)
                    {
                        TakeOff();
                        UpdateModes();
                        return;
                    }

                    if (m_Cc.isGrounded) m_VerticalSpeed = -2f;
                    else m_VerticalSpeed -= 9.81f * dt;
                    var intended = surfaceDir * (Speed * dt);
                    m_LastSurfaceVel = surfaceDir * Speed;
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

            if (gravelAudio) gravelAudio.volume = Mathf.MoveTowards(gravelAudio.volume, OnGravel ? 0.9f : 0f, dt * 6f);

            UpdateModes();
            UpdateSkis(heading, normal, groundHit, hasGround && groundGap < 1f);
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

        void Crash(string message = null)
        {
            if (m_CrashCooldown > 0f) return;
            m_CrashCooldown = 1f;
            float impact = Speed;
            Speed = 0f;
            m_AirVel.x = m_AirVel.z = 0f;
            if (impact < crashMinSpeed && message == null) return;

            CrashCount++;
            CrashPulse = 1f;
            if (crashAudio) crashAudio.Play();
            float amp = Mathf.Clamp01(impact / maxSpeed);
            m_LeftHaptics?.SendHapticImpulse(0.4f + 0.6f * amp, 0.3f);
            m_RightHaptics?.SendHapticImpulse(0.4f + 0.6f * amp, 0.3f);
            GameManager.Instance?.Say(message ?? (impact > 8f ? "쾅! 크게 부딪혔어요. 폴로 밀어 다시 출발하세요." : "쿵! 부딪혔어요."));
        }

        bool OnGravelNow() => GravelPatch.Contains(m_Head.position);

        // ── 공중·착지 ──

        const float ProbeLift = 0.5f; // 레이를 발보다 이만큼 위에서 쏜다(캐릭터 컨트롤러 안쪽이라 자기 자신은 맞지 않음)

        bool ProbeGround(out RaycastHit hit)
        {
            var origin = new Vector3(m_Head.position.x, rig.transform.position.y + ProbeLift, m_Head.position.z);
            return Physics.Raycast(origin, Vector3.down, out hit, ProbeLift + 6f, ~0, QueryTriggerInteraction.Ignore);
        }

        void TakeOff()
        {
            Airborne = true;
            m_AirTime = 0f;
            m_AirVel = m_LastSurfaceVel.sqrMagnitude > 0.01f ? m_LastSurfaceVel : m_Heading * Speed;
            Debug.Log($"[Untrodden] 이륙 {Speed * 3.6f:0}km/h, 상승 {m_AirVel.y:0.0}m/s");
        }

        void UpdateAir(float dt)
        {
            m_AirTime += dt;
            m_AirVel += Vector3.down * (9.81f * airGravityScale * dt);
            var flags = m_Cc.Move(m_AirVel * dt);
            var hv = new Vector3(m_AirVel.x, 0f, m_AirVel.z);
            Speed = hv.magnitude;
            if (hv.sqrMagnitude > 0.01f) m_Heading = hv.normalized;

            // 공중에서 바위·나무 옆면에 부딪힘
            if ((flags & CollisionFlags.Sides) != 0 && Speed > crashMinSpeed)
                Crash("공중에서 부딪혔어요!");

            bool touched = (flags & CollisionFlags.Below) != 0
                           || (m_AirVel.y <= 0f && ProbeGround(out var h) && h.distance - ProbeLift < 0.05f);
            if (touched) Land();
        }

        /// <summary>
        /// 착지 판정. 폴을 들고(끝을 뒤로) 양손이 진행 방향을 가리키면 깔끔한 착지(속도 보너스),
        /// 폴을 짚고 있거나 방향이 조금 틀어지면 휘청(감속), 크게 틀어지면 넘어짐.
        /// </summary>
        void Land()
        {
            Airborne = false;
            m_VerticalSpeed = -2f;
            var hv = new Vector3(m_AirVel.x, 0f, m_AirVel.z);
            float hs = hv.magnitude;
            var dir = hs > 0.1f ? hv / hs : m_Heading;
            m_Heading = dir;
            m_AirVel = Vector3.zero;
            Speed = hs;
            if (m_AirTime < minAirTimeForJudge) return; // 작은 둔덕

            float angle = Vector3.Angle(HandsYawForward(), dir);
            bool polesUp = !(leftPole != null && leftPole.InContact) && !(rightPole != null && rightPole.InContact);
            string verdict;
            if (angle > fallLandingAngle)
            {
                verdict = "넘어짐";
                Crash("넘어졌어요! 착지할 때는 양손으로 진행 방향을 가리키세요.");
            }
            else if (angle <= cleanLandingAngle && polesUp)
            {
                verdict = "깔끔";
                CleanLandings++;
                Speed = Mathf.Min(hs + cleanLandingBoost, hardMaxSpeed);
                m_LeftHaptics?.SendHapticImpulse(0.45f, 0.12f);
                m_RightHaptics?.SendHapticImpulse(0.45f, 0.12f);
                GameManager.Instance?.Say("깔끔한 착지! 가속");
            }
            else
            {
                verdict = "휘청";
                Speed = hs * wobbleSpeedKeep;
                m_LeftHaptics?.SendHapticImpulse(0.85f, 0.25f);
                m_RightHaptics?.SendHapticImpulse(0.85f, 0.25f);
                GameManager.Instance?.Say(polesUp ? "휘청! 진행 방향을 바라보고 내려야 해요." : "휘청! 착지할 때는 폴 끝을 뒤로 들어요.");
            }
            Debug.Log($"[Untrodden] 착지 {verdict} · 체공 {m_AirTime:0.00}초 · 방향 차이 {angle:0}° · 폴 {(polesUp ? "듦" : "짚음")} · {hs * 3.6f:0}→{Speed * 3.6f:0}km/h");
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

        void UpdateSkis(Vector3 heading, Vector3 normal, RaycastHit groundHit, bool useHit)
        {
            if (skis == null) return;
            var p = m_Head.position;
            if (Airborne) { p.y = rig.transform.position.y + 0.03f; normal = Vector3.up; }
            else if (useHit) p.y = groundHit.point.y + 0.03f;
            else p.y = m_Terrain != null ? m_Terrain.SampleHeight(p) + m_Terrain.transform.position.y + 0.03f : rig.transform.position.y;
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
