using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Untrodden
{
    /// <summary>장작(Firewood 컴포넌트가 있는 물체)만 받는 소켓.</summary>
    public class FirewoodSocket : XRSocketInteractor
    {
        public override bool CanHover(IXRHoverInteractable interactable)
            => base.CanHover(interactable) && IsFirewood(interactable);

        public override bool CanSelect(IXRSelectInteractable interactable)
            => base.CanSelect(interactable) && IsFirewood(interactable);

        static bool IsFirewood(IXRInteractable interactable)
            => interactable is UnityEngine.Component c && c.GetComponent<Firewood>() != null;
    }
}
