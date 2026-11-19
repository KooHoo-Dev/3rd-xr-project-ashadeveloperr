using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 컨트롤러에 붙은 스키 폴. 폴 끝이 눈에 닿았는지, 손이 몸 기준으로 어떻게 움직이는지 계산한다.
    /// 컨트롤러를 쥐면 폴이 새끼손가락 쪽(컨트롤러 -forward)으로 뻗어 나간다.
    /// </summary>
    public class SkiPole : MonoBehaviour
    {
        public enum Side { Left, Right }
        public Side side;
        [Tooltip("손잡이에서 폴 끝까지 길이(m)")]
        public float length = 1.15f;
        [Tooltip("폴 끝이 지면에서 이 높이 이내면 닿은 것으로 본다")]
        public float contactTolerance = 0.12f;
        public Transform tipMarker;

        /// <summary>폴 끝이 눈에 닿아 있는지.</summary>
        public bool InContact { get; private set; }
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

            float ground = GroundHeight(TipPosition);
            InContact = TipPosition.y <= ground + contactTolerance;

            if (m_Rig != null && Time.deltaTime > 0f)
            {
                var local = m_Rig.InverseTransformPoint(transform.parent != null ? transform.parent.position : transform.position);
                if (m_HasLast) LocalHandVelocity = Vector3.Lerp(LocalHandVelocity, (local - m_LastLocal) / Time.deltaTime, 0.5f);
                m_LastLocal = local;
                m_HasLast = true;
            }
        }

        public void ResetTracking() => m_HasLast = false;

        static float GroundHeight(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }
    }
}
