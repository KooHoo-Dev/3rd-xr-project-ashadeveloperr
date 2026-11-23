using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 컨트롤러에 붙은 스키 폴. 폴이 눈을 짚고 있는지, 손이 몸 기준으로 어떻게 움직이는지 계산한다.
    /// 짚었는지는 손 높이가 아니라 폴 각도로 판정한다: 폴이 거의 세로(아래)를 향하면 짚은 것,
    /// 손목을 젖혀 폴 끝을 뒤로 들면 뗀 것. 그래서 앉아서 하든 키가 작든 손을 늘어뜨리지 않아도 된다.
    /// 폴 오브젝트의 +z가 폴 끝 방향이다. 컨트롤러를 쥐면 주먹 아래로 내려가도록 씬 빌더가 기울여 둔다.
    /// 시작부터 양손에 들고 있다.
    /// </summary>
    public class SkiPole : MonoBehaviour
    {
        public enum Side { Left, Right }
        public Side side;
        [Tooltip("손잡이에서 폴 끝까지 길이(m)")]
        public float length = 1.1f;
        [Tooltip("폴이 바로 아래 방향에서 이 각도 이내로 서 있으면 눈을 짚은 것으로 본다")]
        public float contactAngle = 40f;
        public Transform tipMarker;

        /// <summary>폴 끝이 눈에 닿아 있는지.</summary>
        public bool InContact { get; private set; }
        /// <summary>이번 프레임에 막 눈에 닿았는지(햅틱용).</summary>
        public bool JustTouched { get; private set; }
        /// <summary>몸(XR Origin) 기준 손 속도. +z가 진행 방향.</summary>
        public Vector3 LocalHandVelocity { get; private set; }
        public Vector3 TipPosition { get; private set; }

        Transform m_Rig;
        Vector3 m_LastLocal;
        bool m_HasLast;

        public void Init(Transform rig) => m_Rig = rig;

        void LateUpdate()
        {
            // 폴 방향: 손잡이(컨트롤러 위치)에서 컨트롤러 -forward 쪽으로 length만큼
            TipPosition = transform.position + transform.forward * length;

            bool was = InContact;
            InContact = Vector3.Angle(transform.forward, Vector3.down) <= contactAngle;
            JustTouched = InContact && !was;

            if (m_Rig != null && Time.deltaTime > 0f)
            {
                var local = m_Rig.InverseTransformPoint(transform.parent != null ? transform.parent.position : transform.position);
                if (m_HasLast) LocalHandVelocity = Vector3.Lerp(LocalHandVelocity, (local - m_LastLocal) / Time.deltaTime, 0.5f);
                m_LastLocal = local;
                m_HasLast = true;
            }
        }

        public void ResetTracking() => m_HasLast = false;
    }
}
