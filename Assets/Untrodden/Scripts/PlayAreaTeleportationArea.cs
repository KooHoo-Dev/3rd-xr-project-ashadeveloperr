using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Untrodden
{
    /// <summary>
    /// 지형용 순간이동 영역. 플레이 영역 밖이나 가파른 비탈(절벽·벽)로는 순간이동할 수 없다.
    /// 경사 제한은 기본 기능(filterSelectionByHitNormal)을 쓰고, 영역 제한만 여기서 추가한다.
    /// </summary>
    public class PlayAreaTeleportationArea : TeleportationArea
    {
        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor)) return false;
            if (interactor is XRRayInteractor ray && ray.TryGetCurrent3DRaycastHit(out var hit))
                return Allowed(hit.point);
            return true;
        }

        protected override bool GenerateTeleportRequest(IXRInteractor interactor, RaycastHit raycastHit, ref TeleportRequest teleportRequest)
        {
            if (raycastHit.collider != null && !Allowed(raycastHit.point)) return false;
            return base.GenerateTeleportRequest(interactor, raycastHit, ref teleportRequest);
        }

        static bool Allowed(Vector3 point) => PlayArea.Instance == null || PlayArea.Instance.Contains(point);
    }
}
