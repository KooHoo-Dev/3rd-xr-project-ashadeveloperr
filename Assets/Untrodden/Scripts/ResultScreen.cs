using TMPro;
using UnityEngine;

namespace Untrodden
{
    /// <summary>스테이지가 끝나면(클리어·시간 초과) 플레이어 앞에 나타나는 결과 화면.</summary>
    public class ResultScreen : MonoBehaviour
    {
        public TMP_Text titleText;
        public TMP_Text statsText;
        public GameObject nextButton;
        public float distanceFromHead = 2f;

        public void Show(bool cleared, int stageIndex, float seconds, float penalty, float limit,
                         int passed, int total, int crashes, bool hasNext)
        {
            var head = GameManager.Instance.Head;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            transform.position = head.position + fwd * distanceFromHead;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            if (titleText)
            {
                titleText.text = cleared ? "스테이지 클리어!" : "시간 초과";
                titleText.color = cleared ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 0.55f, 0.5f);
            }
            if (statsText)
            {
                float total2 = seconds + penalty;
                string record = cleared
                    ? $"기록   <b><size=120%>{Format(total2)}</size></b>   <size=80%>/ 제한 {Format(limit)}</size>"
                    : $"제한시간 {Format(limit)} 안에 결승선에 닿지 못했어요";
                statsText.text =
                    $"<size=80%>{Stages.Titles[stageIndex]}</size>\n" +
                    record + "\n" +
                    $"<size=80%>주행 {Format(seconds)}  +  관문 벌점 {penalty:0}초</size>\n" +
                    $"통과한 관문 <b>{passed} / {total}</b>   ·   충돌 <b>{crashes}회</b>";
            }
            if (nextButton) nextButton.SetActive(hasNext);
            gameObject.SetActive(true);
        }

        static string Format(float s)
        {
            s = Mathf.Max(0f, s);
            int m = Mathf.FloorToInt(s / 60f);
            return $"{m}:{s - m * 60f:00.0}";
        }

        // 버튼 OnClick에 연결
        public void OnRestart() => GameManager.Instance.Restart();
        public void OnNext() => GameManager.Instance.NextStage();
        public void OnStageSelect() => GameManager.Instance.BackToTitle();
    }
}
