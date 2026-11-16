using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Untrodden.UI
{
    /// <summary>One floating world-space panel reused for the title, death and result screens. Pointed at with the controller ray.</summary>
    public class MenuPanel : MonoBehaviour
    {
        Canvas m_Canvas;
        TextMeshProUGUI m_Title;
        TextMeshProUGUI m_Body;
        RectTransform m_Buttons;
        readonly List<GameObject> m_ButtonObjects = new List<GameObject>();

        /// <summary>Font used for every text this game creates (Korean-capable). Set before building UI.</summary>
        public static TMP_FontAsset Font;

        public bool IsOpen => m_Canvas.gameObject.activeSelf;

        public static MenuPanel Create(Camera eventCamera)
        {
            var go = new GameObject("Menu Panel", typeof(RectTransform));
            go.transform.localScale = Vector3.one * 0.001f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1000f, 820f);

            var bg = CreateImage(rt, "Background", new Color(0.04f, 0.06f, 0.09f, 0.88f));
            Stretch(bg.rectTransform);

            var panel = go.AddComponent<MenuPanel>();
            panel.m_Canvas = canvas;

            panel.m_Title = CreateText(rt, "Title", "", 72f, TextAlignmentOptions.Center);
            var titleRt = panel.m_Title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -40f);
            titleRt.sizeDelta = new Vector2(-80f, 110f);

            panel.m_Body = CreateText(rt, "Body", "", 32f, TextAlignmentOptions.Top);
            var bodyRt = panel.m_Body.rectTransform;
            bodyRt.anchorMin = new Vector2(0f, 0f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.offsetMin = new Vector2(60f, 160f);
            bodyRt.offsetMax = new Vector2(-60f, -160f);

            var buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(rt, false);
            panel.m_Buttons = (RectTransform)buttons.transform;
            panel.m_Buttons.anchorMin = new Vector2(0f, 0f);
            panel.m_Buttons.anchorMax = new Vector2(1f, 0f);
            panel.m_Buttons.pivot = new Vector2(0.5f, 0f);
            panel.m_Buttons.anchoredPosition = new Vector2(0f, 40f);
            panel.m_Buttons.sizeDelta = new Vector2(-80f, 100f);
            var layout = buttons.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            go.SetActive(false);
            return panel;
        }

        /// <summary>Show the panel 1.6 m in front of the head, level with the eyes.</summary>
        public void Show(Transform head, string title, string body, params (string label, Action action)[] buttons)
        {
            m_Title.text = title;
            m_Body.text = body;

            foreach (var b in m_ButtonObjects)
                Destroy(b);
            m_ButtonObjects.Clear();
            foreach (var (label, action) in buttons)
                m_ButtonObjects.Add(CreateButton(m_Buttons, label, action));

            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            transform.position = head.position + forward * 1.6f;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        // ------------------------------------------------------------------ UI helpers (shared with the wrist display)

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (Font != null)
                tmp.font = Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = new Color(0.92f, 0.95f, 1f);
            tmp.richText = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static GameObject CreateButton(Transform parent, string label, Action action)
        {
            var img = CreateImage(parent, "Button " + label, new Color(0.25f, 0.45f, 0.6f, 1f));
            img.rectTransform.sizeDelta = new Vector2(440f, 90f);
            var button = img.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(0.45f, 0.7f, 0.9f);
            colors.pressedColor = new Color(0.8f, 0.9f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => action?.Invoke());

            var text = CreateText(img.transform, "Label", label, 30f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return img.gameObject;
        }
    }
}
