using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Untrodden
{
    /// <summary>
    /// 양손에 든 파이어스틱 두 개를 빠르게 부딪치면 불꽃이 튄다.
    /// 짧은 시간 안에 여러 번 부딪치면 근처 모닥불에 점화를 시도한다.
    /// 잡은 물체는 kinematic이라 충돌 대신 트리거와 직접 계산한 속도로 판정한다.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class FireStick : MonoBehaviour
    {
        public float minStrikeSpeed = 0.9f;
        public int strikesToIgnite = 4;
        public float strikeWindow = 3f;
        public float igniteDistance = 2.2f;
        public ParticleSystem sparks;
        public AudioSource strikeAudio;

        XRGrabInteractable m_Grab;
        Vector3 m_LastPos;
        float m_LastStrikeTime = -10f;
        int m_StrikeCount;

        public Vector3 Velocity { get; private set; }
        public bool IsHeld => m_Grab != null && m_Grab.isSelected;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_LastPos = transform.position;
        }

        void FixedUpdate()
        {
            var pos = transform.position;
            Velocity = (pos - m_LastPos) / Time.fixedDeltaTime;
            m_LastPos = pos;
        }

        void OnTriggerEnter(Collider other)
        {
            var otherStick = other.GetComponentInParent<FireStick>();
            if (otherStick == null || otherStick == this) return;
            // 두 막대 중 한쪽에서만 처리한다.
            if (GetInstanceID() > otherStick.GetInstanceID()) return;
            if (!IsHeld || !otherStick.IsHeld) return;

            float speed = (Velocity - otherStick.Velocity).magnitude;
            if (speed < minStrikeSpeed) return;

            var contact = other.ClosestPoint(transform.position);
            Strike(contact);
            otherStick.Haptic(0.6f);
            Haptic(0.6f);
        }

        void Strike(Vector3 point)
        {
            if (Time.time - m_LastStrikeTime > strikeWindow) m_StrikeCount = 0;
            m_LastStrikeTime = Time.time;
            m_StrikeCount++;

            if (sparks != null)
            {
                sparks.transform.position = point;
                sparks.Emit(6 + m_StrikeCount * 4);
            }
            if (strikeAudio != null) strikeAudio.PlayOneShot(strikeAudio.clip, 0.6f);

            if (m_StrikeCount < strikesToIgnite) return;
            m_StrikeCount = 0;

            var fire = Campfire.Nearest(point, igniteDistance);
            if (fire == null)
                GameManager.Instance?.Say("불꽃이 튀었지만 모닥불이 너무 멀어요.");
            else
                fire.TryIgnite();
        }

        void Haptic(float amplitude)
        {
            if (m_Grab.firstInteractorSelecting is XRBaseInputInteractor input)
                input.SendHapticImpulse(amplitude, 0.08f);
        }
    }
}
