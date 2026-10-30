using System.Collections.Generic;
using Builder.Core;
using Builder.UI;
using Builder.Input;
using Builder.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Builder.Tools
{
    /// <summary>
    /// Select tool (I / gamepad Y). The mouse cursor is free; hold the right mouse button to look around.
    ///   - Click a piece: select it. Shift+click: add/remove it. Click empty space: clear.
    ///   - Drag a box with the mouse: select the pieces inside it (Ctrl+drag: add them to the selection).
    ///   - Shift+drag: every piece the mouse passes over gets added (like painting the selection).
    ///   - Gamepad: RT selects what the crosshair aims at.
    ///   - Delete key / LT: delete the selected pieces. Click empty space: clear the selection.
    ///   - Drag the gizmo (arrows / rings / cubes) to move / turn / resize; the edit panel shows exact numbers.
    ///   - Precision mode (V): the mouse moves the selected pieces in exact steps; V again or a click = done
    ///     (the whole move is one undo step).
    /// </summary>
    public class SelectTool : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Selection selection;
        [SerializeField] TransformGizmo gizmo;
        [SerializeField] PrecisionMode precision;
        [SerializeField] Camera viewCamera;
        [SerializeField] PoseTest pose;
        [SerializeField] BoneGizmo boneGizmo;
        [SerializeField] float reach = 100f;

        const float DragStart = 6f;   // pixels the mouse must move before a press becomes a drag

        List<Piece> precisionStart;   // the selected pieces as they were when precision mode began
        bool pressing, dragging, brush, addBox;
        Vector2 pressAt;
        static Texture2D white;
        Vector3 appliedOffset;

        void OnEnable() => precision.Toggled += OnPrecision;

        void OnDisable()
        {
            precision.Toggled -= OnPrecision;
            if (boneGizmo != null) boneGizmo.Hide();
            pressing = dragging = false;
            if (gizmo != null) gizmo.Hide();
        }

        void OnPrecision(bool on)
        {
            if (!on)
            {
                session.History.SealMerge();   // the whole precise move = one undo step
                precisionStart = null;
                return;
            }
            if (selection.Count == 0)
            {
                Toast.Show("Select pieces first, then press V to move them precisely");
                precision.Set(false);
                return;
            }
            precisionStart = selection.Pieces().ConvertAll(p => p.Clone());
            appliedOffset = Vector3.zero;
        }

        /// <summary>Precision mode: keeps the selected pieces at their start spot + the precision offset.</summary>
        void FollowPrecision()
        {
            if (input.Pressed(input.Place))
            {
                precision.Set(false);   // a click = done
                return;
            }
            if (precision.Offset == appliedOffset) return;
            appliedOffset = precision.Offset;
            var moved = precisionStart.ConvertAll(p =>
            {
                var c = p.Clone();
                c.position = (p.position.ToUnity() + appliedOffset).ToDoc();
                return c;
            });
            session.Modify(precisionStart, moved, "Move (precise)", "precise");
        }

        void Update()
        {
            // Releasing the mouse button ends any slider or gizmo drag: the next change is a new undo step.
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) session.History.SealMerge();

            if (input.Blocked) return;
            bool mouseActive = !input.CursorLocked && !BuilderInput.PointerOverUI;

            // Bones panel open: the bone handles (arrows / rings / length cube) instead of the piece handles.
            bool bones = boneGizmo != null && boneGizmo.Active;
            if (bones)
            {
                gizmo.Hide();
                if (boneGizmo.Tick(mouseActive && !pressing)) return;   // on a bone handle, or dragging one
            }
            if (pose != null && pose.Active)
            {
                // Pose test: the picture is posed, so editing pieces would be confusing - but bones can be chosen and turned.
                if (input.Pressed(input.Place) && BuilderInput.FromMouse(input.Place) && mouseActive)
                    boneGizmo.TryPick(BuilderInput.PointerPosition);
                return;
            }

            if (precision.Active && precisionStart != null)
            {
                gizmo.Tick(false);   // keep drawing it, but the mouse is busy moving the pieces
                FollowPrecision();
                return;
            }

            if (!gizmo.Dragging)
            {
                if (input.Pressed(input.Delete) && !BuilderInput.FromMouse(input.Delete) && selection.Count > 0)
                    session.Remove(selection.Ids, selection.Count == 1 ? "Delete" : $"Delete {selection.Count} pieces");

            }

            if (!bones && gizmo.Tick(mouseActive && !pressing)) return;   // the gizmo got this click

            if (input.Pressed(input.Place))
            {
                if (!BuilderInput.FromMouse(input.Place)) ClickSelect(viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)));
                else if (mouseActive && bones && boneGizmo.TryPick(BuilderInput.PointerPosition)) { }   // a bone got it
                else if (mouseActive)
                {
                    // The mouse: a click selects when the button goes up; moving first makes it a drag.
                    pressing = true;
                    dragging = false;
                    pressAt = BuilderInput.PointerPosition;
                    brush = BuilderInput.ShiftHeld;
                    addBox = BuilderInput.CtrlHeld;
                }
            }
            if (pressing) TrackMouse();
        }

        void TrackMouse()
        {
            var mouse = Mouse.current;
            var at = BuilderInput.PointerPosition;
            if (!dragging && Vector2.Distance(at, pressAt) > DragStart) dragging = true;
            if (dragging && brush)
            {
                var piece = PieceUnder(at);
                if (piece != null) selection.Add(new[] { piece.PieceId });
            }
            if (mouse != null && mouse.leftButton.isPressed) return;

            // Button released.
            pressing = false;
            if (!dragging) ClickSelect(viewCamera.ScreenPointToRay(pressAt));
            else if (!brush) BoxSelect(pressAt, at, addBox);
            dragging = false;
        }

        PieceView PieceUnder(Vector2 screen) =>
            Physics.Raycast(viewCamera.ScreenPointToRay(screen), out var hit, reach) ? hit.collider.GetComponent<PieceView>() : null;

        /// <summary>Selects the pieces whose middle is inside the screen box (Ctrl: adds them).</summary>
        void BoxSelect(Vector2 a, Vector2 b, bool add)
        {
            var box = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            var inside = new List<string>();
            foreach (var view in FindObjectsByType<PieceView>(FindObjectsSortMode.None))
            {
                var r = view.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                var p = viewCamera.WorldToScreenPoint(r.bounds.center);
                if (p.z > 0f && box.Contains(new Vector2(p.x, p.y))) inside.Add(view.PieceId);
            }
            if (add) selection.Add(inside);
            else selection.SetMany(inside);
        }

        void OnGUI()
        {
            if (!pressing || !dragging || brush) return;
            if (white == null) white = Texture2D.whiteTexture;
            var a = pressAt;
            var b = BuilderInput.PointerPosition;
            // OnGUI counts y from the top of the screen.
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
            var old = GUI.color;
            GUI.color = new Color(1f, 0.6f, 0.15f, 0.15f);
            GUI.DrawTexture(r, white);
            GUI.color = new Color(1f, 0.6f, 0.15f, 0.9f);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, 2f), white);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - 2f, r.width, 2f), white);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, 2f, r.height), white);
            GUI.DrawTexture(new Rect(r.xMax - 2f, r.yMin, 2f, r.height), white);
            GUI.color = old;
        }

        void ClickSelect(Ray ray)
        {
            // The mouse picks under the cursor; a gamepad picks under the crosshair.
            var piece = Physics.Raycast(ray, out var hit, reach) ? hit.collider.GetComponent<PieceView>() : null;
            bool add = BuilderInput.ShiftHeld;

            if (piece != null)
            {
                if (add) selection.Toggle(piece.PieceId);
                else selection.Set(piece.PieceId);
            }
            else if (!add)
            {
                selection.Clear();
            }
        }
    }
}
