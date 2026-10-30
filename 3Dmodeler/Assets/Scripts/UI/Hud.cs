using Builder.CameraControl;
using System.Linq;
using Builder.Core;
using Builder.Input;
using Builder.Shapes;
using Builder.Tools;
using Builder.View;
using TMPro;
using UnityEngine;

namespace Builder.UI
{
    /// <summary>
    /// The info panel (top left: tool, shape, snap, grid, rotation, mirror, camera) and the
    /// controls help panel (F1 / gamepad Start).
    /// </summary>
    public class Hud : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] CameraRig cameraRig;
        [SerializeField] BuildTool buildTool;
        [SerializeField] ToolManager tools;
        [SerializeField] Selection selection;
        [SerializeField] PaintTool paintTool;
        [SerializeField] EraseTool eraseTool;
        [SerializeField] DocumentView documentView;
        [SerializeField] TextMeshProUGUI statusText;
        [SerializeField] GameObject helpPanel;
        [SerializeField] TextMeshProUGUI helpText;
        [SerializeField] PrecisionMode precision;
        [SerializeField] TextMeshProUGUI precisionBanner;
        [SerializeField] GameObject crosshair;

        string lastStatus;

        void Update()
        {
            if (input.Pressed(input.Help)) ToggleHelp();
            var bannerBox = precisionBanner.transform.parent.gameObject;   // the dark box around the text
            if (bannerBox.activeSelf != precision.Active)
            {
                bannerBox.SetActive(precision.Active);
                precisionBanner.text = $"<b>PRECISION</b>   mouse moves the piece  ·  wheel: up/down  ·  hold right mouse: look  ·  " +
                                       $"steps {precision.StepSize:0.###} m  ·  {ControlsCatalog.All.First(e => e.action == "Precision").KeyText(input.Actions)} = done";
            }
            crosshair.SetActive(!input.MenuOpen);

            var editor = session.Document.editor;
            string snap = buildTool.Mode == SnapMode.Grid ? $"Grid  {editor.gridSize:0.##} m"
                        : buildTool.Mode == SnapMode.Free ? "Free"
                        : buildTool.Snapped ? "Snap  <color=#7BE07B>snapped</color>" : "Snap";
            string title = tools.Current == ToolKind.Paint ? "Paint"
                         : buildTool.Pasting ? $"Paste  ({buildTool.StampCount})"
                         : buildTool.ItemName;
            string status =
                $"<size=125%><b>{title}</b></size>\n" +
                Row("Tool", ToolText()) +
                Row("Snap", snap) +
                Row("Wheel", WheelText()) +
                Row("Rotate", RotationText()) +
                Row("Size", $"x{buildTool.Scale:0.##}") +
                (buildTool.Mode == SnapMode.Snap ? Row("Attach", $"<color=#FF9A26>{buildTool.AttachName}</color>  <color={UiKit.SubtleHex}>(hold Q)</color>") : "") +
                (precision.Active ? Row("Precise", PreciseText()) : "") +
                (buildTool.FloatHeight >= 0f ? Row("Height", $"{buildTool.FloatHeight:0.00} m above the floor") : "") +
                (buildTool.OverlapCount > 0 && !buildTool.Erasing ? Row("Overlap", $"<color=#FF6B5B>goes into {buildTool.OverlapCount} piece(s)</color>") : "") +
                (buildTool.NearestBone != null && tools.Current == ToolKind.Build ? Row("Bone", BoneText()) : "") +
                Row("Mirror", editor.mirrorX ? "<color=#FF7A6B>On</color>  (M)" : "Off  (M)") +
                Row("Camera", cameraRig.Mode == CameraMode.Fly ? "Fly" : "Orbit") +
                Row("Pieces", documentView.PieceCount.ToString()) +
                Row("Project", session.Document.name + (session.Dirty ? "  <color=#FFB347>*</color>" : ""), last: true);
            if (status == lastStatus) return;   // only rebuild the text when something changed
            statusText.text = status;
            lastStatus = status;
        }

        /// <summary>Shows/hides the controls help (F1). It lists the player's current keys.</summary>
        public void ToggleHelp()
        {
            bool show = !helpPanel.activeSelf;
            if (show) helpText.text = HelpText();
            helpPanel.SetActive(show);
        }

        string HelpText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=125%><b>Controls</b></size>   <color={UiKit.SubtleHex}><size=85%>change keys: Esc > Controls</size></color>\n");
            sb.Append($"<color={UiKit.SubtleHex}>Action<pos=40%>Keyboard & mouse<pos=72%>Gamepad</color>\n");
            foreach (var e in ControlsCatalog.All)
            {
                if (!e.inHelp) continue;
                string label = e.label == "Move forward" ? "Move" : e.label == "Hotbar 1" ? "Hotbar" : e.label;
                string keys = e.label == "Move forward" ? MoveKeys() : e.label == "Hotbar 1" ? "1 - 9" : e.KeyText(input.Actions);
                sb.Append(label).Append("<pos=40%>").Append(keys).Append("<pos=72%>").Append(e.pad).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        // The four move keys in one line, e.g. "W A S D".
        string MoveKeys() => string.Join(" ", new[] { "Move forward", "Move left", "Move back", "Move right" }
            .Select(name => ControlsCatalog.All.First(e => e.label == name).KeyText(input.Actions)));

        // Placing near a skeleton: which bone the piece touches, or how far the nearest one is.
        string BoneText() => buildTool.TouchedBone != null
            ? $"<color=#5BFF66><b>in {buildTool.TouchedBone.name}</b></color>"
            : $"<color={UiKit.SubtleHex}>near {buildTool.NearestBone.name} ({buildTool.NearestBoneDistance:0.00} m)</color>";

        // Which job the mouse wheel has right now, and the key that picks each job.
        string WheelText()
        {
            string On(string name, string key, bool active) =>
                active ? $"<color=#FFD54A><b>{name}</b></color>" : $"<color={UiKit.SubtleHex}>{name} {key}</color>";
            var w = buildTool.Wheel;
            return On("Rotate", "R", w == WheelMode.Rotate) + "  " + On("Size", "E", w == WheelMode.Scale) + "  " +
                   On("Item", "MMB", w == WheelMode.Item) + "  " + On("Far", "Ctrl", w == WheelMode.Distance);
        }

        // e.g. "X +0.25  Y 0  Z -0.5" (how far precision mode has moved the piece).
        string PreciseText()
        {
            var o = precision.Offset;
            string F(float v) => (v > 0f ? "+" : "") + v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return $"<color=#FFD54A>X {F(o.x)}  Y {F(o.y)}  Z {F(o.z)}</color>";
        }

        string ToolText()
        {
            switch (tools.Current)
            {
                case ToolKind.Select: return $"<color=#FFB347>Select</color>  ({selection.Count})";
                case ToolKind.Paint: return $"<color=#7BE07B>Paint</color>  <color={paintTool.CurrentColor}><b>{paintTool.CurrentColor}</b></color>";
                case ToolKind.Erase:
                    return $"<color=#FF6B6B>Eraser</color>  {(eraseTool.Mode == EraseTool.Catch.Cut ? "<color=#FFB347>CUT</color>" : eraseTool.Mode == EraseTool.Catch.Inside ? "fully inside" : "overlap")}  (C)";
                default: return "Build";
            }
        }

        // e.g. "axis Y   0 / 90 / 0": the chosen axis in its color (X red, Y green, Z blue), then the angles.
        string RotationText()
        {
            string[] colors = { "#7BE07B", "#FF6B6B", "#6BA8FF" };   // Y, X, Z (same order as BuildTool.AxisNames)
            int axis = buildTool.RotateAxis;
            var e = buildTool.PieceRotation.eulerAngles;
            return $"axis <color={colors[axis]}><b>{BuildTool.AxisNames[axis]}</b></color>  step {buildTool.RotateStep:0}°" +
                   (buildTool.Flipped ? "  <color=#FFB347>flipped</color>" : "") +
                   $"<color={UiKit.SubtleHex}>   {Mathf.RoundToInt(e.x) % 360} / {Mathf.RoundToInt(e.y) % 360} / {Mathf.RoundToInt(e.z) % 360}</color>";
        }

        // One "Label   value" line; the value starts at a fixed spot so the column lines up.
        static string Row(string label, string value, bool last = false) =>
            $"<color={UiKit.SubtleHex}>{label}</color><pos=42%>{value}" + (last ? "" : "\n");
    }
}
