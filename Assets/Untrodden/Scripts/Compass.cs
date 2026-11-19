using UnityEngine;

namespace Untrodden
{
    /// <summary>바늘이 매 프레임 월드 북쪽(+Z, 바다 방향)을 가리킨다.</summary>
    public class Compass : MonoBehaviour
    {
        public Transform needle;
        public float damping = 10f;

        void LateUpdate()
        {
            if (needle == null) return;
            var up = transform.up;
            var north = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (north.sqrMagnitude < 1e-4f) return;
            var target = Quaternion.LookRotation(north.normalized, up);
            needle.rotation = Quaternion.Slerp(needle.rotation, target, 1f - Mathf.Exp(-damping * Time.deltaTime));
        }
    }
}
