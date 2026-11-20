using System.Collections.Generic;
using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 자갈밭(원형 영역). 스키로 지나가면 크게 덜컹이며(강한 진동) 속도가 느려진다.
    /// 판정은 SkiController가 매 프레임 위치로 묻는다(트리거보다 단순하고 가볍다).
    /// </summary>
    public class GravelPatch : MonoBehaviour
    {
        static readonly List<GravelPatch> s_All = new();

        [Tooltip("자갈밭 반지름(m)")]
        public float radius = 4f;

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        /// <summary>p가 어느 자갈밭 위에 있으면 true.</summary>
        public static bool Contains(Vector3 p)
        {
            foreach (var g in s_All)
            {
                var d = g.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude <= g.radius * g.radius) return true;
            }
            return false;
        }
    }
}
