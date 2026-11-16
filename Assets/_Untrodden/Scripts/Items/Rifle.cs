using System.Collections;
using Untrodden.Audio;
using Untrodden.Bears;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Untrodden.Items
{
    /// <summary>
    /// Grab and pull the trigger to fire. Rounds are loaded one by one by pushing a bullet
    /// into the chamber socket on the right side of the action.
    /// </summary>
    public class Rifle : WorldItem
    {
        public const int Capacity = 5;

        public Transform muzzle;
        public TypedSocket chamber;
        public Light flash;

        [SerializeField] int m_Loaded = 2;
        float m_NextShot;

        public int Loaded => m_Loaded;

        protected override void Awake()
        {
            base.Awake();
            Grab.activated.AddListener(_ => Fire());
        }

        void Start()
        {
            if (chamber != null)
            {
                chamber.Accept = i => m_Loaded < Capacity && i.transform.GetComponent<Bullet>() != null;
                chamber.selectEntered.AddListener(OnChambered);
            }
            if (flash != null)
                flash.enabled = false;
        }

        void OnChambered(SelectEnterEventArgs args)
        {
            if (args.interactableObject.transform.GetComponent<Bullet>() is Bullet bullet)
                StartCoroutine(Load(bullet));
        }

        IEnumerator Load(Bullet bullet)
        {
            yield return null;
            if (bullet == null)
                yield break;
            m_Loaded = Mathf.Min(m_Loaded + 1, Capacity);
            Sounds.Play(SoundId.Load, chamber.transform.position, 0.8f);
            bullet.Consume();
        }

        public void Fire()
        {
            if (Time.time < m_NextShot || !IsHeldByHand(Grab))
                return;
            m_NextShot = Time.time + 0.6f;

            if (m_Loaded <= 0)
            {
                Sounds.Play(SoundId.DryFire, muzzle.position, 0.8f);
                return;
            }

            m_Loaded--;
            Sounds.Play(SoundId.Shot, muzzle.position, 1f);
            if (flash != null)
                StartCoroutine(Flash());

            var origin = muzzle.position;
            var dir = muzzle.forward;
            var end = origin + dir * 150f;
            if (Physics.Raycast(origin, dir, out var hit, 150f, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var bear = hit.collider.GetComponentInParent<Bear>();
                if (bear != null)
                {
                    bear.OnShot(hit.collider.name == Bear.HeadColliderName);
                    return;
                }
            }

            BearSpawner.NotifyMiss(origin, end);
        }

        IEnumerator Flash()
        {
            flash.enabled = true;
            yield return new WaitForSeconds(0.05f);
            flash.enabled = false;
        }
    }
}
