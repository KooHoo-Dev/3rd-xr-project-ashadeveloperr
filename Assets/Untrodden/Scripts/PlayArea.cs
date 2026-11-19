using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 플레이어가 다닐 수 있는 영역(XZ 사각형)을 정의하고,
    /// 영역 밖으로 나가거나 지형 아래로 떨어지면 마지막 모닥불로 돌려보낸다.
    /// 순간이동 허용 여부(<see cref="PlayAreaTeleportationArea"/>)도 이 영역을 기준으로 판단한다.
    /// </summary>
    public class PlayArea : MonoBehaviour
    {
        public static PlayArea Instance { get; private set; }

        [Tooltip("다닐 수 있는 영역 (x, z 최소/최대)")]
        public Vector2 min = new(30f, 16f);
        public Vector2 max = new(210f, 406f);
        [Tooltip("영역을 이만큼 벗어나면 복귀")]
        public float outsideMargin = 2f;
        [Tooltip("머리가 지형보다 이만큼 아래로 내려가면 복귀")]
        public float belowGroundMargin = 1.5f;
        [Tooltip("이 높이보다 아래로 떨어지면 무조건 복귀")]
        public float killY = -20f;

        Terrain m_Terrain;

        void Awake()
        {
            Instance = this;
            m_Terrain = Terrain.activeTerrain;
        }

        public bool Contains(Vector3 p, float margin = 0f) =>
            p.x >= min.x - margin && p.x <= max.x + margin &&
            p.z >= min.y - margin && p.z <= max.y + margin;

        public float GroundY(Vector3 p) =>
            m_Terrain != null ? m_Terrain.SampleHeight(p) + m_Terrain.transform.position.y : float.NegativeInfinity;

        void LateUpdate()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Head == null || gm.State != GameState.Playing) return;

            var head = gm.Head.position;
            bool lost = head.y < killY
                        || !Contains(head, outsideMargin)
                        || head.y < GroundY(head) - belowGroundMargin;
            if (lost)
                gm.ReturnToCheckpoint("길을 벗어났어요. 마지막 모닥불로 돌아갑니다.");
        }
    }
}
