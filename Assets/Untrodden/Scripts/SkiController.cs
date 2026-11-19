using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Untrodden
{
    /// <summary>
    /// 스키 이동.
    /// - 양손 폴을 눈에 대고 뒤로 밀면 가속
    /// - 한쪽 폴을 눈에 대고 끌면(손을 거의 멈춤) 그쪽으로 회전, 양쪽을 끌면 브레이크
    /// - 경사를 따라 중력으로 미끄러지고, 최고 속도는 maxSpeed
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
        public float maxSpeed = 8.33f; // 시속 30km
        public float friction = 0.05f;
        public float airDrag = 0.035f;
        [Tooltip("이 속도 아래에서는 정지 마찰로 멈춰 있고, 폴로 밀어야 출발한다")]
        public float startSpeed = 0.35f;
        [Tooltip("이 속도 아래면 걷기 모드")]
        public float walkModeSpeed = 0.5f;

        [Header("Poles")]
        public float pushThreshold = 0.45f;
        public float pushGain = 2.4f;
        [Tooltip("폴로 밀어서 낼 수 있는 최고 속도(그 이상은 중력으로만)")]
        public float pushMaxSpeed = 6.5f;
        public float dragHandSpeed = 0.6f;
        public float turnRate = 50f;
        public float dragDecel = 0.7f;
        public float brakeDecel = 4.5f;

        [Header("Crash")]
        public float crashMinSpeed = 1.6f;
        public float crashDamagePerSpeed = 6f;

        public float Speed { get; private set; }
        public int CrashCount { get; private set; }
        public bool IsSkiing => Speed >= walkModeSpeed;

        CharacterController m_Cc;
        Transform m_Head;
        Terrain m_Terrain;
        float m_VerticalSpeed;
        float m_CrashCooldown;
        bool m_Moving;
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

            var heading = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
            var normal = GroundNormal(m_Head.position);

            if (playing)
            {
                // ── 경사에 따른 가속 ──
                var downhill = Vector3.ProjectOnPlane(Vector3.down, normal); // 길이 = sin(경사)
                float along = Vector3.Dot(downhill, heading) * 9.81f;
                float cos = Mathf.Max(0.2f, normal.y);

                // ── 폴 ──
                bool lPush = false, rPush = false, lDrag = false, rDrag = false;
                float push = 0f;
                ReadPole(leftPole, ref push, ref lPush, ref lDrag);
                ReadPole(rightPole, ref push, ref rPush, ref rDrag);

                if (Speed < startSpeed && push <= 0f)
                {
                    Speed = 0f; // 정지 마찰: 밀지 않으면 출발하지 않는다
                }
                else
                {
                    Speed += along * dt;
                    Speed -= friction * 9.81f * cos * dt;
                    Speed -= airDrag * Speed * Speed * dt;
                    if (Speed < pushMaxSpeed) Speed += push * pushGain * dt * (1f - Speed / pushMaxSpeed * 0.6f);

                    if (lDrag && rDrag) Speed -= brakeDecel * dt;
                    else if (lDrag || rDrag)
                    {
                        Speed -= dragDecel * dt;
                        float speedFactor = Mathf.Clamp(Speed / 3f, 0.35f, 1f);
                        float yaw = (lDrag ? -1f : 1f) * turnRate * speedFactor * dt;
                        rig.transform.RotateAround(m_Head.position, Vector3.up, yaw);
                        heading = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
                    }
                    Speed = Mathf.Clamp(Speed, 0f, maxSpeed);
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
            if (windAudio) windAudio.volume = Mathf.Lerp(0f, 0.7f, Speed / maxSpeed);
        }

        void ReadPole(SkiPole pole, ref float push, ref bool pushing, ref bool dragging)
        {
            if (pole == null || !pole.Equipped || !pole.InContact || !pole.isActiveAndEnabled) return;
            var v = pole.LocalHandVelocity;
            float back = -v.z;
            if (back > pushThreshold)
            {
                push += back;
                pushing = true;
            }
            else if (v.magnitude < dragHandSpeed && Speed > 0.2f)
            {
                dragging = true;
            }
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
            GameManager.Instance?.Say(impact > 5f ? "쾅! 크게 부딪혔어요. 폴로 밀어 다시 출발하세요." : "쿵! 부딪혔어요.");
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
