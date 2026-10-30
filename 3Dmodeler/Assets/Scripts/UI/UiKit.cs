using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>Small helpers that create styled UI pieces, so every panel and button looks the same.</summary>
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.06f, 0.07f, 0.09f, 0.82f);
        public static readonly Color Slot = new Color(0.16f, 0.17f, 0.20f, 0.92f);
        public static readonly Color SlotHover = new Color(0.24f, 0.26f, 0.30f, 0.95f);
        public static readonly Color Accent = new Color(0.36f, 0.74f, 1f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.95f, 0.98f, 1f);
        public static readonly Color SubtleText = new Color(0.62f, 0.67f, 0.74f, 1f);
        public const string SubtleHex = "#9EA7B3";
        public const string AccentHex = "#5CBDFF";

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A rounded, colored box.</summary>
        public static Image Box(Transform parent, string name, UiTheme theme, Color color, bool clickable = false)
        {
            var img = Rect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = theme.rounded;
            img.type = Image.Type.Sliced;   // corners keep their shape at any size
            img.color = color;
            img.raycastTarget = clickable;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, UiTheme theme, float size, Color color,
                                            TextAlignmentOptions align, string text = "")
        {
            var t = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = theme.font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.text = text;
            return t;
        }

        /// <summary>A rounded button that lights up when hovered or selected with a gamepad.</summary>
        public static Button Button(Transform parent, string name, UiTheme theme)
        {
            var bg = Box(parent, name, theme, Color.white, clickable: true);
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.normalColor = Slot;
            colors.highlightedColor = SlotHover;
            colors.selectedColor = SlotHover;
            colors.pressedColor = Accent;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            return button;
        }

        /// <summary>A button with a text label, sized for a row (layout width <paramref name="width"/>).</summary>
        public static Button TextButton(Transform parent, string text, UiTheme theme, float width,
                                        UnityEngine.Events.UnityAction onClick, float fontSize = 16f)
        {
            var button = Button(parent, text, theme);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            var label = Label(button.transform, "Text", theme, fontSize, Text, TextAlignmentOptions.Center, text);
            Fill(label.rectTransform);
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>A one-line text box (layout width <paramref name="width"/>, or stretching when flexible).</summary>
        public static TMP_InputField InputBox(Transform parent, UiTheme theme, float width, bool flexible,
                                              TMP_InputField.ContentType type, float fontSize = 16f)
        {
            var res = new TMP_DefaultControls.Resources { standard = theme.rounded, background = theme.rounded, inputField = theme.rounded };
            var go = TMP_DefaultControls.CreateInputField(res);
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.flexibleWidth = flexible ? 1f : 0f;
            go.GetComponent<Image>().color = Slot;

            var box = go.GetComponent<TMP_InputField>();
            box.contentType = type;
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                t.font = theme.font;
                t.fontSize = fontSize;
                t.color = Text;
            }
            if (box.placeholder is TextMeshProUGUI placeholder) placeholder.text = "";
            return box;
        }

        /// <summary>A horizontal row for a list (fixed height), children laid out left to right.</summary>
        public static HorizontalLayoutGroup Row(Transform parent, string name, float height)
        {
            var rt = Rect(parent, name);
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return layout;
        }

        /// <summary>
        /// A scrolling list filling <paramref name="area"/>: a "viewport" window that hides what doesn't fit, with a
        /// content column inside that grows with its rows. Add rows to the returned content.
        /// </summary>
        public static RectTransform ScrollList(RectTransform area)
        {
            var viewport = Rect(area, "Viewport");
            Fill(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;   // so the wheel / drag reaches the list
            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(0, 12, 0, 8);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        /// <summary>Stretches <paramref name="rt"/> across the parent's width, <paramref name="fromTop"/> pixels down.</summary>
        public static void TopStrip(RectTransform rt, float fromTop, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -fromTop - height);
            rt.offsetMax = new Vector2(0f, -fromTop);
        }

        /// <summary>Fills the parent, leaving <paramref name="padding"/> pixels free on every side.</summary>
        public static void Fill(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Pins a rectangle to one spot of the parent (anchor), e.g. (0.5, 0) = bottom center.</summary>
        public static void Pin(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }
    }
}
