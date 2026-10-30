using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Builder.Core;
using Builder.Input;
using Builder.Shapes;
using Builder.Tools;
using Builder.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The edit panel (right side) for the selected pieces, shown in the Select tool.
    ///   - One piece: position, rotation and all size settings its shape uses.
    ///   - Several pieces: the size settings; a change goes to every selected piece that uses that setting.
    /// Sliders change the pieces live (one undo step per drag); the number boxes take exact values (Enter).
    /// Gamepad: D-pad up/down picks a row, D-pad left/right changes it.
    /// </summary>
    public class EditPanel : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Selection selection;
        [SerializeField] ToolManager tools;
        [SerializeField] UiTheme theme;
        [SerializeField] GameObject panel;
        [SerializeField] RectTransform rowParent;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] BlueprintDialog blueprintDialog;
        [SerializeField] BonesPanel bones;   // while it is open, the Skeleton tree uses this spot

        enum Field { PosX, PosY, PosZ, RotX, RotY, RotZ, Width, Height, Length, Bevel, Taper, Hollow, Wall, Segments, Cuts }

        enum Style { Stepper, Slider, Toggle }

        class Row
        {
            public Field field;
            public Style style;
            public Image background;
            public Slider slider;
            public TMP_InputField box;
            public TextMeshProUGUI toggleText;
            public float shown = float.NaN;   // value currently on screen (to skip needless updates)
        }

        readonly List<Row> rows = new List<Row>();
        string builtFor;
        int focused;

        static readonly Color RowNormal = new Color(1f, 1f, 1f, 0f);
        static readonly Color RowFocused = new Color(0.36f, 0.74f, 1f, 0.22f);

        void Start() => panel.SetActive(false);

        void Update()
        {
            bool show = tools.Current == ToolKind.Select && selection.Count > 0 && (bones == null || !bones.IsOpen);
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;

            var pieces = selection.Pieces();
            if (pieces.Count == 0) return;
            string layout = LayoutKey(pieces);
            if (layout != builtFor) Build(pieces, layout);

            Refresh(pieces[0]);
            HandleGamepad();
        }

        // ---------- which rows ----------

        static ShapeFeatures FeaturesOf(Piece p) => ShapeRegistry.Get(p.shape)?.Features ?? ShapeFeatures.None;

        static string LayoutKey(List<Piece> pieces)
        {
            var features = pieces.Aggregate(ShapeFeatures.None, (f, p) => f | FeaturesOf(p));
            return (pieces.Count == 1 ? "one:" : "many:") + (int)features + (pieces[0].p.hollow ? ":h" : "")
                   + (pieces.Any(p => p.HasCuts()) ? ":c" : "");
        }

        void Build(List<Piece> pieces, string layout)
        {
            foreach (Transform child in rowParent) Destroy(child.gameObject);
            rows.Clear();
            builtFor = layout;
            focused = 0;

            var features = pieces.Aggregate(ShapeFeatures.None, (f, p) => f | FeaturesOf(p));
            var shape = ShapeRegistry.Get(pieces[0].shape);
            title.text = pieces.Count == 1
                ? $"<b>{shape?.DisplayName ?? pieces[0].shape}</b>"
                : $"<b>{pieces.Count} pieces</b>";

            var actions = UiKit.Row(rowParent, "Actions", 38f);
            UiKit.TextButton(actions.transform, "Save as Blueprint", theme, 190f, blueprintDialog.Open, 16f);

            if (pieces.Count == 1)
            {
                AddRow(Field.PosX, "Position X", Style.Stepper);
                AddRow(Field.PosY, "Position Y", Style.Stepper);
                AddRow(Field.PosZ, "Position Z", Style.Stepper);
                AddRow(Field.RotX, "Rotation X", Style.Stepper);
                AddRow(Field.RotY, "Rotation Y", Style.Stepper);
                AddRow(Field.RotZ, "Rotation Z", Style.Stepper);
            }
            AddRow(Field.Width, "Width", Style.Slider);
            AddRow(Field.Height, "Height", Style.Slider);
            AddRow(Field.Length, "Length", Style.Slider);
            if ((features & ShapeFeatures.Bevel) != 0) AddRow(Field.Bevel, "Bevel", Style.Slider);
            if ((features & ShapeFeatures.Taper) != 0) AddRow(Field.Taper, "Taper", Style.Slider);
            if ((features & ShapeFeatures.Hollow) != 0) AddRow(Field.Hollow, "Hollow", Style.Toggle);
            if ((features & ShapeFeatures.Wall) != 0 || ((features & ShapeFeatures.Hollow) != 0 && pieces[0].p.hollow))
                AddRow(Field.Wall, "Wall", Style.Slider);
            if ((features & ShapeFeatures.Segments) != 0) AddRow(Field.Segments, "Sides", Style.Slider);
            if (pieces.Any(p => p.HasCuts())) AddRow(Field.Cuts, "Cuts", Style.Toggle);   // button: remove all cuts
        }

        // ---------- making the row controls ----------

        void AddRow(Field field, string label, Style style)
        {
            var bg = UiKit.Box(rowParent, field.ToString(), theme, RowNormal);
            var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 6, 3, 3);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            bg.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            var name = UiKit.Label(bg.transform, "Label", theme, 17f, UiKit.SubtleText, TextAlignmentOptions.Left, label);
            Size(name.gameObject, 98f);

            var row = new Row { field = field, style = style, background = bg };
            switch (style)
            {
                case Style.Stepper:
                    StepButton(bg.transform, "-", () => Step(row, -1));
                    row.box = NumberBox(bg.transform, row, flexible: true);
                    StepButton(bg.transform, "+", () => Step(row, 1));
                    break;
                case Style.Slider:
                    row.slider = MakeSlider(bg.transform, row);
                    row.box = NumberBox(bg.transform, row, flexible: false);
                    break;
                default:
                    var button = UiKit.Button(bg.transform, "Toggle", theme);
                    Size(button.gameObject, 84f);
                    row.toggleText = UiKit.Label(button.transform, "Text", theme, 17f, UiKit.Text, TextAlignmentOptions.Center);
                    UiKit.Fill(row.toggleText.rectTransform);
                    button.onClick.AddListener(() => Step(row, 1));
                    break;
            }
            rows.Add(row);
        }

        static void Size(GameObject go, float width, bool flexible = false)
        {
            // (Not "??": Unity's missing-component value only pretends to be null.)
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = flexible ? 1f : 0f;
        }

        void StepButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var button = UiKit.Button(parent, text, theme);
            Size(button.gameObject, 30f);
            var label = UiKit.Label(button.transform, "Text", theme, 20f, UiKit.Text, TextAlignmentOptions.Center, text);
            UiKit.Fill(label.rectTransform);
            button.onClick.AddListener(onClick);
        }

        Slider MakeSlider(Transform parent, Row row)
        {
            var res = new DefaultControls.Resources { standard = theme.rounded, background = theme.rounded, knob = theme.rounded };
            var go = DefaultControls.CreateSlider(res);
            go.transform.SetParent(parent, false);
            Size(go, 120f, flexible: true);
            go.transform.Find("Background").GetComponent<Image>().color = UiKit.Slot;
            go.transform.Find("Fill Area/Fill").GetComponent<Image>().color = UiKit.Accent;
            go.transform.Find("Handle Slide Area/Handle").GetComponent<Image>().color = Color.white;

            var slider = go.GetComponent<Slider>();
            var (min, max) = Range(row.field);
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = row.field == Field.Segments;
            slider.onValueChanged.AddListener(v => Apply(row.field, v, seal: false));
            return slider;
        }

        TMP_InputField NumberBox(Transform parent, Row row, bool flexible)
        {
            var res = new TMP_DefaultControls.Resources { standard = theme.rounded, background = theme.rounded, inputField = theme.rounded };
            var go = TMP_DefaultControls.CreateInputField(res);
            go.transform.SetParent(parent, false);
            Size(go, 76f, flexible);
            go.GetComponent<Image>().color = UiKit.Slot;

            var box = go.GetComponent<TMP_InputField>();
            box.contentType = TMP_InputField.ContentType.DecimalNumber;
            foreach (var text in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.font = theme.font;
                text.fontSize = 17f;
                text.color = UiKit.Text;
                text.alignment = TextAlignmentOptions.Center;
            }
            if (box.placeholder is TextMeshProUGUI placeholder) placeholder.text = "";
            box.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    Apply(row.field, v, seal: true);
                row.shown = float.NaN;   // show the real value again (it may have been clamped)
            });
            return box;
        }

        // ---------- showing values ----------

        void Refresh(Piece first)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.background.color = i == focused && BuilderInput.FromGamepad(input.GridSize) ? RowFocused : RowNormal;
                if (row.box != null && row.box.isFocused) continue;   // don't overwrite what the user is typing

                float value = Get(first, row.field);
                if (Mathf.Approximately(value, row.shown)) continue;
                row.shown = value;
                if (row.slider != null) row.slider.SetValueWithoutNotify(value);
                if (row.box != null) row.box.SetTextWithoutNotify(Format(row.field, value));
                if (row.toggleText != null)
                    row.toggleText.text = row.field == Field.Cuts ? $"Remove {value:0}" : value > 0.5f ? "On" : "Off";
            }
        }

        static string Format(Field f, float v) =>
            f == Field.Segments ? Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture)
            : f >= Field.RotX && f <= Field.RotZ ? v.ToString("0.#", CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---------- gamepad ----------

        void HandleGamepad()
        {
            if (rows.Count == 0 || input.Blocked) return;
            bool Pad(UnityEngine.InputSystem.InputAction a) => input.Pressed(a) && BuilderInput.FromGamepad(a);

            if (Pad(input.GridSize)) focused = (focused + rows.Count - 1) % rows.Count;     // D-pad up
            if (Pad(input.SnapToggle)) focused = (focused + 1) % rows.Count;                // D-pad down
            focused = Mathf.Clamp(focused, 0, rows.Count - 1);
            if (Pad(input.TurnPlus)) Step(rows[focused], 1);                                 // D-pad right
            if (Pad(input.TurnMinus)) Step(rows[focused], -1);                               // D-pad left
        }

        // ---------- changing values ----------

        void Step(Row row, int direction)
        {
            var pieces = selection.Pieces();
            if (pieces.Count == 0) return;
            if (row.field == Field.Cuts)
            {
                Apply(Field.Cuts, 0f, seal: true);   // the piece is whole again
                return;
            }
            if (row.field == Field.Hollow)
            {
                Apply(Field.Hollow, pieces[0].p.hollow ? 0f : 1f, seal: true);
                return;
            }
            Apply(row.field, Get(pieces[0], row.field) + direction * StepSize(row.field), seal: true);
        }

        float StepSize(Field f)
        {
            var editor = session.Document.editor;
            switch (f)
            {
                case Field.RotX: case Field.RotY: case Field.RotZ: return 15f;
                case Field.Segments: return 1f;
                case Field.Bevel: case Field.Wall: case Field.Taper: return 0.05f;
                default: return editor.snapMode == SnapMode.Free ? 0.05f : editor.gridSize;
            }
        }

        static (float min, float max) Range(Field f)
        {
            switch (f)
            {
                case Field.Bevel: return (0f, 0.5f);
                case Field.Taper: return (0f, 1f);
                case Field.Wall: return (0.01f, 1f);
                case Field.Segments: return (3f, 48f);
                default: return (0.05f, 10f);   // width / height / length (the number box allows up to 100)
            }
        }

        /// <summary>Sets one setting on every selected piece as one command (merging while a slider is dragged).</summary>
        void Apply(Field field, float value, bool seal)
        {
            var before = selection.Pieces();
            if (before.Count == 0) return;
            var after = before.Select(p => p.Clone()).ToList();
            foreach (var p in after) Set(p, field, value);
            session.Modify(before, after, "Edit " + field.ToString().ToLowerInvariant(), "edit:" + field);
            if (seal) session.History.SealMerge();
        }

        static float Get(Piece p, Field f)
        {
            var e = p.rotation.ToUnity().eulerAngles;
            switch (f)
            {
                case Field.PosX: return p.position.x;
                case Field.PosY: return p.position.y;
                case Field.PosZ: return p.position.z;
                case Field.RotX: return Mathf.Round(e.x * 10f) / 10f % 360f;
                case Field.RotY: return Mathf.Round(e.y * 10f) / 10f % 360f;
                case Field.RotZ: return Mathf.Round(e.z * 10f) / 10f % 360f;
                case Field.Width: return p.p.width;
                case Field.Height: return p.p.height;
                case Field.Length: return p.p.length;
                case Field.Bevel: return p.p.bevel;
                case Field.Taper: return p.p.taper;
                case Field.Hollow: return p.p.hollow ? 1f : 0f;
                case Field.Wall: return p.p.wall;
                case Field.Cuts: return p.cuts?.Count ?? 0;
                default: return p.p.segments;
            }
        }

        static void Set(Piece p, Field f, float v)
        {
            var features = FeaturesOf(p);
            switch (f)
            {
                case Field.PosX: p.position.x = v; break;
                case Field.PosY: p.position.y = v; break;
                case Field.PosZ: p.position.z = v; break;
                case Field.RotX: case Field.RotY: case Field.RotZ:
                    var e = p.rotation.ToUnity().eulerAngles;
                    if (f == Field.RotX) e.x = v; else if (f == Field.RotY) e.y = v; else e.z = v;
                    p.rotation = Quaternion.Euler(e).ToDoc();
                    break;
                case Field.Width: p.p.width = Mathf.Clamp(v, 0.05f, 100f); break;
                case Field.Height: p.p.height = Mathf.Clamp(v, 0.05f, 100f); break;
                case Field.Length: p.p.length = Mathf.Clamp(v, 0.05f, 100f); break;
                case Field.Bevel: if ((features & ShapeFeatures.Bevel) != 0) p.p.bevel = Mathf.Clamp(v, 0f, 5f); break;
                case Field.Taper: if ((features & ShapeFeatures.Taper) != 0) p.p.taper = Mathf.Clamp01(v); break;
                case Field.Hollow: if ((features & ShapeFeatures.Hollow) != 0) p.p.hollow = v > 0.5f; break;
                case Field.Wall: p.p.wall = Mathf.Clamp(v, 0.01f, 10f); break;
                case Field.Segments: if ((features & ShapeFeatures.Segments) != 0) p.p.segments = Mathf.Clamp(Mathf.RoundToInt(v), 3, 64); break;
                case Field.Cuts: p.cuts = null; break;
            }
        }
    }
}
