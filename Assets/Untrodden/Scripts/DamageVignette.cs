using UnityEngine;
using UnityEngine.UI;

namespace Untrodden
{
    /// <summary>
    /// 화면 흔들림 없이 붉은 테두리로 충돌을 표현한다(멀미 방지).
    /// 카메라 앞 월드 캔버스의 이미지 알파만 바꾼다.
    /// </summary>
    public class DamageVignette : MonoBehaviour
    {
        public Image image;
        public float maxAlpha = 0.75f;

        void Update()
        {
            var ski = SkiController.Instance;
            if (ski == null || image == null) return;
            float a = ski.CrashPulse * 0.7f * maxAlpha;
            var c = image.color;
            c.a = a;
            image.color = c;
            image.enabled = a > 0.01f;
        }
    }
}
