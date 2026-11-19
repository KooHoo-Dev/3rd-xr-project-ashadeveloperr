using System.Collections.Generic;
using UnityEngine;

namespace Untrodden
{
    /// <summary>바위 그늘 등 눈보라를 피할 수 있는 원형 영역.</summary>
    public class ShelterZone : MonoBehaviour
    {
        static readonly List<ShelterZone> s_All = new();
        public float radius = 4f;

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        public static bool Contains(Vector3 position)
        {
            foreach (var z in s_All)
            {
                var d = z.transform.position - position;
                d.y = 0f;
                if (d.sqrMagnitude < z.radius * z.radius) return true;
            }
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
