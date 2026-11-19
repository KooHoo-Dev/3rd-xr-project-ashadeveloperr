using UnityEngine;
using UnityEngine.UI;

namespace Untrodden
{
    /// <summary>
    /// 화면 흔들림 없이 붉은 테두리로 피해를 표현한다(멀미 방지).
    /// 카메라 앞 월드 캔버스의 이미지 알파만 바꾼다.
    /// </summary>
    public class DamageVignette : MonoBehaviour
    {
        public Image image;
        public float maxAlpha = 0.75f;

        void Update()
        {
            var v = PlayerVitals.Instance;
            if (v == null || image == null) return;
            float lowHealth = 1f - Mathf.Clamp01(v.Health / (v.maxHealth * 0.5f));
            float a = Mathf.Max(v.DamagePulse * 0.6f, lowHealth * 0.8f) * maxAlpha;
            a *= 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);
            var c = image.color;
            c.a = a;
            image.color = c;
            image.enabled = a > 0.01f;
        }
    }
}
