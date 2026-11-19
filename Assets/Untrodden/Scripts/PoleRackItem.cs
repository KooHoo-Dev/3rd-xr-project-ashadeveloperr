using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Untrodden
{
    /// <summary>
    /// 출발 지점 거치대에 세워 둔 스키 폴. 그립 버튼으로 잡으면(XRGrabInteractable)
    /// 잡은 손의 스키 폴이 장착되고 거치대의 폴은 사라진다.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PoleRackItem : MonoBehaviour
    {
        XRGrabInteractable m_Grab;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Grab.selectEntered.AddListener(OnGrabbed);
        }

        void OnDestroy()
        {
            if (m_Grab != null) m_Grab.selectEntered.RemoveListener(OnGrabbed);
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            var pole = FindHandPole(args.interactorObject.transform);
            if (pole == null) return;
            if (pole.Equipped)
            {
                // 이미 이 손에 폴이 있으면 다른 손 몫으로 남겨 둔다
                GameManager.Instance?.Say("이 손에는 이미 폴이 있어요. 다른 손으로 잡으세요.");
                return;
            }
            pole.Equip();
            gameObject.SetActive(false); // 선택 상태는 XRI가 비활성화 시 자동으로 해제한다

            if (SkiPole.BothEquipped)
                GameManager.Instance?.Say("준비 완료! 시작하기를 누르고 폴로 밀어 출발하세요.");
            else
                GameManager.Instance?.Say("다른 손으로 나머지 폴도 잡으세요.");
        }

        /// <summary>인터랙터에서 위로 올라가며 같은 컨트롤러 아래의 SkiPole을 찾는다.</summary>
        static SkiPole FindHandPole(Transform interactor)
        {
            for (var t = interactor; t != null; t = t.parent)
            {
                var p = t.GetComponentInChildren<SkiPole>(true);
                if (p != null) return p;
            }
            return null;
        }
    }
}
