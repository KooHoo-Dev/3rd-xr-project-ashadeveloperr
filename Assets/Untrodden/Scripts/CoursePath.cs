using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 코스 중심선(출발 → 결승, 일정 간격의 지면 위 점). 길을 잃었을 때 가장 가까운 코스 위로 돌려보내는 데 쓴다.
    /// 씬 빌더가 점을 채운다.
    /// </summary>
    public class CoursePath : MonoBehaviour
    {
        public static CoursePath Instance { get; private set; }

        public Vector3[] points;
        [Tooltip("복귀할 때 가장 가까운 점에서 이만큼 뒤(출발 쪽)로 물러난다(m)")]
        public float backOff = 6f;

        void Awake() => Instance = this;

        /// <summary>p에서 가장 가까운 코스 점을 찾아, 조금 뒤쪽 지점과 진행 방향을 돌려준다.</summary>
        public bool TryGetReturnPose(Vector3 p, out Vector3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (points == null || points.Length < 2) return false;

            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                var d = points[i] - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestD) { bestD = sq; best = i; }
            }

            // 뒤로 backOff만큼 물러난 점
            int idx = best;
            float back = 0f;
            while (idx > 0 && back < backOff)
            {
                back += Vector3.Distance(points[idx], points[idx - 1]);
                idx--;
            }
            position = points[idx];
            var fwd = points[Mathf.Min(idx + 1, points.Length - 1)] - points[Mathf.Max(idx - 1, 0)];
            fwd.y = 0f;
            yaw = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd).eulerAngles.y : 0f;
            return true;
        }
    }
}
