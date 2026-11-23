using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 머리 아래(허리 높이)를 따라다니며 머리의 좌우 회전(yaw)만 따른다.
    /// 나침반 등을 꽂아 두는 허리 소켓의 부모로 쓴다.
    /// </summary>
    public class BodyFollower : MonoBehaviour
    {
        public Transform head;
        public float heightBelowHead = 0.55f;
        public float yawFollowSpeed = 4f;

        void LateUpdate()
        {
            if (head == null) return;
            transform.position = head.position + Vector3.down * heightBelowHead;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) return;
            var target = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime));
        }
    }
}
