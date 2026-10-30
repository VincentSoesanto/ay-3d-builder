using System.Collections.Generic;
using Builder.Core;
using Builder.Input;
using Builder.Shapes;
using Builder.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Builder.Tools
{
    /// <summary>
    /// The handles drawn on the selection, used with the mouse in the Select tool:
    ///   - arrows (X red, Y green, Z blue): drag to move along that direction
    ///   - rings: drag to turn around that direction
    ///   - white cubes on the faces (one piece selected): drag to make the piece bigger/smaller on that side only
    /// In GRID and SNAP mode moves/resizes follow the grid size and turns go in 15 degree steps; FREE = no steps.
    /// The whole drag is one undo step. The gizmo stays the same size on screen however far away it is.
    /// </summary>
    public class TransformGizmo : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Selection selection;
        [SerializeField] Camera viewCamera;
        [SerializeField] Material overlayMaterial;   // draws on top of everything

        const float ScreenSize = 0.13f;      // gizmo size as a fraction of its distance to the camera
        const float PickPixels = 12f;        // how close (in pixels) the mouse must be to grab something
        const float TurnStep = 15f;          // degrees, in GRID/SNAP mode
        const float MinSize = 0.05f;         // smallest piece side when resizing

        enum Kind { None, Move, Turn, Resize }

        struct Target
        {
            public Kind kind;
            public int index;   // Move/Turn: 0 = X, 1 = Y, 2 = Z. Resize: handle 0-5 (axis = index / 2, side = even ? + : -)
        }

        static readonly Vector3[] AxisDirs = { Vector3.right, Vector3.up, Vector3.forward };
        static readonly Color[] AxisColors = { new Color(1f, 0.35f, 0.35f, 0.9f), new Color(0.4f, 0.9f, 0.4f, 0.9f), new Color(0.35f, 0.6f, 1f, 0.9f) };
        static readonly Color HoverColor = new Color(1f, 0.88f, 0.2f, 1f);
        static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.9f);
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly MeshRenderer[] arrows = new MeshRenderer[3];
        readonly MeshRenderer[] rings = new MeshRenderer[3];
        readonly MeshRenderer[] handles = new MeshRenderer[6];
        MaterialPropertyBlock block;

        // Layout of this frame.
        Vector3 center;
        float scale;
        readonly Vector3[] handlePositions = new Vector3[6];
        readonly Vector3[] handleDirs = new Vector3[6];
        bool handlesShown;

        // Drag in progress.
        Target hovered, dragging;
        List<Piece> startPieces;
        Vector3 dragOrigin, dragAxis, turnReference;
        float dragStart, lastDelta;

        /// <summary>True while an arrow, ring or handle is being dragged.</summary>
        public bool Dragging => dragging.kind != Kind.None;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            var cube = ShapeRegistry.GetMesh("box", new ShapeParams { width = 1f, height = 1f, length = 1f }, false);
            for (int i = 0; i < 3; i++)
            {
                arrows[i] = Part("Move " + "XYZ"[i], GizmoMeshes.StraightArrow);
                rings[i] = Part("Turn " + "XYZ"[i], GizmoMeshes.Ring);
            }
            for (int i = 0; i < 6; i++) handles[i] = Part("Resize " + i, cube);
            Hide();
        }

        MeshRenderer Part(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = overlayMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public void Hide()
        {
            foreach (var r in arrows) r.gameObject.SetActive(false);
            foreach (var r in rings) r.gameObject.SetActive(false);
            foreach (var r in handles) r.gameObject.SetActive(false);
            if (Dragging) EndDrag();
        }

        /// <summary>
        /// Called every frame by the Select tool. <paramref name="mouseActive"/> = the free mouse cursor can use the gizmo
        /// (not over a panel, not looking around). Returns true if the mouse is on the gizmo or dragging it,
        /// so the click must not also select/deselect pieces.
        /// </summary>
        public bool Tick(bool mouseActive)
        {
            var bounds = selection.Bounds;
            if (bounds == null)
            {
                Hide();
                return false;
            }

            Layout(bounds.Value);

            if (Dragging)
            {
                if (Mouse.current == null || !Mouse.current.leftButton.isPressed) EndDrag();
                else UpdateDrag();
                Paint(dragging);
                return true;
            }

            hovered = mouseActive ? Pick(BuilderInput.PointerPosition) : default;
            Paint(hovered);
            if (hovered.kind != Kind.None && input.Pressed(input.Place) && BuilderInput.FromMouse(input.Place))
            {
                BeginDrag(hovered);
                return true;
            }
            return hovered.kind != Kind.None;
        }

        // ---------- layout + look ----------

        void Layout(Bounds bounds)
        {
            center = bounds.center;
            scale = Vector3.Distance(viewCamera.transform.position, center) * ScreenSize;
            for (int i = 0; i < 3; i++)
            {
                var turn = Quaternion.FromToRotation(Vector3.up, AxisDirs[i]);
                Place(arrows[i], center, turn, scale);
                Place(rings[i], center, turn, scale * 0.75f);
            }

            // Resize handles: only for one piece, on the middle of each face (the piece may be turned).
            handlesShown = selection.Count == 1;
            if (handlesShown)
            {
                var piece = selection.Pieces()[0];
                var mesh = ShapeRegistry.GetPieceMesh(piece);
                var rot = piece.rotation.ToUnity();
                var pos = piece.position.ToUnity();
                for (int h = 0; h < 6; h++)
                {
                    int axis = h / 2;
                    float side = h % 2 == 0 ? 1f : -1f;
                    var local = mesh.bounds.center + AxisDirs[axis] * (side * mesh.bounds.extents[axis]);
                    handlePositions[h] = pos + rot * local;
                    handleDirs[h] = rot * AxisDirs[axis] * side;
                    Place(handles[h], handlePositions[h], rot, scale * 0.07f);
                }
            }
            for (int h = 0; h < 6; h++) handles[h].gameObject.SetActive(handlesShown);
        }

        static void Place(MeshRenderer r, Vector3 position, Quaternion rotation, float size)
        {
            r.gameObject.SetActive(true);
            r.transform.SetPositionAndRotation(position, rotation);
            r.transform.localScale = Vector3.one * size;
        }

        void Paint(Target active)
        {
            for (int i = 0; i < 3; i++)
            {
                SetColor(arrows[i], active.kind == Kind.Move && active.index == i ? HoverColor : AxisColors[i]);
                var ring = AxisColors[i];
                ring.a = 0.6f;
                SetColor(rings[i], active.kind == Kind.Turn && active.index == i ? HoverColor : ring);
            }
            for (int h = 0; h < 6; h++)
                SetColor(handles[h], active.kind == Kind.Resize && active.index == h ? HoverColor : HandleColor);
        }

        void SetColor(MeshRenderer r, Color c)
        {
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        // ---------- picking: what is under the mouse? (measured on screen, in pixels) ----------

        Target Pick(Vector2 mouse)
        {
            var best = new Target();
            float bestDistance = PickPixels;

            void Consider(Kind kind, int index, float distance)
            {
                if (distance < bestDistance) { bestDistance = distance; best = new Target { kind = kind, index = index }; }
            }

            if (handlesShown)
                for (int h = 0; h < 6; h++)
                    if (OnScreen(handlePositions[h], out var hp))
                        Consider(Kind.Resize, h, Vector2.Distance(mouse, hp) - 4f);   // handles win small ties

            for (int i = 0; i < 3; i++)
            {
                if (OnScreen(center, out var a) && OnScreen(center + AxisDirs[i] * scale, out var b))
                    Consider(Kind.Move, i, GizmoMath.DistanceToSegment(mouse, a, b));

                // Ring: check the mouse against short pieces of the circle.
                var turn = Quaternion.FromToRotation(Vector3.up, AxisDirs[i]);
                Vector2? prev = null;
                for (int s = 0; s <= 48; s++)
                {
                    float t = s * Mathf.PI * 2f / 48f;
                    var p = center + turn * new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t)) * (scale * 0.75f);
                    if (!OnScreen(p, out var sp)) { prev = null; continue; }
                    if (prev.HasValue) Consider(Kind.Turn, i, GizmoMath.DistanceToSegment(mouse, prev.Value, sp) + 2f);
                    prev = sp;
                }
            }
            return best;
        }

        bool OnScreen(Vector3 world, out Vector2 screen)
        {
            var s = viewCamera.WorldToScreenPoint(world);
            screen = s;
            return s.z > 0f;
        }

        // ---------- dragging ----------

        void BeginDrag(Target target)
        {
            dragging = target;
            startPieces = selection.Pieces().ConvertAll(p => p.Clone());
            lastDelta = float.NaN;
            var ray = viewCamera.ScreenPointToRay(BuilderInput.PointerPosition);

            if (target.kind == Kind.Resize)
            {
                dragOrigin = handlePositions[target.index];
                dragAxis = handleDirs[target.index];
            }
            else
            {
                dragOrigin = center;
                dragAxis = AxisDirs[target.index];
            }

            if (target.kind == Kind.Turn)
            {
                turnReference = GizmoMath.Perpendicular(dragAxis);
                dragStart = GizmoMath.PlaneAngle(ray, dragOrigin, dragAxis, turnReference) ?? 0f;
            }
            else
            {
                dragStart = GizmoMath.AxisParam(ray, dragOrigin, dragAxis) ?? 0f;
            }
        }

        void UpdateDrag()
        {
            var ray = viewCamera.ScreenPointToRay(BuilderInput.PointerPosition);
            var editor = session.Document.editor;
            bool stepped = editor.snapMode != SnapMode.Free;

            float delta;
            if (dragging.kind == Kind.Turn)
            {
                var angle = GizmoMath.PlaneAngle(ray, dragOrigin, dragAxis, turnReference);
                if (angle == null) return;
                delta = GizmoMath.Snap(Mathf.DeltaAngle(dragStart, angle.Value), stepped ? TurnStep : 0f);
            }
            else
            {
                var t = GizmoMath.AxisParam(ray, dragOrigin, dragAxis);
                if (t == null) return;
                delta = GizmoMath.Snap(t.Value - dragStart, stepped ? editor.gridSize : 0f);
            }
            if (Mathf.Approximately(delta, lastDelta)) return;   // nothing new to apply
            lastDelta = delta;

            var after = startPieces.ConvertAll(p => p.Clone());
            string label;
            switch (dragging.kind)
            {
                case Kind.Move:
                    foreach (var p in after) p.position = (p.position.ToUnity() + dragAxis * delta).ToDoc();
                    label = "Move";
                    break;
                case Kind.Turn:
                    var turn = Quaternion.AngleAxis(delta, dragAxis);
                    foreach (var p in after)
                    {
                        p.position = (dragOrigin + turn * (p.position.ToUnity() - dragOrigin)).ToDoc();
                        p.rotation = (turn * p.rotation.ToUnity()).ToDoc();
                    }
                    label = "Rotate";
                    break;
                default:
                    Resize(after[0], delta);
                    label = "Resize";
                    break;
            }
            session.Modify(startPieces, after, label, "gizmo:" + dragging.kind + dragging.index);
        }

        /// <summary>Grows/shrinks the piece on the dragged side only: the opposite face stays where it was.</summary>
        void Resize(Piece p, float delta)
        {
            int axis = dragging.index / 2;
            float before = axis == 0 ? p.p.width : axis == 1 ? p.p.height : p.p.length;
            float size = Mathf.Max(MinSize, before + delta);
            if (axis == 0) p.p.width = size;
            else if (axis == 1) p.p.height = size;
            else p.p.length = size;
            p.position = (p.position.ToUnity() + dragAxis * ((size - before) * 0.5f)).ToDoc();
        }

        void EndDrag()
        {
            dragging = default;
            session.History.SealMerge();
        }
    }
}
