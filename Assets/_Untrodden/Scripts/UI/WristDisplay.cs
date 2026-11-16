using TMPro;
using Untrodden.Items;
using Untrodden.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Untrodden.UI
{
    /// <summary>Small panel on the left wrist: health, what is draining it, ammo, storm warning, time.</summary>
    public class WristDisplay : MonoBehaviour
    {
        SurvivalState m_Survival;
        RectTransform m_Fill;
        Image m_FillImage;
        TextMeshProUGUI m_Text;
        float m_Timer;

        const float k_BarWidth = 180f;

        public static WristDisplay Create(Transform wrist)
        {
            var canvasGo = new GameObject("Wrist Display", typeof(RectTransform));
            canvasGo.transform.SetParent(wrist, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0.035f, -0.06f);
            canvasGo.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.0006f;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(200f, 130f);

            var bg = MenuPanel.CreateImage(rt, "Background", new Color(0.05f, 0.07f, 0.1f, 0.75f));
            MenuPanel.Stretch(bg.rectTransform);

            var barBg = MenuPanel.CreateImage(rt, "Bar", new Color(0.2f, 0.2f, 0.2f, 0.9f));
            var barRt = barBg.rectTransform;
            barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.anchoredPosition = new Vector2(0f, -10f);
            barRt.sizeDelta = new Vector2(k_BarWidth, 16f);

            var fill = MenuPanel.CreateImage(barRt, "Fill", new Color(0.85f, 0.3f, 0.25f));
            var fillRt = fill.rectTransform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
            fillRt.sizeDelta = new Vector2(k_BarWidth, 0f);

            var text = MenuPanel.CreateText(rt, "Status", "", 14f, TextAlignmentOptions.TopLeft);
            var textRt = text.rectTransform;
            textRt.anchorMin = new Vector2(0f, 0f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.offsetMin = new Vector2(10f, 6f);
            textRt.offsetMax = new Vector2(-10f, -32f);

            var display = canvasGo.AddComponent<WristDisplay>();
            display.m_Fill = fillRt;
            display.m_FillImage = fill;
            display.m_Text = text;
            return display;
        }

        public void Init(SurvivalState survival) => m_Survival = survival;

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || m_Survival == null)
                return;

            var h = Mathf.Clamp01(m_Survival.Health / 100f);
            m_Fill.sizeDelta = new Vector2(k_BarWidth * h, 0f);
            m_FillImage.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.35f, 0.8f, 0.45f), h);

            m_Timer -= Time.deltaTime;
            if (m_Timer > 0f)
                return;
            m_Timer = 0.2f;

            string Tag(string word, bool active, string color) =>
                active ? $"<color={color}><b>{word}</b></color>" : $"<color=#555>{word}</color>";

            var drains = Tag(Strings.Cold, m_Survival.ColdRate > 0.07f, "#9fd3ff") + "  " +
                         Tag(Strings.Hunger, m_Survival.HungerRate > 0f, "#ffcf7a") + "  " +
                         Tag(Strings.Thirst, m_Survival.ThirstRate > 0f, "#ffffff");
            if (m_Survival.IsWarm)
                drains = $"<color=#ffb066><b>{Strings.Warm}</b></color>  " + drains;

            var rifle = gm.PlayerRifle;
            var ammo = string.Format(Strings.Ammo, rifle != null ? rifle.Loaded : 0, gm.Pouch.Count);

            var storm = "";
            if (gm.StormActive)
                storm = $"<color=#ff6a5a><b>{Strings.BlizzardNow}</b></color>";
            else if (gm.StormWarning)
                storm = $"<color=#ffd36a><b>{string.Format(Strings.BlizzardSoon, Mathf.CeilToInt(gm.StormCountdown))}</b></color>";

            var t = Mathf.FloorToInt(gm.PlayTime);
            m_Text.text = $"{drains}\n{ammo}    {t / 60:00}:{t % 60:00}\n{storm}";
        }
    }
}
