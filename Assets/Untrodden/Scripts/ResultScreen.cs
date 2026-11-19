using TMPro;
using UnityEngine;

namespace Untrodden
{
    /// <summary>결승선 통과 시 플레이어 앞에 나타나는 결과 화면.</summary>
    public class ResultScreen : MonoBehaviour
    {
        public TMP_Text statsText;
        public float distanceFromHead = 2f;

        public void Show(float seconds, float penalty, int passed, int total, int crashes, int respawns)
        {
            var head = GameManager.Instance.Head;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            transform.position = head.position + fwd * distanceFromHead;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            if (statsText)
                statsText.text =
                    $"최종 기록   <b><size=120%>{Format(seconds + penalty)}</size></b>\n" +
                    $"<size=80%>주행 {Format(seconds)}  +  벌점 {penalty:0}초</size>\n" +
                    $"통과한 관문   <b>{passed} / {total}</b>\n" +
                    $"충돌 <b>{crashes}회</b>   ·   다시 출발 <b>{respawns}회</b>";
            gameObject.SetActive(true);
        }

        static string Format(float s)
        {
            int m = Mathf.FloorToInt(s / 60f);
            return $"{m}:{s - m * 60f:00.0}";
        }

        // 버튼 OnClick에 연결
        public void OnRestart() => GameManager.Instance.Restart();
    }
}
