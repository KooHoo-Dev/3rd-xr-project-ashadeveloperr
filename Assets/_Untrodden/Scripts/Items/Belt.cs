using System.Collections;
using Untrodden.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Untrodden.Items
{
    /// <summary>
    /// Holsters that follow the body (head position, yaw only): rifle on the right shoulder,
    /// fire steel on the left hip, compass on the chest, bullet pouch on the right hip.
    /// </summary>
    public class Belt : MonoBehaviour
    {
        Transform m_Head;

        public TypedSocket RifleSocket { get; private set; }
        public TypedSocket SteelSocket { get; private set; }
        public TypedSocket CompassSocket { get; private set; }
        public BulletPouch Pouch { get; private set; }

        public static Belt Create(Transform head, GameAssets assets)
        {
            var belt = new GameObject("Belt").AddComponent<Belt>();
            belt.m_Head = head;
            // Right hip, muzzle pointing down and back so it never crosses the view.
            belt.RifleSocket = belt.AddSocket("Rifle Holster", new Vector3(0.32f, 0f, -0.08f), new Vector3(115f, 0f, 0f), 0.25f,
                i => i.transform.GetComponent<Rifle>() != null);
            belt.SteelSocket = belt.AddSocket("Fire Steel Holster", new Vector3(-0.25f, 0f, 0.05f), new Vector3(0f, 0f, 0f), 0.12f,
                i => i.transform.GetComponent<FireSteel>() != null);
            belt.CompassSocket = belt.AddSocket("Compass Holster", new Vector3(-0.15f, 0.25f, 0.18f), new Vector3(-30f, 0f, 0f), 0.12f,
                i => i.transform.GetComponent<Compass>() != null);
            belt.Pouch = BulletPouch.Create(belt.transform, new Vector3(0.22f, 0.02f, 0.12f), assets);
            belt.LateUpdate();
            return belt;
        }

        TypedSocket AddSocket(string name, Vector3 localPos, Vector3 localEuler, float radius, System.Func<IXRInteractable, bool> accept)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = localEuler;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius;
            var socket = go.AddComponent<TypedSocket>();
            socket.showInteractableHoverMeshes = false;
            socket.Accept = accept;
            return socket;
        }

        void LateUpdate()
        {
            if (m_Head == null)
                return;
            var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);
            transform.SetPositionAndRotation(m_Head.position + Vector3.down * 0.6f, Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        /// <summary>Put a starting item straight into a holster.</summary>
        public IEnumerator Equip(TypedSocket socket, WorldItem item)
        {
            item.MarkOwned();
            item.transform.position = socket.transform.position;
            yield return null; // let both register with the interaction manager
            if (socket.interactionManager != null)
                socket.interactionManager.SelectEnter((IXRSelectInteractor)socket, (IXRSelectInteractable)item.Grab);
        }
    }

    /// <summary>Grab the pouch to pull a single bullet out of it.</summary>
    public class BulletPouch : MonoBehaviour
    {
        public int Count { get; private set; } = 3;
        XRSimpleInteractable m_Interactable;

        public static BulletPouch Create(Transform parent, Vector3 localPos, GameAssets assets)
        {
            var go = new GameObject("Bullet Pouch");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            Meshes.Part(go.transform, PrimitiveType.Cube, assets.ammoBox, Vector3.zero, new Vector3(0.1f, 0.12f, 0.06f));
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.12f, 0.14f, 0.08f);
            var pouch = go.AddComponent<BulletPouch>();
            pouch.m_Interactable = go.AddComponent<XRSimpleInteractable>();
            pouch.m_Interactable.selectEntered.AddListener(pouch.OnGrab);
            return pouch;
        }

        public void Add(int rounds) => Count += rounds;

        void OnGrab(SelectEnterEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor)
                return;
            StartCoroutine(HandOut(args.interactorObject));
        }

        IEnumerator HandOut(IXRSelectInteractor hand)
        {
            yield return null;
            var manager = m_Interactable.interactionManager;
            if (manager == null)
                yield break;
            manager.SelectExit(hand, (IXRSelectInteractable)m_Interactable);

            if (Count <= 0)
            {
                Sounds.Play(SoundId.DryFire, transform.position, 0.4f);
                yield break;
            }

            Count--;
            var attach = hand.GetAttachTransform(m_Interactable);
            var bullet = GameManager.Instance.Items.Create(ItemKind.Bullet, attach.position, attach.rotation);
            bullet.MarkOwned();
            yield return null;
            manager.SelectEnter(hand, (IXRSelectInteractable)bullet.Grab);
            Sounds.Play(SoundId.Pickup, transform.position, 0.4f);
        }
    }
}
