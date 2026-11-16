using System.Collections;
using Untrodden.Audio;
using Untrodden.World;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Untrodden.Items
{
    public enum ItemKind { Firewood, Fish, Can, AmmoBox, Bullet, Rifle, FireSteel, Compass }

    /// <summary>
    /// Base for everything the player can pick up. Items spawned by a chunk stay with that chunk
    /// until the player touches them; after that they are "taken" and never respawn.
    /// </summary>
    public class WorldItem : MonoBehaviour
    {
        public ItemKind kind;

        long m_Id = -1;
        ChunkStreamer m_Streamer;

        public XRGrabInteractable Grab { get; private set; }
        public bool Taken { get; private set; }

        protected virtual void Awake()
        {
            Grab = GetComponent<XRGrabInteractable>();
            if (Grab != null)
            {
                Grab.selectEntered.AddListener(OnSelectEntered);
                Grab.selectExited.AddListener(OnSelectExited);
            }
        }

        public void Bind(long id, ChunkStreamer streamer)
        {
            m_Id = id;
            m_Streamer = streamer;
        }

        /// <summary>Starting equipment: owned from the start, never cleaned up.</summary>
        public void MarkOwned() => Taken = true;

        protected virtual void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (Taken)
                return;
            Taken = true;
            if (m_Id >= 0 && m_Streamer != null)
                m_Streamer.MarkTaken(m_Id);
        }

        protected virtual void OnSelectExited(SelectExitEventArgs args) { }

        protected virtual void Update()
        {
            // Loose items left far behind are gone for good (the ground under them unloads).
            if (!Taken || Grab == null || Grab.isSelected || GameManager.Instance == null)
                return;
            if (transform.position.y < -60f ||
                (transform.position - GameManager.Instance.Head.position).sqrMagnitude > 110f * 110f)
                Destroy(gameObject);
        }

        public void Consume()
        {
            if (Grab != null && Grab.isSelected && Grab.interactionManager != null)
                Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)Grab);
            Destroy(gameObject);
        }

        public static bool IsHeldByHand(XRGrabInteractable grab)
        {
            if (grab == null || !grab.isSelected)
                return false;
            return !(grab.firstInteractorSelecting is XRSocketInteractor);
        }
    }

    public class Bullet : WorldItem { }

    /// <summary>Picking it up tips the rounds into the belt pouch.</summary>
    public class AmmoBox : WorldItem
    {
        public int rounds = 3;

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            StartCoroutine(Empty());
        }

        IEnumerator Empty()
        {
            yield return null;
            GameManager.Instance.Pouch.Add(rounds);
            Sounds.Play(SoundId.Pickup, transform.position, 0.7f);
            Consume();
        }
    }

    /// <summary>Bring it to your mouth while holding it.</summary>
    public class Edible : WorldItem
    {
        const float k_MouthDistance = 0.17f;

        protected override void Update()
        {
            base.Update();
            if (!IsHeldByHand(Grab) || GameManager.Instance == null || !GameManager.Instance.IsPlaying)
                return;
            if (Vector3.Distance(transform.position, GameManager.Instance.Mouth) < k_MouthDistance)
            {
                GameManager.Instance.Survival.Eat(kind);
                Sounds.Play(SoundId.Eat, transform.position, 0.8f);
                Consume();
            }
        }
    }

    /// <summary>Drop logs on the ground: the first one starts a fire pit, the next ones join it.</summary>
    public class Firewood : WorldItem
    {
        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (args.interactorObject is XRSocketInteractor || !isActiveAndEnabled)
                return;
            StartCoroutine(SettleIntoPit());
        }

        IEnumerator SettleIntoPit()
        {
            yield return new WaitForSeconds(0.35f);
            if (Grab == null || Grab.isSelected || GameManager.Instance == null)
                yield break;

            var world = GameManager.Instance.World;
            var p = transform.position;
            var ground = world.Height(p.x, p.z);
            if (p.y - ground > 0.8f)
                yield break; // dropped from up high or still flying: wait for the next drop
            if (world.TryGetWater(p.x, p.z, out _))
                yield break;

            var pit = FirePit.FindNearest(p, 1.8f);
            if (pit == null)
                pit = FirePit.Create(new Vector3(p.x, ground, p.z), GameManager.Instance.Assets);
            pit.TryAdd(this);
        }
    }

    /// <summary>The needle always points to world +Z, which is north (towards the sea).</summary>
    public class Compass : WorldItem
    {
        public Transform needle;

        protected override void Update()
        {
            base.Update();
            if (needle == null)
                return;
            var north = Vector3.ProjectOnPlane(Vector3.forward, transform.up);
            if (north.sqrMagnitude > 0.0001f)
                needle.rotation = Quaternion.LookRotation(north, transform.up);
        }
    }
}
