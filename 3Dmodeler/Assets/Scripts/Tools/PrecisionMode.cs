using System;
using Builder.Core;
using Builder.Input;
using Builder.UI;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Precision mode (V): the mouse moves the piece instead of turning the camera, in exact steps.
    ///   - mouse left/right and forward/back: along the ground (the world axis X or Z closest to that direction)
    ///   - mouse wheel: up / down (Y), one step per notch
    ///   - hold the right mouse button: look around (the piece stays where it is)
    ///   - GRID mode: grid-size steps. SNAP / FREE mode: 1 cm steps (overlapping is fine).
    ///   - Gamepad: the right stick moves along the ground the same way (a gamepad on/off button comes with the gamepad menu).
    /// The Build / Eraser tool moves its ghost by <see cref="Offset"/>; the Select tool moves the selected pieces.
    /// Switching tools or opening a menu turns it off.
    /// </summary>
    public class PrecisionMode : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] ToolManager tools;
        [SerializeField] Camera viewCamera;

        const float GridPixelsPerStep = 30f;   // mouse pixels for one grid step
        const float FineStep = 0.01f;          // 1 cm in SNAP / FREE mode
        const float FinePixelsPerStep = 3f;
        const float StickPixelsPerSecond = 600f;   // the gamepad stick counts like this much mouse movement

        public bool Active => input.PrecisionOn;

        /// <summary>How far the piece has been moved since precision mode was turned on (meters).</summary>
        public Vector3 Offset { get; private set; }

        /// <summary>Raised when precision mode turns on (true) or off (false).</summary>
        public event Action<bool> Toggled;

        /// <summary>The size of one step right now.</summary>
        public float StepSize => session.Document.editor.snapMode == SnapMode.Grid ? session.Document.editor.gridSize : FineStep;
        float PixelsPerStep => session.Document.editor.snapMode == SnapMode.Grid ? GridPixelsPerStep : FinePixelsPerStep;

        Vector3 pixels;   // mouse movement not yet turned into steps: x = right, y = up/down, z = forward

        void Update()
        {
            // A menu or another tool ends precision mode.
            if (Active && (input.MenuOpen || tools.Current == ToolKind.Paint)) Set(false);

            if (input.Pressed(input.Precision) && !BuilderInput.CtrlHeld)
            {
                if (tools.Current == ToolKind.Paint) Toast.Show("Precision mode works in Build, Eraser and Select");
                else Set(!Active);
            }

            if (Active) Follow();
        }

        public void Set(bool on)
        {
            if (on == Active) return;
            Offset = Vector3.zero;
            pixels = Vector3.zero;
            input.SetPrecision(on);
            Toggled?.Invoke(on);
            Toast.Show(on ? "Precision ON - mouse moves the piece (wheel: up/down, hold right mouse: look). V = done" : "Precision OFF");
        }

        /// <summary>Moves the offset by an exact amount (used by tests and future buttons).</summary>
        public void Nudge(Vector3 worldDelta) => Offset += worldDelta;

        void Follow()
        {
            Vector2 mouse = input.PrecisionMouse + input.TouchPrecision + input.PrecisionStick * (StickPixelsPerSecond * Time.deltaTime);
            pixels.z += mouse.y;
            pixels.x += mouse.x;

            float per = PixelsPerStep;
            int right = PrecisionMath.TakeSteps(ref pixels.x, per);
            int forward = PrecisionMath.TakeSteps(ref pixels.z, per);
            // The mouse wheel: one step up / down per notch.
            int up = 0;
            if (input.Pressed(input.TurnPlus) && BuilderInput.FromMouse(input.TurnPlus)) up++;
            if (input.Pressed(input.TurnMinus) && BuilderInput.FromMouse(input.TurnMinus)) up--;
            if (right == 0 && up == 0 && forward == 0) return;

            var cam = viewCamera.transform;
            Nudge((PrecisionMath.GroundAxis(cam.right) * right
                   + Vector3.up * up
                   + PrecisionMath.GroundAxis(cam.forward) * forward) * StepSize);
        }
    }
}
