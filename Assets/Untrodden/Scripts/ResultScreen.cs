using TMPro;
using UnityEngine;

namespace Untrodden
{
    /// <summary>마을 도착 시 플레이어 앞에 나타나는 결과 화면.</summary>
    public class ResultScreen : MonoBehaviour
    {
        public TMP_Text statsText;
        public float distanceFromHead = 2f;

        public void Show(float seconds, int respawns, float distance)
        {
            var head = GameManager.Instance.Head;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            transform.position = head.position + fwd * distanceFromHead;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            int t = Mathf.FloorToInt(seconds);
            if (statsText)
                statsText.text =
                    $"걸린 시간   <b>{t / 60}분 {t % 60:00}초</b>\n" +
                    $"다시 시작   <b>{respawns}회</b>\n" +
                    $"이동 거리   <b>{distance:0}m</b>";
            gameObject.SetActive(true);
        }

        // 버튼 OnClick에 연결
        public void OnRestart() => GameManager.Instance.Restart();
    }
}
