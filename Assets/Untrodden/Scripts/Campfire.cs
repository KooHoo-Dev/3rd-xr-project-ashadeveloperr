using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Untrodden
{
    /// <summary>
    /// 장작 소켓에 장작이 충분히 끼워진 상태에서 파이어스틱 불꽃이 닿으면 점화된다.
    /// 점화되면 체크포인트로 저장되고 주변을 따뜻하게 한다.
    /// </summary>
    public class Campfire : MonoBehaviour
    {
        static readonly List<Campfire> s_All = new();

        public FirewoodSocket[] sockets;
        public int logsRequired = 2;
        public float warmRadius = 3.5f;
        public Transform respawnPoint;
        public GameObject fireEffect;
        public Light fireLight;
        public AudioSource crackle;
        public bool startLit;

        public bool IsLit { get; private set; }

        public int LogCount
        {
            get
            {
                int n = 0;
                foreach (var s in sockets)
                    if (s != null && s.hasSelection) n++;
                return n;
            }
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        void Start()
        {
            SetVisual(false);
            if (startLit) Ignite(false);
        }

        void Update()
        {
            if (IsLit && fireLight != null)
                fireLight.intensity = 2.2f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 0.8f;
        }

        /// <summary>파이어스틱 불꽃이 이 위치 근처에서 튀었을 때 호출된다.</summary>
        public bool TryIgnite()
        {
            if (IsLit) return true;
            if (LogCount < logsRequired)
            {
                GameManager.Instance?.Say($"장작이 부족해요 ({LogCount}/{logsRequired})");
                return false;
            }
            Ignite(true);
            return true;
        }

        void Ignite(bool announce)
        {
            IsLit = true;
            SetVisual(true);
            foreach (var s in sockets)
            {
                if (s == null || !s.hasSelection) continue;
                // 불에 들어간 장작은 다시 뺄 수 없게 한다.
                if (s.firstInteractableSelected is Component c &&
                    c.TryGetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(out var grab))
                    grab.enabled = false;
            }
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetCheckpoint(respawnPoint != null ? respawnPoint : transform);
                if (announce) GameManager.Instance.Say("불이 붙었어요! 몸을 녹이세요.");
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

        public static bool AnyLitFireWarms(Vector3 position)
        {
            foreach (var f in s_All)
            {
                if (!f.IsLit) continue;
                var d = f.transform.position - position;
                d.y = 0f;
                if (d.sqrMagnitude < f.warmRadius * f.warmRadius) return true;
            }
            return false;
        }

        public static Campfire Nearest(Vector3 position, float maxDistance)
        {
            Campfire best = null;
            float bestSq = maxDistance * maxDistance;
            foreach (var f in s_All)
            {
                float sq = (f.transform.position - position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = f;
                }
            }
            return best;
        }
    }
}
