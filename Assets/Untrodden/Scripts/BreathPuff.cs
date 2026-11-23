using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 추운 날씨의 입김. 몇 초마다 입 앞(시야 아래쪽)에 옅은 김을 내뿜는다.
    /// 시야를 가리지 않도록 아래쪽에서 짧게만 보이게 한다. 눈보라·속도에 따라 숨이 가빠진다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class BreathPuff : MonoBehaviour
    {
        public Transform head;
        public Vector2 interval = new(3.2f, 4.2f);
        public float coldInterval = 1.8f;
        public int particlesPerPuff = 5;

        ParticleSystem m_Ps;
        float m_Timer;

        void Awake()
        {
            m_Ps = GetComponent<ParticleSystem>();
            m_Ps.Play(); // 방출은 꺼져 있고 Emit으로만 내보낸다
            m_Timer = Random.Range(interval.x, interval.y);
        }

        void LateUpdate()
        {
            if (head == null) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing) return;

            m_Timer -= Time.deltaTime;
            if (m_Timer > 0f) return;

            // 눈보라가 거세거나 빨리 달릴수록 숨이 가빠진다
            float cold = Blizzard.Instance != null ? Blizzard.Instance.Intensity : 0f;
            if (SkiController.Instance != null)
                cold = Mathf.Max(cold, SkiController.Instance.Speed / SkiController.Instance.maxSpeed * 0.6f);
            m_Timer = Mathf.Lerp(Random.Range(interval.x, interval.y), coldInterval, cold);

            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var mouth = head.position - Vector3.up * 0.2f + fwd * 0.16f;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < particlesPerPuff; i++)
            {
                ep.position = mouth + Random.insideUnitSphere * 0.02f;
                ep.velocity = fwd * Random.Range(0.35f, 0.6f) + Random.insideUnitSphere * 0.06f + Vector3.down * 0.05f;
                m_Ps.Emit(ep, 1);
            }
        }
    }
}
