using System;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Untrodden.Items
{
    /// <summary>A socket that only accepts interactables matching <see cref="Accept"/> (set from code).</summary>
    public class TypedSocket : XRSocketInteractor
    {
        public Func<IXRInteractable, bool> Accept;

        public override bool CanHover(IXRHoverInteractable interactable) =>
            base.CanHover(interactable) && (Accept == null || Accept(interactable));

        public override bool CanSelect(IXRSelectInteractable interactable) =>
            base.CanSelect(interactable) && (Accept == null || Accept(interactable));
    }
}
