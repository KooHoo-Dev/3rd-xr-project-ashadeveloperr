using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 체력 하나만 관리한다. 충돌과 눈보라가 체력을 깎고, 모닥불을 지나면 가득 찬다.
    /// 0이 되면 마지막 모닥불에서 다시 출발한다.
    /// </summary>
    public class PlayerVitals : MonoBehaviour
    {
        public static PlayerVitals Instance { get; private set; }

        public float maxHealth = 100f;
        [Tooltip("눈보라가 최고조일 때 초당 체력 감소")]
        public float stormDamagePerSecond = 1.6f;

        public float Health { get; private set; }
        /// <summary>최근 피해 정도(0~1). 붉은 테두리 연출에 사용.</summary>
        public float DamagePulse { get; private set; }

        void Awake()
        {
            Instance = this;
            Health = maxHealth;
        }

        public void HealFull() => Health = maxHealth;

        public void TakeDamage(float amount)
        {
            if (amount <= 0f) return;
            Health -= amount;
            DamagePulse = Mathf.Min(1f, DamagePulse + Mathf.Clamp01(amount / 25f) + 0.2f);
        }

        void Update()
        {
            var gm = GameManager.Instance;
            DamagePulse = Mathf.MoveTowards(DamagePulse, 0f, Time.deltaTime * 0.8f);
            if (gm == null || gm.State != GameState.Playing) return;

            var b = Blizzard.Instance;
            if (b != null && b.StormStrength > 0f)
            {
                Health -= stormDamagePerSecond * b.StormStrength * Time.deltaTime;
                DamagePulse = Mathf.Max(DamagePulse, 0.25f * b.StormStrength);
            }

            if (Health <= 0f)
            {
                Health = maxHealth;
                gm.Respawn();
            }
        }
    }
}
