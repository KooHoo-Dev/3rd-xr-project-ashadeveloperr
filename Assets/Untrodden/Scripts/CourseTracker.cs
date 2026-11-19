using System;
using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 관문 통과·누락을 판정하고 벌점 시간을 쌓는다.
    /// 다음 관문 이후의 어떤 관문 평면을 넘으면, 그 앞의 처리 안 된 관문은 모두 누락으로 본다.
    /// </summary>
    public class CourseTracker : MonoBehaviour
    {
        public static CourseTracker Instance { get; private set; }

        public SlalomGate[] gates;
        public float missPenalty = 5f;
        public AudioSource passAudio;

        public int NextIndex { get; private set; }
        public int Passed { get; private set; }
        public int Missed { get; private set; }
        public float Penalty => Missed * missPenalty;
        public int Total => gates != null ? gates.Length : 0;

        public event Action<SlalomGate> GateResolved;

        Vector3 m_Last;
        bool m_HasLast;

        void Awake()
        {
            Instance = this;
            Array.Sort(gates, (a, b) => a.index.CompareTo(b.index));
        }

        void OnEnable()
        {
            if (GameManager.Instance != null) GameManager.Instance.Teleported += ResetTracking;
        }

        void OnDisable()
        {
            if (GameManager.Instance != null) GameManager.Instance.Teleported -= ResetTracking;
        }

        void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Teleported -= ResetTracking;
                GameManager.Instance.Teleported += ResetTracking;
            }
        }

        void ResetTracking() => m_HasLast = false;

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing || gm.Head == null) { m_HasLast = false; return; }

            var p = gm.Head.position;
            if (!m_HasLast) { m_Last = p; m_HasLast = true; return; }

            for (int i = NextIndex; i < gates.Length; i++)
            {
                var g = gates[i];
                g.Measure(m_Last, out float before, out _);
                g.Measure(p, out float after, out float lateral);
                if (!(before < 0f && after >= 0f)) continue;
                // 너무 멀리 떨어진 평면 교차(다른 굽이)는 무시
                if (Mathf.Abs(lateral) > 25f) continue;

                for (int k = NextIndex; k < i; k++) Resolve(gates[k], false);
                Resolve(g, Mathf.Abs(lateral) <= g.halfWidth);
                NextIndex = i + 1;
                break;
            }
            m_Last = p;
        }

        void Resolve(SlalomGate g, bool passed)
        {
            if (g.State != SlalomGate.Result.Pending) return;
            g.SetResult(passed ? SlalomGate.Result.Passed : SlalomGate.Result.Missed);
            if (passed)
            {
                Passed++;
                if (passAudio) passAudio.Play();
            }
            else
            {
                Missed++;
                GameManager.Instance?.Say($"관문을 놓쳤어요! +{missPenalty:0}초");
            }
            GateResolved?.Invoke(g);
        }

        /// <summary>손목 나침반이 가리킬 다음 목표(다음 관문, 없으면 결승선).</summary>
        public Vector3? NextTarget()
        {
            if (gates != null && NextIndex < gates.Length) return gates[NextIndex].transform.position;
            var gm = GameManager.Instance;
            return gm != null && gm.goalPoint != null ? gm.goalPoint.position : null;
        }
    }
}
