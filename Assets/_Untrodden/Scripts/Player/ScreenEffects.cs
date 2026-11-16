using UnityEngine;
using UnityEngine.Rendering;

namespace Untrodden.Player
{
    /// <summary>
    /// Two quads just in front of the eyes: an edge vignette tinted by what is hurting you
    /// (frost for cold, dark for hunger, pale haze for thirst), and a full-screen layer for
    /// damage flashes and fades. Nothing moves the camera itself (comfort).
    /// </summary>
    public class ScreenEffects : MonoBehaviour
    {
        SurvivalState m_Survival;
        Material m_EdgeMat;
        Material m_FullMat;
        float m_Flash;
        float m_Fade;
        float m_FadeTarget;
        float m_FadeSpeed = 1f;

        public static ScreenEffects Create(Camera cam, SurvivalState survival, GameAssets assets)
        {
            var fx = cam.gameObject.AddComponent<ScreenEffects>();
            fx.m_Survival = survival;
            // Drawn just before the transparent queue (3000) so menus and world-space UI stay on top and readable.
            fx.m_EdgeMat = fx.CreateQuad(cam.transform, "Edge Overlay", assets.overlay, EdgeTexture(), 2990);
            fx.m_FullMat = fx.CreateQuad(cam.transform, "Full Overlay", assets.overlay, Texture2D.whiteTexture, 2991);
            survival.Hurt += amount => fx.m_Flash = Mathf.Clamp01(fx.m_Flash + 0.35f + amount * 0.01f);
            return fx;
        }

        Material CreateQuad(Transform cam, string name, Material template, Texture tex, int queue)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(cam, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            quad.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var mat = new Material(template) { renderQueue = queue };
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.clear);
            r.sharedMaterial = mat;
            return mat;
        }

        /// <summary>Fade the whole view to black (1) or back (0).</summary>
        public void FadeTo(float alpha, float seconds)
        {
            m_FadeTarget = alpha;
            m_FadeSpeed = seconds > 0f ? 1f / seconds : 1000f;
        }

        void Update()
        {
            // edge vignette
            var cold = Mathf.Clamp01(m_Survival.ColdRate / 0.3f);
            var hunger = Mathf.Clamp01(m_Survival.HungerRate / 0.18f);
            var thirst = Mathf.Clamp01(m_Survival.ThirstRate / 0.23f);
            var low = Mathf.Clamp01((40f - m_Survival.Health) / 40f);

            var color = Color.clear;
            var weight = 0f;
            void Mix(Color c, float w)
            {
                if (w <= 0f) return;
                color += c * w;
                weight += w;
            }
            Mix(new Color(0.85f, 0.93f, 1f), cold);
            Mix(new Color(0.05f, 0.04f, 0.04f), hunger);
            Mix(new Color(1f, 0.96f, 0.88f), thirst);
            Mix(new Color(0.35f, 0f, 0f), low);
            if (weight > 0f)
                color /= weight;
            color.a = Mathf.Clamp01(Mathf.Max(Mathf.Max(cold, hunger), Mathf.Max(thirst, low)) * 0.75f);
            m_EdgeMat.SetColor("_BaseColor", color);

            // damage flash + fade
            m_Flash = Mathf.Max(0f, m_Flash - Time.deltaTime * 1.5f);
            m_Fade = Mathf.MoveTowards(m_Fade, m_FadeTarget, Time.deltaTime * m_FadeSpeed);
            var full = Color.Lerp(new Color(0.6f, 0f, 0f, m_Flash * 0.6f), new Color(0f, 0f, 0f, 1f), m_Fade);
            full.a = Mathf.Max(m_Flash * 0.6f, m_Fade);
            m_FullMat.SetColor("_BaseColor", full);
        }

        static Texture2D EdgeTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var c = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                var a = Mathf.Clamp01((d - 0.45f) / 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            tex.Apply();
            return tex;
        }
    }
}
