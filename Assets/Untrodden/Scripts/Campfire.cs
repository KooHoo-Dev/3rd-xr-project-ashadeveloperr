using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// 코스 옆 모닥불. 가까이 지나가면 불이 켜지며 체크포인트로 저장되고 체력이 가득 찬다.
    /// </summary>
    public class Campfire : MonoBehaviour
    {
        public float triggerRadius = 13f;
        public Transform respawnPoint;
        public GameObject fireEffect;
        public Light fireLight;
        public AudioSource crackle;
        public bool startLit;

        public bool IsLit { get; private set; }

        void Start()
        {
            SetVisual(startLit);
            IsLit = startLit;
        }

        void Update()
        {
            if (IsLit && fireLight != null)
                fireLight.intensity = 2.2f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 0.8f;

            var gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing || gm.Head == null) return;
            var d = gm.Head.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > triggerRadius * triggerRadius) return;

            bool first = !IsLit;
            if (first)
            {
                IsLit = true;
                SetVisual(true);
            }
            // 처음 지날 때만 체크포인트·완충 (다시 출발할 때 반복 회복되지 않도록)
            if (first || gm.Checkpoint != (respawnPoint != null ? respawnPoint : transform))
            {
                gm.SetCheckpoint(respawnPoint != null ? respawnPoint : transform, silent: true);
                PlayerVitals.Instance?.HealFull();
                gm.Say("모닥불 체크포인트! 체력이 가득 찼어요.");
            }
        }

        void SetVisual(bool lit)
        {
            if (fireEffect) fireEffect.SetActive(lit);
            if (fireLight) fireLight.enabled = lit;
            if (crackle)
            {
                if (lit) crackle.Play();
                else crackle.Stop();
            }
        }
    }
}
