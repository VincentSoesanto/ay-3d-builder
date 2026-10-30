using Builder.Input;
using Builder.UI;
using UnityEngine;

namespace Builder.Tools
{
    public enum ToolKind { Build, Select, Paint, Erase }

    /// <summary>
    /// Switches tools:
    ///   - I: Build <-> Select
    ///   - P: Paint (again = back to Build), X: Eraser (again = back to Build)
    ///   - gamepad Y: Build -> Select -> Paint -> Eraser -> Build
    /// Pressing a hotbar number while selecting goes back to building with that shape.
    /// </summary>
    public class ToolManager : MonoBehaviour
    {
        [SerializeField] BuilderInput input;
        [SerializeField] BuildTool buildTool;
        [SerializeField] SelectTool selectTool;
        [SerializeField] PaintTool paintTool;
        [SerializeField] PrecisionMode precision;

        public ToolKind Current { get; private set; } = ToolKind.Build;

        void Start() => SetTool(ToolKind.Build, quiet: true);

        void Update()
        {
            if (input.Pressed(input.ToggleTool))
            {
                if (BuilderInput.FromGamepad(input.ToggleTool)) SetTool((ToolKind)(((int)Current + 1) % 4));
                else SetTool(Current == ToolKind.Select ? ToolKind.Build : ToolKind.Select);
            }
            if (input.Pressed(input.PaintTool)) SetTool(Current == ToolKind.Paint ? ToolKind.Build : ToolKind.Paint);
            if (input.Pressed(input.EraseTool)) SetTool(Current == ToolKind.Erase ? ToolKind.Build : ToolKind.Erase);

            if (Current == ToolKind.Select)
                foreach (var slot in input.Slots)
                    if (input.Pressed(slot)) SetTool(ToolKind.Build);
        }

        public void SetTool(ToolKind kind, bool quiet = false)
        {
            if (kind != Current) precision.Set(false);   // precision mode belongs to the tool it was started in
            Current = kind;
            // The Eraser is the build tool in erase mode (same controls, removes instead of placing).
            buildTool.enabled = kind == ToolKind.Build || kind == ToolKind.Erase;
            buildTool.SetErasing(kind == ToolKind.Erase);
            selectTool.enabled = kind == ToolKind.Select;
            paintTool.enabled = kind == ToolKind.Paint;
            input.SetPointerMode(kind == ToolKind.Select);   // only Select uses a free mouse cursor
            if (!quiet) Toast.Show(kind + " tool");
        }
    }
}
