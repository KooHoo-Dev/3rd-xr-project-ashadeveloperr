using Untrodden.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Untrodden.Player
{
    /// <summary>
    /// With an empty hand: dip it in a river to scoop water, or press it into the snow to grab a handful.
    /// Bring it to your mouth to drink / eat it. Snow helps thirst a little but chills you.
    /// </summary>
    public class HandScoop : MonoBehaviour
    {
        enum Held { None, Water, Snow }

        const float k_HoldSeconds = 8f;
        const float k_MouthDistance = 0.2f;

        XRBaseInteractor[] m_Interactors;
        Held m_Held;
        float m_HeldUntil;
        Renderer m_Visual;
        MaterialPropertyBlock m_Block;

        public static HandScoop Attach(Transform hand, GameAssets assets)
        {
            var scoop = hand.gameObject.AddComponent<HandScoop>();
            scoop.m_Interactors = hand.GetComponentsInChildren<XRBaseInteractor>(true);
            var visual = Meshes.Part(hand, PrimitiveType.Sphere, assets.handful, new Vector3(0f, -0.02f, 0.04f), Vector3.one * 0.06f);
            visual.name = "Handful";
            scoop.m_Visual = visual.GetComponent<Renderer>();
            scoop.m_Visual.enabled = false;
            scoop.m_Block = new MaterialPropertyBlock();
            return scoop;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsPlaying)
            {
                SetHeld(Held.None);
                return;
            }

            foreach (var interactor in m_Interactors)
            {
                if (interactor != null && interactor.hasSelection)
                {
                    SetHeld(Held.None);
                    return;
                }
            }

            var p = transform.position;
            var world = gm.World;
            if (world.TryGetWater(p.x, p.z, out var surface) && p.y < surface + 0.02f)
            {
                if (m_Held != Held.Water)
                    Sounds.Play(SoundId.Splash, p, 0.6f);
                SetHeld(Held.Water);
            }
            else if (p.y < world.Height(p.x, p.z) + 0.08f)
            {
                SetHeld(Held.Snow);
            }

            if (m_Held != Held.None && Time.time > m_HeldUntil)
                SetHeld(Held.None);

            if (m_Held != Held.None && Vector3.Distance(p, gm.Mouth) < k_MouthDistance)
            {
                if (m_Held == Held.Water)
                    gm.Survival.Drink();
                else
                    gm.Survival.EatSnow();
                Sounds.Play(m_Held == Held.Water ? SoundId.Drink : SoundId.Eat, p, 0.7f);
                SetHeld(Held.None);
            }
        }

        void SetHeld(Held held)
        {
            if (held != Held.None)
                m_HeldUntil = Time.time + k_HoldSeconds;
            if (held == m_Held)
                return;
            m_Held = held;
            m_Visual.enabled = held != Held.None;
            if (held != Held.None)
            {
                m_Block.SetColor("_BaseColor", held == Held.Water ? new Color(0.45f, 0.65f, 0.85f, 0.8f) : new Color(0.95f, 0.97f, 1f, 1f));
                m_Visual.SetPropertyBlock(m_Block);
            }
        }
    }
}
