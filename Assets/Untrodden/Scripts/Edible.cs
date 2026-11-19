using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Untrodden
{
    /// <summary>잡은 상태로 입(머리 아래쪽) 가까이 가져가면 먹는다.</summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Edible : MonoBehaviour
    {
        public float nutrition = 35f;
        public float mouthDistance = 0.18f;
        public string displayName = "버섯";

        XRGrabInteractable m_Grab;

        void Awake() => m_Grab = GetComponent<XRGrabInteractable>();

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !m_Grab.isSelected) return;

            var mouth = gm.Head.position - gm.Head.up * 0.1f + gm.Head.forward * 0.05f;
            if ((transform.position - mouth).sqrMagnitude > mouthDistance * mouthDistance) return;

            PlayerVitals.Instance?.Eat(nutrition);
            gm.Say($"{displayName}을(를) 먹었어요. 허기 +{nutrition:0}");
            if (m_Grab.firstInteractorSelecting is UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor input)
                input.SendHapticImpulse(0.3f, 0.15f);
            Destroy(gameObject);
        }
    }
}
