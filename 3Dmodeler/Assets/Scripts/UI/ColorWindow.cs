using System;
using Builder.Input;
using Builder.Tools;
using Builder.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>Calls <see cref="Changed"/> with the pointer's spot (0..1 in both directions) while it is pressed / dragged here.</summary>
    public class DragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2> Changed;

        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);

        void Report(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var r = rt.rect;
            Changed?.Invoke(new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x), Mathf.InverseLerp(r.yMin, r.yMax, local.y)));
        }
    }

    /// <summary>
    /// The Colors window (T in the Paint tool): make your own colors and manage the project's palette.
    ///   - Big square = how strong / how bright, bar = which color (hue), or type a code like #FF8800.
    ///   - Finish: Matte (paint), Metal (shiny) or Glow (lights, visors, thrusters). Also in the .glb export.
    ///   - Click a palette color to edit it: "Change" replaces it, "Add new" adds the color, "Remove" deletes it.
    ///     (Pieces already painted keep their color.)
    /// </summary>
    public class ColorWindow : MonoBehaviour
    {
        const int SquareSize = 64;

        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] PaintTool paintTool;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform canvasRoot;

        GameObject screen;
        RawImage square, hueBar, preview;
        RectTransform squareMarker, hueMarker, paletteRow;
        TMP_InputField hexBox;
        TextMeshProUGUI info;
        Button[] finishButtons;
        Texture2D squareTex;
        float h, s = 0.6f, v = 0.8f;
        ColorFinish.Finish finish;
        int editing;

        public bool IsOpen => screen != null && screen.activeSelf;

        void Update()
        {
            if (IsOpen && input.PressedInMenu(input.Cancel)) Close();
        }

        public void Open()
        {
            if (screen == null) Build();
            editing = paintTool.ColorIndex;
            Load(paintTool.CurrentColor);
            screen.SetActive(true);
            input.SetMenuOpen(true);
            RefreshPalette();
        }

        public void Close()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            screen.SetActive(false);
            input.SetMenuOpen(false);
        }

        /// <summary>The color being made, as "#RRGGBB" (+ "/metal" or "/glow").</summary>
        public string Current => ColorFinish.Make(Conversions.ColorToHex(Color.HSVToRGB(h, s, v)), finish);

        void Load(string color)
        {
            Color.RGBToHSV(Conversions.HexToColor(color), out h, out s, out v);
            finish = ColorFinish.Of(color);
            ShowColor(true);
        }

        // ---------- palette actions ----------

        void AddNew()
        {
            var palette = paintTool.Palette;
            if (palette.Count >= PaintTool.MaxColors)
            {
                Toast.Show($"The palette is full ({PaintTool.MaxColors} colors) - change or remove one");
                return;
            }
            palette.Add(Current);
            editing = palette.Count - 1;
            Changed("Added " + Current);
        }

        void Change()
        {
            var palette = paintTool.Palette;
            if (editing < 0 || editing >= palette.Count) return;
            palette[editing] = Current;
            Changed("Changed color " + (editing + 1));
        }

        void Remove()
        {
            var palette = paintTool.Palette;
            if (palette.Count <= 1 || editing < 0 || editing >= palette.Count) return;
            palette.RemoveAt(editing);
            editing = Mathf.Clamp(editing, 0, palette.Count - 1);
            Changed("Removed a color");
        }

        void Changed(string message)
        {
            session.MarkDirty();   // the palette is saved with the project
            paintTool.SelectColor(editing);
            RefreshPalette();
            Toast.Show(message);
        }

        // ---------- showing ----------

        void ShowColor(bool updateHexBox)
        {
            var c = Color.HSVToRGB(h, s, v);
            preview.color = c;
            if (updateHexBox && !hexBox.isFocused) hexBox.SetTextWithoutNotify(Conversions.ColorToHex(c));
            PaintSquare();
            var sr = square.rectTransform.rect;
            squareMarker.anchoredPosition = new Vector2(s * sr.width, v * sr.height);
            hueMarker.anchoredPosition = new Vector2(0f, h * hueBar.rectTransform.rect.height);
            for (int i = 0; i < finishButtons.Length; i++)
                finishButtons[i].image.color = i == (int)finish ? UiKit.Accent * new Color(1f, 1f, 1f, 0.6f) : UiKit.Slot;
            info.text = $"<b>{Current}</b>   <color={UiKit.SubtleHex}>editing palette color {editing + 1}</color>";
        }

        void PaintSquare()
        {
            for (int y = 0; y < SquareSize; y++)
            for (int x = 0; x < SquareSize; x++)
                squareTex.SetPixel(x, y, Color.HSVToRGB(h, x / (SquareSize - 1f), y / (SquareSize - 1f)));
            squareTex.Apply();
        }

        void RefreshPalette()
        {
            foreach (Transform child in paletteRow) Destroy(child.gameObject);
            var palette = paintTool.Palette;
            for (int i = 0; i < palette.Count; i++)
            {
                int index = i;
                var b = UiKit.Button(paletteRow, "Color " + (i + 1), theme);
                b.image.color = Conversions.HexToColor(palette[i]);
                var tint = b.colors;   // buttons normally darken their color; swatches must show the real color
                tint.normalColor = Color.white;
                tint.highlightedColor = tint.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                b.colors = tint;
                var f = ColorFinish.Of(palette[i]);
                if (f != ColorFinish.Finish.Matte)
                {
                    var mark = UiKit.Label(b.transform, "Finish", theme, 12f, Color.white, TextAlignmentOptions.BottomRight, f == ColorFinish.Finish.Metal ? "M" : "G");
                    UiKit.Fill(mark.rectTransform, 2f);
                    mark.outlineWidth = 0.3f;
                }
                if (i == editing)
                {
                    var frame = b.gameObject.AddComponent<Outline>();
                    frame.effectColor = Color.white;
                    frame.effectDistance = new Vector2(3f, -3f);
                }
                b.onClick.AddListener(() =>
                {
                    editing = index;
                    Load(paintTool.Palette[index]);
                    paintTool.SelectColor(index);
                    RefreshPalette();
                });
            }
            ShowColor(true);
        }

        // ---------- building ----------

        void Build()
        {
            var overlay = UiKit.Rect(canvasRoot, "Color Window");
            UiKit.Fill(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            screen = overlay.gameObject;

            var panel = UiKit.Box(overlay, "Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680f, 560f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var p = panel.rectTransform;

            var title = UiKit.Label(p, "Title", theme, 28f, UiKit.Text, TextAlignmentOptions.Left, "<b>Colors</b>");
            UiKit.Pin(title.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(400f, 40f));
            info = UiKit.Label(p, "Info", theme, 16f, UiKit.Text, TextAlignmentOptions.Left);
            UiKit.Pin(info.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -56f), new Vector2(620f, 26f));

            // Saturation (x) / brightness (y) square.
            squareTex = new Texture2D(SquareSize, SquareSize, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            square = UiKit.Rect(p, "Square").gameObject.AddComponent<RawImage>();
            square.texture = squareTex;
            UiKit.Pin(square.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -92f), new Vector2(240f, 240f));
            square.gameObject.AddComponent<DragArea>().Changed = at => { s = at.x; v = at.y; ShowColor(true); };
            squareMarker = Marker(square.rectTransform, new Vector2(14f, 14f));

            // Hue bar.
            var hueTex = new Texture2D(1, 64, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) hueTex.SetPixel(0, y, Color.HSVToRGB(y / 63f, 1f, 1f));
            hueTex.Apply();
            hueBar = UiKit.Rect(p, "Hue").gameObject.AddComponent<RawImage>();
            hueBar.texture = hueTex;
            UiKit.Pin(hueBar.rectTransform, new Vector2(0f, 1f), new Vector2(276f, -92f), new Vector2(32f, 240f));
            hueBar.gameObject.AddComponent<DragArea>().Changed = at => { h = Mathf.Clamp(at.y, 0f, 0.999f); ShowColor(true); };
            hueMarker = Marker(hueBar.rectTransform, new Vector2(40f, 6f));
            hueMarker.anchorMin = hueMarker.anchorMax = new Vector2(0.5f, 0f);

            // Right column: preview, code, finish.
            preview = UiKit.Rect(p, "Preview").gameObject.AddComponent<RawImage>();
            UiKit.Pin(preview.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -92f), new Vector2(320f, 70f));
            var codeLabel = UiKit.Label(p, "Code", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Left, "Code");
            UiKit.Pin(codeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -174f), new Vector2(60f, 34f));
            var codeRow = UiKit.Row(p, "Code Box", 34f);
            UiKit.Pin((RectTransform)codeRow.transform, new Vector2(0f, 1f), new Vector2(390f, -174f), new Vector2(160f, 34f));
            hexBox = UiKit.InputBox(codeRow.transform, theme, 150f, false, TMP_InputField.ContentType.Standard);
            hexBox.onEndEdit.AddListener(text =>
            {
                string t = text.Trim();
                if (!t.StartsWith("#")) t = "#" + t;
                if (ColorUtility.TryParseHtmlString(t, out var c)) { Color.RGBToHSV(c, out h, out s, out v); ShowColor(false); }
            });

            var finishRow = UiKit.Row(p, "Finish", 38f);
            UiKit.Pin((RectTransform)finishRow.transform, new Vector2(0f, 1f), new Vector2(330f, -222f), new Vector2(320f, 38f));
            finishButtons = new Button[3];
            string[] names = { "Matte", "Metal", "Glow" };
            for (int i = 0; i < 3; i++)
            {
                var f = (ColorFinish.Finish)i;
                finishButtons[i] = UiKit.TextButton(finishRow.transform, names[i], theme, 100f, () => { finish = f; ShowColor(true); }, 17f);
            }
            var hint = UiKit.Label(p, "Hint", theme, 14f, UiKit.SubtleText, TextAlignmentOptions.TopLeft,
                "Matte = paint.  Metal = shiny steel.  Glow = lights, visors, thruster fire (glows in your game too).");
            hint.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Pin(hint.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -268f), new Vector2(320f, 60f));

            // Palette.
            var paletteTitle = UiKit.Label(p, "Palette Title", theme, 17f, UiKit.Accent, TextAlignmentOptions.Left,
                "<b>Palette</b>  <size=80%><color=#9EA7B3>click a color to edit it</color></size>");
            UiKit.Pin(paletteTitle.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -344f), new Vector2(620f, 28f));
            paletteRow = UiKit.Rect(p, "Palette");
            UiKit.Pin(paletteRow, new Vector2(0f, 1f), new Vector2(24f, -376f), new Vector2(632f, 80f));
            var grid = paletteRow.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(34f, 34f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 16;

            var buttons = UiKit.Row(p, "Buttons", 44f);
            UiKit.Pin((RectTransform)buttons.transform, new Vector2(0f, 0f), new Vector2(24f, 20f), new Vector2(632f, 44f));
            buttons.childAlignment = TextAnchor.MiddleRight;
            UiKit.TextButton(buttons.transform, "Add new color", theme, 160f, AddNew, 17f);
            UiKit.TextButton(buttons.transform, "Change this color", theme, 180f, Change, 17f);
            UiKit.TextButton(buttons.transform, "Remove", theme, 100f, Remove, 17f);
            UiKit.TextButton(buttons.transform, "Close", theme, 100f, Close, 17f);
            screen.SetActive(false);
        }

        RectTransform Marker(RectTransform parent, Vector2 size)
        {
            var m = UiKit.Box(parent, "Marker", theme, new Color(1f, 1f, 1f, 0.9f));
            m.raycastTarget = false;
            var rt = m.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            var outline = m.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            return rt;
        }
    }
}
