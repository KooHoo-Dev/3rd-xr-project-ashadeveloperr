using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 깃발 관문 하나. 두 깃대 사이(폭 2×halfWidth)를 진행 방향(transform.forward)으로 지나면 통과.
    /// 판정은 <see cref="CourseTracker"/>가 순서대로 처리한다.
    /// </summary>
    public class SlalomGate : MonoBehaviour
    {
        public enum Result { Pending, Passed, Missed }

        public int index;
        public float halfWidth = 2.6f;
        public Renderer[] flags;
        public Material passedMaterial;
        public Material missedMaterial;

        public Result State { get; private set; } = Result.Pending;

        /// <summary>관문 평면 기준 앞뒤 거리(+면 지나감)와 좌우 거리.</summary>
        public void Measure(Vector3 p, out float along, out float lateral)
        {
            var d = p - transform.position;
            d.y = 0f;
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            along = Vector3.Dot(d, fwd);
            lateral = Vector3.Dot(d, right);
        }

        public void SetResult(Result r)
        {
            State = r;
            var m = r == Result.Passed ? passedMaterial : r == Result.Missed ? missedMaterial : null;
            if (m == null) return;
            foreach (var f in flags)
                if (f) f.sharedMaterial = m;
        }
    }
}
