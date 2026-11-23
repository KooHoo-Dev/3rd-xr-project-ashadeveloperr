using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 시작 화면을 항상 플레이어 앞에 둔다. 고개를 30° 넘게 돌리면 천천히 정면으로 따라온다.
    /// 시작 방향(트래킹 기준)이 어떻게 잡혔든 시작 화면은 반드시 보이고,
    /// 실제로 슬로프를 바라보게 하는 건 시작 버튼을 누르는 순간 <see cref="GameManager.StartGame"/>이 맡는다.
    /// </summary>
    public class TitleFollow : MonoBehaviour
    {
        public Transform head;
        [Tooltip("머리에서 화면까지 수평 거리(m)")]
        public float distance = 2.6f;
        [Tooltip("이 각도를 넘게 벗어나면 따라오기 시작")]
        public float startFollowAngle = 30f;
        [Tooltip("따라오다가 이 각도 안으로 들어오면 멈춤")]
        public float stopFollowAngle = 3f;
        public float followSpeed = 3f;

        bool m_Following;

        void OnEnable() => Snap();

        void Snap()
        {
            if (head == null || !TryHeadYaw(out var fwd)) return;
            transform.SetPositionAndRotation(Target(fwd), Quaternion.LookRotation(fwd, Vector3.up));
        }

        void LateUpdate()
        {
            if (head == null || !TryHeadYaw(out var fwd)) return;
            var toPanel = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
            float off = toPanel.sqrMagnitude > 1e-4f ? Vector3.Angle(toPanel, fwd) : 180f;
            if (off > startFollowAngle) m_Following = true;
            else if (off < stopFollowAngle) m_Following = false;

            // 따라오지 않을 때도 높이와 거리는 맞춰 둔다(눈높이가 다시 맞춰지는 경우)
            var keepDir = m_Following ? fwd : (toPanel.sqrMagnitude > 1e-4f ? toPanel.normalized : fwd);
            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            var dir = m_Following ? Vector3.Slerp(toPanel.normalized, fwd, k).normalized : keepDir;
            transform.SetPositionAndRotation(Target(dir), Quaternion.LookRotation(dir, Vector3.up));
        }

        Vector3 Target(Vector3 dir) => head.position + dir * distance;

        bool TryHeadYaw(out Vector3 fwd)
        {
            fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up); // 바로 아래를 볼 때
            if (fwd.sqrMagnitude < 1e-6f) return false;
            fwd.Normalize();
            return true;
        }
    }
}
