using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 컨트롤러에 붙은 스키 폴. 폴 끝이 눈에 닿았는지, 손이 몸 기준으로 어떻게 움직이는지 계산한다.
    /// 컨트롤러를 쥐면 폴이 새끼손가락 쪽(컨트롤러 -forward)으로 뻗어 나간다.
    /// 출발 지점 거치대에서 폴을 잡기 전에는 보이지 않고 동작하지 않는다(<see cref="PoleRackItem"/>).
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
        [Tooltip("처음부터 장착된 상태로 시작(테스트용)")]
        public bool startEquipped;

        static readonly System.Collections.Generic.List<SkiPole> s_All = new();
        public static bool BothEquipped
        {
            get
            {
                int n = 0;
                foreach (var p in s_All) if (p.Equipped) n++;
                return n >= 2;
            }
        }
        public bool Equipped { get; private set; }

        /// <summary>폴 끝이 눈에 닿아 있는지.</summary>
        public bool InContact { get; private set; }
        /// <summary>몸(XR Origin) 기준 손 속도. +z가 진행 방향.</summary>
        public Vector3 LocalHandVelocity { get; private set; }
        public Vector3 TipPosition { get; private set; }

        Transform m_Rig;
        Vector3 m_LastLocal;
        bool m_HasLast;

        public void Init(Transform rig) => m_Rig = rig;

        void Awake()
        {
            SetVisible(startEquipped);
            Equipped = startEquipped;
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        public void Equip()
        {
            if (Equipped) return;
            Equipped = true;
            SetVisible(true);
            m_HasLast = false;
        }

        void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        void LateUpdate()
        {
            if (!Equipped) { InContact = false; return; }
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
