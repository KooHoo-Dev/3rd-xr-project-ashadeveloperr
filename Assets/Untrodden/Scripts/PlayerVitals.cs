using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 체온·허기를 내부 수치로 두고, 부족할 때 하나의 체력이 줄어든다.
    /// 모닥불 근처에서 체온 회복, 눈보라 중에는 체온 감소가 빨라지고 바위 그늘에서는 완화된다.
    /// </summary>
    public class PlayerVitals : MonoBehaviour
    {
        public static PlayerVitals Instance { get; private set; }

        [Header("Max")]
        public float maxHealth = 100f;
        public float maxWarmth = 100f;
        public float maxFood = 100f;

        [Header("Rates per second")]
        public float warmthDrain = 0.6f;
        public float foodDrain = 0.12f;
        public float fireWarmthGain = 8f;
        public float freezingDamage = 2.5f;
        public float starvingDamage = 1f;
        public float healthRegen = 1.5f;
        public float shelterMultiplier = 0.35f;

        public float Health { get; private set; }
        public float Warmth { get; private set; }
        public float Food { get; private set; }
        public bool NearFire { get; private set; }
        public bool InShelter { get; private set; }
        /// <summary>최근 피해 정도(0~1). 붉은 테두리 연출에 사용.</summary>
        public float DamagePulse { get; private set; }

        void Awake()
        {
            Instance = this;
            ResetVitals();
        }

        public void ResetVitals()
        {
            Health = maxHealth;
            Warmth = maxWarmth;
            Food = maxFood;
        }

        public void Eat(float amount)
        {
            Food = Mathf.Min(maxFood, Food + amount);
            Health = Mathf.Min(maxHealth, Health + amount * 0.25f);
        }

        void Update()
        {
            var gm = GameManager.Instance;
            DamagePulse = Mathf.MoveTowards(DamagePulse, 0f, Time.deltaTime * 0.8f);
            if (gm == null || gm.State != GameState.Playing) return;

            var head = gm.Head.position;
            NearFire = Campfire.AnyLitFireWarms(head);
            InShelter = ShelterZone.Contains(head);

            float dt = Time.deltaTime;
            float blizzard = Blizzard.Instance != null ? Blizzard.Instance.ColdMultiplier : 1f;

            if (NearFire)
            {
                Warmth = Mathf.Min(maxWarmth, Warmth + fireWarmthGain * dt);
            }
            else
            {
                float drain = warmthDrain * blizzard * (InShelter ? shelterMultiplier : 1f);
                Warmth = Mathf.Max(0f, Warmth - drain * dt);
            }

            Food = Mathf.Max(0f, Food - foodDrain * dt);

            float damage = 0f;
            if (Warmth <= 0f) damage += freezingDamage;
            if (Food <= 0f) damage += starvingDamage;

            if (damage > 0f)
            {
                Health -= damage * dt;
                DamagePulse = Mathf.Min(1f, DamagePulse + dt * 1.5f);
            }
            else if (Warmth > maxWarmth * 0.5f && Food > maxFood * 0.3f)
            {
                Health = Mathf.Min(maxHealth, Health + healthRegen * dt);
            }

            if (Health <= 0f)
            {
                ResetVitals();
                gm.Respawn();
            }
        }
    }
}
