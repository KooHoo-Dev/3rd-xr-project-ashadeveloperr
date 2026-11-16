using Untrodden.Audio;
using UnityEngine;

namespace Untrodden.Items
{
    /// <summary>Scrape it quickly just above a fire pit to throw sparks. Enough sparks on three logs light it.</summary>
    public class FireSteel : WorldItem
    {
        public Transform tip;

        const float k_StrikeSpeed = 1.3f;   // m/s
        const float k_Cooldown = 0.35f;

        Vector3 m_LastTip;
        float m_NextStrike;

        protected override void Update()
        {
            base.Update();
            if (tip == null)
                return;

            var p = tip.position;
            var speed = (p - m_LastTip).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            m_LastTip = p;

            if (!IsHeldByHand(Grab) || speed < k_StrikeSpeed || Time.time < m_NextStrike)
                return;

            var pit = FirePit.FindNearest(p, 0.6f);
            if (pit == null || p.y > pit.transform.position.y + 0.9f)
                return;

            m_NextStrike = Time.time + k_Cooldown;
            pit.Strike(p);
        }
    }
}
