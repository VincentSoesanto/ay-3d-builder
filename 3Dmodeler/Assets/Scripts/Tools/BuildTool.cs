using System.Collections.Generic;
using System.Linq;
using Builder.CameraControl;
using Builder.Core;
using Builder.Input;
using Builder.IO;
using Builder.Shapes;
using Builder.UI;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>What the mouse wheel does in the build / erase tool.</summary>
    public enum WheelMode { Rotate, Scale, Item, Distance }

    /// <summary>
    /// Place / delete, Minecraft-creative style. Aims with the crosshair (screen center).
    /// A see-through "ghost" shows where the piece will go; the piece you are aiming at gets a light glow.
    /// Left click / RT places, right click / LT deletes.
    ///
    /// The mouse wheel has three jobs; a key picks which one (it stays until another is picked):
    ///   R  = rotate: the wheel turns the piece 90 degrees around the chosen axis; R again = next axis (Y -> X -> Z).
    ///        A curved arrow on the ghost shows the axis and which way the next turn goes (X red, Y green, Z blue).
    ///   E  = scale: the wheel makes the piece bigger / smaller in friendly steps (x0.5, x1, x1.5, x2 ...).
    ///   middle mouse button = item: the wheel picks the hotbar slot (see Hotbar).
    /// Gamepad: X = rotate / next axis, D-pad left/right = the wheel. In orbit camera mode the wheel zooms instead.
    ///
    /// Placement modes (F / D-pad down cycles):
    ///   GRID: the piece's box edges land on grid lines.
    ///   SNAP: corners / edge middles / face middles snap onto the same points of the aimed piece (Valheim style);
    ///         the ghost turns green when snapped. Hold Q + wheel (or tap Q, gamepad D-pad up) to pick which point
    ///         of the ghost attaches: Auto (nearest pair) or one fixed point, shown by an orange dot.
    ///   FREE: no snapping at all; the piece sits where you aim and may overlap others.
    ///
    /// Paste mode (Ctrl+V / Ctrl+D): the ghost is the whole copied group, placed, turned and scaled like one piece.
    /// Click places a copy (as often as you like); right click / LT stops pasting.
    /// Mirror (M): a second ghost shows the mirror copy that will be placed on the other side of X = 0.
    ///
    /// Erase mode (the Eraser tool, X): same controls, but the ghost is red, goes INTO the aimed surface, and a click
    /// deletes every piece it catches (C: overlapping / only fully inside). Those pieces glow red first.
    /// </summary>
    public class BuildTool : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Camera viewCamera;
        [SerializeField] CameraRig cameraRig;
        [SerializeField] EraseTool eraser;
        [SerializeField] PrecisionMode precision;
        [SerializeField] BlueprintLibrary library;
        [SerializeField] BonePlacer bonePlacer;
        [SerializeField] BoneView boneView;
        [SerializeField] Material ghostMaterial;
        [SerializeField] Material highlightMaterial;
        [SerializeField] float reach = 60f;               // meters

        // ---------- what gets placed ----------
        // The hotbar sets the shape. Each shape remembers its own settings while the app runs.
        public string ShapeId { get; private set; } = "box";
        public ShapeParams Params { get; private set; } = new ShapeParams();
        public string ColorHex { get; private set; } = "#B0B0B0";

        /// <summary>The blueprint the hotbar slot holds (placed like one piece), or null for a plain shape.</summary>
        public Blueprint CurrentBlueprint { get; private set; }

        /// <summary>The name of what gets placed: the shape's or the blueprint's.</summary>
        public string ItemName => CurrentBlueprint?.name ?? ShapeRegistry.Get(ShapeId)?.DisplayName ?? ShapeId;

        /// <summary>True when the hotbar item is a Point (it goes on corners / grid crossings, not on surfaces).</summary>
        public bool PlacingPoint => ShapeId == PointShape.ShapeId && !Pasting && CurrentBlueprint == null && !Erasing;

        /// <summary>True when the hotbar item is the Rigger tube: clicks place bone joints (start, then end).</summary>
        public bool PlacingBone => ShapeId == BoneJointShape.ShapeId && !Pasting && CurrentBlueprint == null && !Erasing;

        BoneJointView aimedJoint;   // a bone joint ball under the crosshair (only aimable while placing bones)

        float ctrlDownAt;
        bool ctrlUsed;

        /// <summary>FREE mode: how far in front of you the item floats when no surface is closer (Ctrl + wheel changes it).</summary>
        public float HoldDistance { get; private set; } = 5f;

        /// <summary>True while placing copies of the clipboard instead of the hotbar shape.</summary>
        public bool Pasting { get; private set; }

        /// <summary>True when this is the Eraser tool: the ghost deletes instead of placing.</summary>
        public bool Erasing { get; private set; }

        /// <summary>How many pieces one click places (1, or the size of the pasted group).</summary>
        public int StampCount => stamp.Count;

        // ---------- the wheel ----------
        public WheelMode Wheel { get; private set; } = WheelMode.Rotate;

        /// <summary>True when the mouse wheel zooms the camera (orbit mode), so it does nothing else.</summary>
        public bool WheelZooms => cameraRig.Mode == CameraMode.Orbit;

        /// <summary>The sizes E + wheel steps through (1 = normal size).</summary>
        public static readonly float[] ScaleSteps = { 0.1f, 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f, 8f, 10f };
        int scaleIndex = 4;   // = 1

        /// <summary>Size of the placed piece (or pasted group) compared to normal.</summary>
        public float Scale => ScaleSteps[scaleIndex];

        // ---------- rotation ----------
        /// <summary>The axes R cycles through, and their names for the HUD.</summary>
        public static readonly string[] AxisNames = { "Y", "X", "Z" };
        static readonly Vector3[] Axes = { Vector3.up, Vector3.right, Vector3.forward };

        /// <summary>Which axis the wheel turns around: 0 = Y, 1 = X, 2 = Z.</summary>
        public int RotateAxis { get; private set; }

        /// <summary>The turn sizes Shift+R cycles through (degrees per wheel notch).</summary>
        public static readonly float[] RotateSteps = { 90f, 45f, 15f, 5f };
        int rotateStepIndex;

        /// <summary>How far one wheel notch turns the piece (degrees).</summary>
        public float RotateStep => RotateSteps[rotateStepIndex];

        /// <summary>
        /// True when the ghost is flipped (mirrored) with J. Then every part is the mirror image (in the stamp's own
        /// space) before <see cref="PieceRotation"/> turns it.
        /// </summary>
        public bool Flipped { get; private set; }

        /// <summary>The piece's rotation (in steps of <see cref="RotateStep"/>), applied before placing.</summary>
        public Quaternion PieceRotation { get; private set; } = Quaternion.identity;

        // ---------- placement mode + attach point ----------
        public SnapMode Mode => session.Document.editor.snapMode;

        /// <summary>True while the ghost is snapped to a point of another piece (SNAP mode).</summary>
        public bool Snapped { get; private set; }

        /// <summary>Placing: how many placed pieces the ghost goes into (they glow red).</summary>
        public int OverlapCount => overlaps.Count;

        /// <summary>Placing: the bone that runs through the ghost (it lights up green), or null.</summary>
        public Bone TouchedBone { get; private set; }

        /// <summary>Placing: the bone nearest to the ghost's middle and how far it is (null when there are no bones).</summary>
        public Bone NearestBone { get; private set; }
        public float NearestBoneDistance { get; private set; }

        /// <summary>FREE: how high the bottom of the ghost floats above the floor (-1 when not shown).</summary>
        public float FloatHeight { get; private set; } = -1f;

        /// <summary>SNAP mode attach point: -1 = Auto (nearest pair), else an index into PlacementMath.AttachPoints.</summary>
        public int AttachIndex { get; private set; } = -1;

        public static string AttachNameOf(int index) =>
            index < 0 ? "Auto" : PlacementMath.AttachPointName(PlacementMath.AttachPoints[index]);

        public string AttachName => AttachNameOf(AttachIndex);

        /// <summary>How long Q must be held before the attach point list appears (a quicker tap = next point).</summary>
        const float HoldForList = 0.35f;

        /// <summary>True while the attach point list should be on screen (Q held long enough, or just changed with the gamepad).</summary>
        public bool AttachListVisible => enabled && Mode == SnapMode.Snap && !input.Blocked &&
            ((input.Held(input.SnapPoint) && (scrolledWhileHoldingQ || Time.unscaledTime - qPressedAt >= HoldForList))
             || Time.unscaledTime < showListUntil);

        // ---------- internals ----------
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color GhostNormal = new Color(0.4f, 0.85f, 1f, 0.3f);
        static readonly Color HeightLineColor = new Color(1f, 0.85f, 0.3f, 0.8f);
        static readonly Color GhostSnapped = new Color(0.45f, 1f, 0.55f, 0.35f);
        static readonly Color GhostMirror = new Color(0.4f, 0.85f, 1f, 0.16f);
        static readonly Color EraserColor = new Color(1f, 0.25f, 0.2f, 0.25f);
        static readonly Color EraserMirror = new Color(1f, 0.25f, 0.2f, 0.14f);
        static readonly Color MarkerColor = new Color(1f, 0.6f, 0.15f, 0.95f);

        /// <summary>One piece of the stamp: a template piece (position/rotation relative to the stamp) + its mesh.</summary>
        struct Part
        {
            public Piece template;
            public Mesh mesh;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }

        readonly List<Part> stamp = new List<Part>();
        readonly List<Vector3> stampCorners = new List<Vector3>();
        Bounds stampBounds;
        List<Piece> pastePieces;   // the clipboard copy while pasting (unscaled)

        readonly Dictionary<string, ShapeParams> paramsByShape = new Dictionary<string, ShapeParams>();
        readonly Dictionary<Mesh, Vector3[]> cornerCache = new Dictionary<Mesh, Vector3[]>();
        readonly List<MeshRenderer> ghosts = new List<MeshRenderer>();
        readonly List<MeshRenderer> mirrorGhosts = new List<MeshRenderer>();
        readonly HashSet<string> victims = new HashSet<string>();
        readonly HashSet<string> overlaps = new HashSet<string>();   // placing: placed pieces the ghost goes into
        MeshRenderer heightLine, floorDot;
        MeshFilter highlightFilter, markerFilter;
        MaterialPropertyBlock colorBlock;
        AxisIndicator axisIndicator;

        bool hasTarget;
        Vector3 targetPosition;
        Quaternion targetRotation;
        PieceView aimedPiece;
        bool precisionAnchored;     // precision mode: the ghost's start spot has been taken
        Vector3 precisionAnchor;
        bool scrolledWhileHoldingQ;
        float qPressedAt;
        float showListUntil;

        void Awake()
        {
            colorBlock = new MaterialPropertyBlock();
            highlightFilter = MakeOverlay("Aim Highlight", highlightMaterial).GetComponent<MeshFilter>();
            axisIndicator = AxisIndicator.Create(transform, ghostMaterial);

            // Orange dot that shows the chosen attach point.
            var marker = MakeOverlay("Attach Point", ghostMaterial);
            markerFilter = marker.GetComponent<MeshFilter>();
            markerFilter.sharedMesh = ShapeRegistry.GetMesh("sphere",
                new ShapeParams { width = 0.09f, height = 0.09f, length = 0.09f, segments = 8 }, false);
            SetColor(marker, MarkerColor);

            // FREE: a thin line from the floating ghost down to the floor, and a dot where it lands.
            var round = ShapeRegistry.GetMesh("cylinder", new ShapeParams { width = 1f, height = 1f, length = 1f, segments = 12 }, false);
            heightLine = MakeOverlay("Height Line", ghostMaterial);
            heightLine.GetComponent<MeshFilter>().sharedMesh = round;
            SetColor(heightLine, HeightLineColor);
            floorDot = MakeOverlay("Floor Dot", ghostMaterial);
            floorDot.GetComponent<MeshFilter>().sharedMesh = round;
            SetColor(floorDot, HeightLineColor);
        }

        MeshRenderer MakeOverlay(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return r;
        }

        void SetColor(MeshRenderer r, Color c)
        {
            colorBlock.SetColor(ColorId, c);
            r.SetPropertyBlock(colorBlock);
        }

        /// <summary>Switch the shape that gets placed (called by the hotbar). Also stops pasting.</summary>
        public void SetShape(string shapeId)
        {
            pastePieces = null;
            Pasting = false;
            CurrentBlueprint = library != null ? library.FromItem(shapeId) : null;
            if (CurrentBlueprint != null)
            {
                ShapeId = shapeId;   // "bp:<id>"
                return;
            }

            var shape = ShapeRegistry.Get(shapeId) ?? ShapeRegistry.Get("box");
            if (!paramsByShape.TryGetValue(shape.Id, out var p))
            {
                p = shape.CreateDefaults();
                paramsByShape[shape.Id] = p;
            }
            ShapeId = shape.Id;
            Params = p;
            pastePieces = null;
            Pasting = false;
        }

        /// <summary>Starts paste mode with these pieces (positions relative to the group's bottom-middle).</summary>
        public void StartPaste(List<Piece> pieces)
        {
            pastePieces = pieces;
            Pasting = true;
        }

        public void StopPaste()
        {
            pastePieces = null;
            Pasting = false;
        }

        /// <summary>Turns erase mode on (Eraser tool) or off (Build tool).</summary>
        public void SetErasing(bool on)
        {
            Erasing = on;
            if (on) StopPaste();
        }

        static ShapeParams Scaled(ShapeParams p, float s) => LinkedCopies.Scaled(p, s);

        /// <summary>
        /// Makes the stamp from the pasted pieces, or one piece of the current shape, at the current scale
        /// (a pasted group grows from its bottom-middle point).
        /// </summary>
        void BuildStamp()
        {
            var pieces = pastePieces ?? CurrentBlueprint?.content.pieces
                         ?? new List<Piece> { new Piece { shape = ShapeId, p = Params, color = ShapeRegistry.StartColor(ShapeId, ColorHex) } };

            stamp.Clear();
            stampCorners.Clear();
            bool first = true;
            foreach (var original in pieces)
            {
                // Flipped (J): use the mirror image of each part, mirrored across the stamp's own X = 0.
                var p = Flipped ? MirrorEdits.MirrorOf(original, original.id) : original;
                var template = p.Clone();
                template.p = Scaled(p.p, Scale);
                var part = new Part
                {
                    template = template,
                    mesh = ShapeRegistry.GetPieceMesh(template),
                    localPosition = p.position.ToUnity() * Scale,
                    localRotation = p.rotation.ToUnity(),
                };
                stamp.Add(part);
                foreach (var c in CornersOf(part.mesh))
                {
                    var corner = part.localPosition + part.localRotation * c;
                    stampCorners.Add(corner);
                    if (first) { stampBounds = new Bounds(corner, Vector3.zero); first = false; }
                    else stampBounds.Encapsulate(corner);
                }
            }
        }

        void Update()
        {
            if (input.Blocked)
            {
                HideOverlays();
                return;
            }

            if (Erasing && ((input.Pressed(input.EraseCatch) && !BuilderInput.CtrlHeld) || (input.Pressed(input.GridSize) && BuilderInput.FromGamepad(input.GridSize))))
            {
                // C cycles: overlap -> fully inside -> cut -> overlap
                eraser.Mode = (EraseTool.Catch)(((int)eraser.Mode + 1) % 3);
                Toast.Show(eraser.Mode == EraseTool.Catch.Overlap ? "Eraser: delete every piece it overlaps"
                         : eraser.Mode == EraseTool.Catch.Inside ? "Eraser: delete only pieces fully inside"
                         : "Eraser: CUT its shape out of the pieces it overlaps");
            }

            bool pickingAttach = HandleAttachPoint();
            if (!pickingAttach) HandleWheel();

            BuildStamp();
            Aim();
            if (boneView != null) boneView.JointsAimable = PlacingBone;
            if (bonePlacer != null)
            {
                if (!PlacingBone) bonePlacer.Cancel(quiet: true);
                else bonePlacer.Preview(hasTarget ? targetPosition + targetRotation * stampBounds.center : (Vector3?)null);
            }

            if (Erasing)
            {
                if (victims.Count > 0 && input.Pressed(input.Place))
                {
                    if (eraser.Mode == EraseTool.Catch.Cut) CutVictims();
                    else session.Remove(victims.ToList(), victims.Count == 1 ? "Erase 1 piece" : $"Erase {victims.Count} pieces");
                }
            }
            else if (hasTarget && input.Pressed(input.Place)) PlaceStamp();
            // (In precision mode the right mouse button is for looking around, so it doesn't delete.)
            else if (input.Pressed(input.Delete) && !(precision.Active && BuilderInput.FromMouse(input.Delete)))
            {
                if (Pasting) StopPaste();   // right click / LT ends paste mode
                else if (PlacingBone && bonePlacer.Pending) bonePlacer.Cancel();   // ends the bone chain
                else if (PlacingBone && aimedJoint != null)
                {
                    // Right click on a bone (tube or joint ball) deletes it.
                    var bone = session.Document.content.FindBone(aimedJoint.BoneId);
                    if (bone != null)
                    {
                        session.DeleteBone(bone.id);
                        Toast.Show($"Deleted bone \"{bone.name}\" (Ctrl+Z brings it back)");
                    }
                }
                else if (aimedPiece != null) session.Remove(new[] { aimedPiece.PieceId }, "Delete");
            }
        }

        void OnDisable()
        {
            if (highlightFilter != null) HideOverlays();   // switched to another tool
            StopPaste();
            if (bonePlacer != null) bonePlacer.Cancel(quiet: true);
            if (boneView != null) boneView.JointsAimable = false;
        }

        void HideOverlays()
        {
            foreach (var g in ghosts) g.gameObject.SetActive(false);
            foreach (var g in mirrorGhosts) g.gameObject.SetActive(false);
            highlightFilter.gameObject.SetActive(false);
            markerFilter.gameObject.SetActive(false);
            axisIndicator.Hide();
            victims.Clear();
            overlaps.Clear();
            if (eraser != null) eraser.HideVictims();
            heightLine.gameObject.SetActive(false);
            floorDot.gameObject.SetActive(false);
            ShowBoneHint(null, null, 0f);
            FloatHeight = -1f;
        }

        // ---------- input ----------

        /// <summary>
        /// SNAP mode only. Hold Q: the wheel scrolls the attach point list (and does nothing else).
        /// Tap Q (without scrolling): next point. Gamepad D-pad up: next point (the list shows for a moment).
        /// Returns true while Q is held, so the wheel is not also used for something else.
        /// </summary>
        bool HandleAttachPoint()
        {
            if (Mode != SnapMode.Snap) return false;

            if (input.Pressed(input.SnapPoint))
            {
                scrolledWhileHoldingQ = false;
                qPressedAt = Time.unscaledTime;
            }

            bool holding = input.Held(input.SnapPoint);
            if (holding)
            {
                // Wheel up = up the list (back), wheel down = down the list (next).
                if (input.Pressed(input.TurnPlus)) { StepAttach(-1); scrolledWhileHoldingQ = true; }
                if (input.Pressed(input.TurnMinus)) { StepAttach(1); scrolledWhileHoldingQ = true; }
            }
            // A quick tap (released before the list appeared, no scrolling) = next point.
            bool quickTap = Time.unscaledTime - qPressedAt < HoldForList;
            if (input.SnapPoint.WasReleasedThisFrame() && !scrolledWhileHoldingQ && quickTap) StepAttach(1);

            if (!Erasing && BuilderInput.FromGamepad(input.GridSize) && input.Pressed(input.GridSize))
            {
                StepAttach(1);
                showListUntil = Time.unscaledTime + 1.5f;
            }
            return holding;
        }

        /// <summary>Moves through Auto + the 26 points, wrapping around at both ends.</summary>
        void StepAttach(int step)
        {
            int count = PlacementMath.AttachPoints.Length + 1;       // +1 for Auto
            int entry = (AttachIndex + 1 + step + count) % count;    // 0 = Auto
            AttachIndex = entry - 1;
        }

        /// <summary>R / E / middle mouse pick what the wheel does; then the wheel (or D-pad left/right) does it.</summary>
        void HandleWheel()
        {
            // Ctrl, like R / E / middle mouse, picks a job for the wheel: how far away the held item floats (FREE mode).
            // A short tap of Ctrl alone switches to it; holding Ctrl while scrolling does it too (and nothing else).
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.ctrlKey.wasPressedThisFrame) { ctrlDownAt = Time.unscaledTime; ctrlUsed = false; }
                else if (keyboard.ctrlKey.isPressed && keyboard.anyKey.wasPressedThisFrame) ctrlUsed = true;   // Ctrl+Z etc.
                if (keyboard.ctrlKey.wasReleasedThisFrame && !ctrlUsed && Time.unscaledTime - ctrlDownAt < 0.4f)
                {
                    Wheel = Wheel == WheelMode.Distance ? WheelMode.Rotate : WheelMode.Distance;
                    if (Wheel == WheelMode.Distance) Toast.Show($"Wheel = how far the item floats ({HoldDistance:0.#} m) - FREE mode");
                }
            }
            int dir = input.Pressed(input.TurnPlus) ? 1 : input.Pressed(input.TurnMinus) ? -1 : 0;
            if (BuilderInput.CtrlHeld || (Wheel == WheelMode.Distance && dir != 0))
            {
                if (dir != 0)
                {
                    ctrlUsed = true;
                    HoldDistance = Mathf.Clamp(HoldDistance * (dir > 0 ? 1.15f : 1f / 1.15f), 1f, 40f);
                    if (Mode != SnapMode.Free)
                    {
                        session.Document.editor.snapMode = SnapMode.Free;   // the distance only matters in FREE mode
                        Toast.Show($"FREE mode: the item floats {HoldDistance:0.#} m away");
                    }
                    else Toast.Show($"The item floats {HoldDistance:0.#} m away");
                }
                if (BuilderInput.CtrlHeld) return;   // Ctrl + wheel never turns / resizes / changes the item
            }
            if (input.Pressed(input.RotateStep))
            {
                rotateStepIndex = (rotateStepIndex + 1) % RotateSteps.Length;
                Wheel = WheelMode.Rotate;
                Toast.Show($"Turn step {RotateStep:0} degrees");
            }
            else if (input.Pressed(input.RotateAxis) && !BuilderInput.ShiftHeld)   // (Shift+R = step size)
            {
                if (Wheel == WheelMode.Rotate) RotateAxis = (RotateAxis + 1) % Axes.Length;
                else Wheel = WheelMode.Rotate;
            }
            if (input.Pressed(input.Flip) && Wheel == WheelMode.Rotate) Flip();
            if (input.Pressed(input.ScaleMode) && !BuilderInput.CtrlHeld) Wheel = WheelMode.Scale;   // not Ctrl+E (export)
            if (input.Pressed(input.ItemMode)) Wheel = WheelMode.Item;

            // The mouse wheel is busy when it zooms the orbit camera or (precision mode) moves the piece up/down.
            bool wheelBusy = WheelZooms || precision.Active;
            int step = 0;
            if (input.Pressed(input.TurnPlus) && !(wheelBusy && BuilderInput.FromMouse(input.TurnPlus))) step = 1;
            if (input.Pressed(input.TurnMinus) && !(wheelBusy && BuilderInput.FromMouse(input.TurnMinus))) step = -1;
            if (step == 0) return;

            switch (Wheel)
            {
                case WheelMode.Distance: break;   // handled above
                case WheelMode.Rotate: Turn(step); break;
                case WheelMode.Scale: scaleIndex = Mathf.Clamp(scaleIndex + step, 0, ScaleSteps.Length - 1); break;
                // WheelMode.Item: the Hotbar changes the slot.
            }
        }

        /// <summary>
        /// J: mirrors the ghost in place. The active rotation axis picks the direction: Y = left/right (across X),
        /// X = up/down (across Y), Z = front/back (across Z).
        /// </summary>
        void Flip()
        {
            Vector3 across = RotateAxis == 0 ? Vector3.right : RotateAxis == 1 ? Vector3.up : Vector3.forward;
            PieceRotation = FlipRotation(PieceRotation, across);
            Flipped = !Flipped;
            Toast.Show(RotateAxis == 0 ? "Flipped left / right" : RotateAxis == 1 ? "Flipped up / down" : "Flipped front / back");
        }

        /// <summary>
        /// The rotation that goes with one more mirror across the world plane facing <paramref name="across"/>.
        /// (Mirror across that plane, times the turn, times a mirror across the piece's own X = a plain turn,
        /// because two mirrors make a turn. The piece's own X-mirror is then toggled: <see cref="Flipped"/>.)
        /// </summary>
        public static Quaternion FlipRotation(Quaternion rotation, Vector3 across)
        {
            var mirrorWorld = Matrix4x4.Scale(Vector3.one - 2f * across);
            var mirrorOwnX = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            return (mirrorWorld * Matrix4x4.Rotate(rotation) * mirrorOwnX).rotation;
        }

        /// <summary>Turns the piece 90 degrees around the chosen world axis (direction = +1 or -1).</summary>
        void Turn(int direction)
        {
            var turned = Quaternion.AngleAxis(RotateStep * direction, Axes[RotateAxis]) * PieceRotation;
            // Round to exact steps so tiny math errors never add up (only safe for 90 degrees; smaller steps keep
            // the exact turned rotation).
            var e = turned.eulerAngles;
            PieceRotation = RotateStep >= 90f
                ? Quaternion.Euler(Mathf.Round(e.x / 90f) * 90f, Mathf.Round(e.y / 90f) * 90f, Mathf.Round(e.z / 90f) * 90f)
                : turned.normalized;
        }

        // ---------- aiming ----------

        void Aim()
        {
            if (precision.Active)
            {
                AimPrecisely();
                return;
            }
            precisionAnchored = false;

            var ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            hasTarget = Physics.Raycast(ray, out var hit, reach);
            aimedPiece = hasTarget ? hit.collider.GetComponent<PieceView>() : null;
            aimedJoint = hasTarget ? hit.collider.GetComponent<BoneJointView>() : null;
            Snapped = false;

            // FREE: the item is held at the crosshair, floating in the air, unless a surface is closer than that.
            if (Mode == SnapMode.Free && (!hasTarget || hit.distance > HoldDistance))
            {
                var spot = ray.GetPoint(HoldDistance);
                hasTarget = true;
                aimedPiece = null;
                aimedJoint = null;
                bool joint = PlacingPoint || PlacingBone;
                targetRotation = joint ? Quaternion.identity : PieceRotation;
                targetPosition = spot - targetRotation * stampBounds.center;
                ShowAimOverlays();
                return;
            }

            if (!hasTarget)
            {
                HideOverlays();
                return;
            }

            // Placing: the ghost sits ON the aimed surface. Erasing: it goes INTO the aimed piece (same math,
            // reversed side) - but on the floor it sits on top, so it stays visible instead of going underground.
            Vector3 side = Erasing && aimedPiece != null ? -hit.normal : hit.normal;
            targetRotation = PieceRotation;
            targetPosition = PlacementMath.SurfacePosition(hit.point, side, stampCorners, stampBounds, targetRotation);

            // FREE eraser: its middle sits right on the aimed spot (floor or piece), so it slides smoothly
            // instead of jumping between "on the floor" and "inside the piece".
            if (Erasing && Mode == SnapMode.Free)
                targetPosition = hit.point - targetRotation * stampBounds.center;

            if (Mode == SnapMode.Grid)
                targetPosition = PlacementMath.SnapToGrid(targetPosition, side, stampBounds, targetRotation,
                                                          session.Document.editor.gridSize);
            else if (Mode == SnapMode.Snap && aimedPiece != null)
                SnapToAimedPiece(hit.point);
            // FREE: keep the plain surface position.

            if (PlacingPoint || PlacingBone) AimPoint(hit.point);

            ShowAimOverlays();
        }

        /// <summary>
        /// A point goes exactly on a corner spot: GRID = the nearest grid crossing, SNAP = the aimed piece's nearest
        /// corner / edge middle / face middle, FREE = exactly where you aim.
        /// </summary>
        void AimPoint(Vector3 aimed)
        {
            Snapped = false;
            Vector3 spot = aimed;
            if (Mode == SnapMode.Grid) spot = PlacementMath.NearestGridCrossing(aimed, session.Document.editor.gridSize);
            else if (Mode == SnapMode.Snap && aimedPiece != null)
            {
                var t = aimedPiece.transform;
                spot = PlacementMath.NearestSnapPoint(t.position, t.rotation, aimedPiece.GetComponent<MeshFilter>().sharedMesh.bounds, aimed);
                Snapped = true;
            }
            if (PlacingBone && aimedJoint != null && !aimedJoint.IsTube)
            {
                spot = aimedJoint.transform.position;   // a new bone joins an existing joint exactly
                Snapped = true;
            }
            targetRotation = Quaternion.identity;
            targetPosition = spot - stampBounds.center;
        }

        /// <summary>
        /// Precision mode: the ghost stays where it was when V was pressed (or 4 m ahead if it pointed at nothing)
        /// and the mouse moves it in exact steps (PrecisionMode.Offset). No snapping, overlapping is fine.
        /// </summary>
        void AimPrecisely()
        {
            if (!precisionAnchored)
            {
                var cam = viewCamera.transform;
                precisionAnchor = hasTarget ? targetPosition : cam.position + cam.forward * 4f;
                precisionAnchored = true;
            }
            hasTarget = true;
            aimedPiece = null;
            aimedJoint = null;
            Snapped = false;
            targetRotation = PieceRotation;
            targetPosition = precisionAnchor + precision.Offset;
            ShowAimOverlays();
        }

        /// <summary>Ghosts, rotation arrow, attach dot, aim highlight and (eraser) red glows for the current target.</summary>
        void ShowAimOverlays()
        {
            ShowGhosts();

            // Curved arrow around the active rotation axis (only while the wheel rotates).
            Vector3 middle = targetPosition + targetRotation * stampBounds.center;
            if (Wheel == WheelMode.Rotate)
                axisIndicator.Show(middle, PlacementMath.RotatedExtents(stampBounds.extents, targetRotation) * 2f, RotateAxis,
                                   viewCamera.transform.position);
            else axisIndicator.Hide();

            // Orange dot on the chosen attach point.
            bool showMarker = Mode == SnapMode.Snap && AttachIndex >= 0 && !precision.Active;
            if (showMarker)
            {
                var local = PlacementMath.PointOnBox(stampBounds, PlacementMath.AttachPoints[AttachIndex]);
                markerFilter.transform.position = targetPosition + targetRotation * local;
            }
            markerFilter.gameObject.SetActive(showMarker);

            // Highlight: a slightly bigger copy of the aimed piece, so you can see what right click deletes.
            bool showHighlight = aimedPiece != null && !Pasting && !Erasing;
            if (showHighlight)
            {
                var t = aimedPiece.transform;
                highlightFilter.sharedMesh = aimedPiece.GetComponent<MeshFilter>().sharedMesh;
                highlightFilter.transform.SetPositionAndRotation(t.position, t.rotation);
                highlightFilter.transform.localScale = Vector3.one * 1.01f;
            }
            highlightFilter.gameObject.SetActive(showHighlight);

            if (Erasing) eraser.ShowVictims(victims);
            else eraser.ShowVictims(overlaps);   // placing: red glow on the placed pieces the ghost goes into

            ShowBoneHint();
            ShowHeightLine();
        }

        /// <summary>
        /// Placing pieces near a skeleton: the bone that runs through the ghost lights up green (and the piece will be
        /// bound to it when placed), so you can see when e.g. a sphere really sits on the Head bone.
        /// </summary>
        void ShowBoneHint()
        {
            var bones = session.Document.content.bones;
            if (bones.Count == 0 || Erasing || PlacingBone || PlacingPoint || Pasting || boneView == null || !boneView.Visible)
            {
                ShowBoneHint(null, null, 0f);
                return;
            }
            var box = stampBounds;
            box.Expand(0.02f);   // touching the ghost's skin counts
            var inverse = Quaternion.Inverse(targetRotation);
            var middle = targetPosition + targetRotation * stampBounds.center;
            Bone touched = null, nearest = null;
            float touchedScore = float.MaxValue, nearestDistance = float.MaxValue;
            foreach (var b in bones)
            {
                float d = RigTools.Distance(b, middle);
                if (d < nearestDistance) { nearestDistance = d; nearest = b; }
                if (d < touchedScore && SegmentInBox(b.head.ToUnity(), b.tail.ToUnity(), box, inverse))
                {
                    touchedScore = d;   // several bones in the ghost: the one closest to its middle
                    touched = b;
                }
            }
            ShowBoneHint(touched, nearest, nearestDistance);
        }

        void ShowBoneHint(Bone touched, Bone nearest, float distance)
        {
            if (touched?.id != TouchedBone?.id && touched != null && !Erasing)
                Toast.Show($"In bone: {touched.name}  (the piece will move with it)");
            TouchedBone = touched;
            NearestBone = nearest;
            NearestBoneDistance = distance;
            if (boneView != null) boneView.Touched = touched?.id;
        }

        /// <summary>Does the line from a to b pass through the box (box in the ghost's own space)?</summary>
        bool SegmentInBox(Vector3 a, Vector3 b, Bounds box, Quaternion inverse)
        {
            const int Steps = 24;
            for (int i = 0; i <= Steps; i++)
            {
                var world = Vector3.Lerp(a, b, i / (float)Steps);
                if (box.Contains(inverse * (world - targetPosition))) return true;
            }
            return false;
        }

        /// <summary>FREE: a thin line from the bottom of the floating ghost straight down to the floor.</summary>
        void ShowHeightLine()
        {
            FloatHeight = -1f;
            bool show = Mode == SnapMode.Free && !precision.Active && stampCorners.Count > 0;
            Vector3 bottom = Vector3.zero;
            if (show)
            {
                float low = float.MaxValue;
                foreach (var c in stampCorners) low = Mathf.Min(low, (targetPosition + targetRotation * c).y);
                bottom = targetPosition + targetRotation * stampBounds.center;
                bottom.y = low;
                show = low > 0.02f;
                if (show) FloatHeight = low;
            }
            heightLine.gameObject.SetActive(show);
            floorDot.gameObject.SetActive(show);
            if (!show) return;
            heightLine.transform.SetPositionAndRotation(new Vector3(bottom.x, bottom.y * 0.5f, bottom.z), Quaternion.identity);
            heightLine.transform.localScale = new Vector3(0.012f, bottom.y, 0.012f);
            floorDot.transform.SetPositionAndRotation(new Vector3(bottom.x, 0.003f, bottom.z), Quaternion.identity);
            floorDot.transform.localScale = new Vector3(0.14f, 0.004f, 0.14f);
        }

        /// <summary>
        /// One ghost per stamp part, plus mirror ghosts when mirror building is on.
        /// In erase mode it also collects the pieces each (mirror) ghost catches.
        /// </summary>
        void ShowGhosts()
        {
            bool mirror = session.Document.editor.mirrorX;
            victims.Clear();
            overlaps.Clear();
            if (Erasing) eraser.ClearFindings();
            bool findOverlaps = !Erasing && !PlacingBone && !PlacingPoint;
            for (int i = 0; i < stamp.Count; i++)
            {
                var part = stamp[i];
                var ghost = Pooled(ghosts, i, "Ghost");
                Vector3 pos = targetPosition + targetRotation * part.localPosition;
                Quaternion rot = targetRotation * part.localRotation;
                ghost.GetComponent<MeshFilter>().sharedMesh = part.mesh;
                ghost.transform.SetPositionAndRotation(pos, rot);
                SetColor(ghost, Erasing ? EraserColor : Snapped ? GhostSnapped : GhostNormal);
                ghost.gameObject.SetActive(true);
                if (findOverlaps) eraser.CollectOverlaps(part.mesh, pos, rot, overlaps);
                if (Erasing)
                    eraser.Collect(part.mesh, pos, rot, victims, new EraseTool.Cutter
                    {
                        shape = part.template.shape, p = part.template.p, mirrored = part.template.mirrored,
                        position = pos, rotation = rot,
                    });

                var mirrorGhost = Pooled(mirrorGhosts, i, "Mirror Ghost");
                bool showMirror = mirror && Mathf.Abs(pos.x) > 1e-4f;
                if (showMirror)
                {
                    var m = part.template;
                    var mirrorMesh = ShapeRegistry.GetPieceMesh(m, !m.mirrored);
                    var mirrorPos = MirrorEdits.Position(pos.ToDoc()).ToUnity();
                    var mirrorRot = MirrorEdits.Rotation(rot.ToDoc()).ToUnity();
                    mirrorGhost.GetComponent<MeshFilter>().sharedMesh = mirrorMesh;
                    mirrorGhost.transform.SetPositionAndRotation(mirrorPos, mirrorRot);
                    SetColor(mirrorGhost, Erasing ? EraserMirror : GhostMirror);
                    if (findOverlaps) eraser.CollectOverlaps(mirrorMesh, mirrorPos, mirrorRot, overlaps);
                    if (Erasing)
                        eraser.Collect(mirrorMesh, mirrorPos, mirrorRot, victims, new EraseTool.Cutter
                        {
                            shape = m.shape, p = m.p, mirrored = !m.mirrored, position = mirrorPos, rotation = mirrorRot,
                        });
                }
                mirrorGhost.gameObject.SetActive(showMirror);
            }
            for (int i = stamp.Count; i < ghosts.Count; i++) ghosts[i].gameObject.SetActive(false);
            for (int i = stamp.Count; i < mirrorGhosts.Count; i++) mirrorGhosts[i].gameObject.SetActive(false);
        }

        MeshRenderer Pooled(List<MeshRenderer> pool, int index, string name)
        {
            while (pool.Count <= index) pool.Add(MakeOverlay(name, ghostMaterial));
            return pool[index];
        }

        /// <summary>SNAP mode: line the stamp up with a snap point of the aimed piece.</summary>
        void SnapToAimedPiece(Vector3 aimPoint)
        {
            var target = aimedPiece.transform;
            var targetMesh = aimedPiece.GetComponent<MeshFilter>().sharedMesh;

            if (AttachIndex >= 0)
            {
                // The chosen point of the ghost goes onto the aimed piece's point nearest the crosshair.
                targetPosition = PlacementMath.AttachAt(targetRotation, stampBounds, PlacementMath.AttachPoints[AttachIndex],
                                                        target.position, target.rotation, targetMesh.bounds, aimPoint);
                Snapped = true;
                return;
            }

            // Auto: the closest pair of points snaps, if close enough
            // (40% of the smaller piece's biggest side, between 5 cm and 75 cm).
            float radius = Mathf.Clamp(0.4f * Mathf.Min(MaxSide(stampBounds), MaxSide(targetMesh.bounds)), 0.05f, 0.75f);
            var snapped = PlacementMath.SnapToPoints(targetPosition, targetRotation, stampBounds,
                                                     target.position, target.rotation, targetMesh.bounds, radius);
            if (snapped.HasValue)
            {
                targetPosition = snapped.Value;
                Snapped = true;
            }
        }

        static float MaxSide(Bounds b) => Mathf.Max(b.size.x, b.size.y, b.size.z);

        /// <summary>The mesh's corner points (each point once), remembered per mesh.</summary>
        Vector3[] CornersOf(Mesh mesh)
        {
            if (!cornerCache.TryGetValue(mesh, out var corners))
            {
                corners = new HashSet<Vector3>(mesh.vertices).ToArray();
                if (cornerCache.Count > 500) cornerCache.Clear();   // don't keep old meshes alive
                cornerCache[mesh] = corners;
            }
            return corners;
        }

        // ---------- editing ----------

        /// <summary>Cut mode: cut the eraser's shape out of every caught piece; pieces fully inside are removed.</summary>
        void CutVictims()
        {
            var cuts = new Dictionary<string, Cut>();
            var remove = new List<string>();
            foreach (var id in victims)
            {
                if (eraser.FullyInside.Contains(id)) { remove.Add(id); continue; }
                var piece = session.Document.content.FindPiece(id);
                if (piece != null && eraser.CutterOf.TryGetValue(id, out var cutter)) cuts[id] = ToCut(piece, cutter);
            }
            int n = cuts.Count + remove.Count;
            session.CutPieces(cuts, remove, n == 1 ? "Cut 1 piece" : $"Cut {n} pieces");
        }

        /// <summary>
        /// The cutter, measured from the piece (its own space before mirroring). For a mirrored piece the cutter is
        /// mirrored into that space too, so the stored cut works the same for the piece and its mirror copy.
        /// </summary>
        public static Cut ToCut(Piece piece, EraseTool.Cutter cutter)
        {
            var inverse = Quaternion.Inverse(piece.rotation.ToUnity());
            Vector3 localPosition = inverse * (cutter.position - piece.position.ToUnity());
            Quaternion localRotation = inverse * cutter.rotation;
            bool mirrored = cutter.mirrored;
            if (piece.mirrored)
            {
                localPosition.x = -localPosition.x;
                localRotation = MirrorEdits.Rotation(localRotation.ToDoc()).ToUnity();
                mirrored = !mirrored;
            }
            return new Cut
            {
                shape = cutter.shape, p = cutter.p.Clone(), mirrored = mirrored,
                position = localPosition.ToDoc(), rotation = localRotation.ToDoc(),
            };
        }

        void PlaceStamp()
        {
            if (PlacingBone)
            {
                bonePlacer.Click(targetPosition + targetRotation * stampBounds.center, aimedJoint);
                return;
            }
            var pieces = new List<Piece>(stamp.Count);
            // A blueprint is placed as one linked copy: saving the blueprint again updates it.
            // (Built-in library parts are placed as plain pieces: they never change.)
            var bp = Pasting ? null : CurrentBlueprint;
            var link = bp == null || library.Get(bp.id) == null ? null : new PieceLink
            {
                instanceId = Ids.New(), blueprintId = bp.id, version = bp.version,
                position = targetPosition.ToDoc(), rotation = targetRotation.ToDoc(), scale = Scale, flipped = Flipped,
            };
            for (int i = 0; i < stamp.Count; i++)
            {
                var part = stamp[i];
                var piece = part.template.Clone();
                piece.id = Ids.New();
                piece.mirrorPartnerId = null;
                piece.frame = false;   // a copy of frame parts is your own part (not refitted / removed with the frame)
                piece.position = (targetPosition + targetRotation * part.localPosition).ToDoc();
                piece.rotation = (targetRotation * part.localRotation).ToDoc();
                if (link != null)
                {
                    piece.link = link.Clone();
                    piece.link.part = i;
                }
                pieces.Add(piece);
            }
            // Pasted linked copies stay linked: their links move with them (each paste is a new copy).
            if (Pasting) LinkedCopies.PlaceLinks(pieces, Flipped, Scale, targetRotation, targetPosition);

            // A blueprint with bones brings new bones along (moved like its pieces), and its pieces stay bound to them.
            List<Bone> bones = null;
            if (bp != null && bp.content.bones.Count > 0)
            {
                var newIds = new Dictionary<string, string>();
                foreach (var b in bp.content.bones) newIds[b.id] = Ids.New();
                bones = bp.content.bones.ConvertAll(b =>
                {
                    var c = Rig.Placed(b, Flipped, Scale, targetRotation, targetPosition);
                    c.id = newIds[b.id];
                    c.parentId = b.parentId != null && newIds.TryGetValue(b.parentId, out var parent) ? parent : null;
                    c.copyId = link?.instanceId;   // a linked copy rebuilds these bones when the blueprint changes
                    c.copyBone = link != null ? b.id : null;
                    return c;
                });
                foreach (var piece in pieces)
                    piece.boneId = piece.boneId != null && newIds.TryGetValue(piece.boneId, out var bone) ? bone : null;
            }
            else
            {
                // The ghost touched a bone (it was lit green): the new pieces move with that bone.
                string touchedBone = Pasting ? null : TouchedBone?.id;
                foreach (var piece in pieces) piece.boneId = touchedBone;
            }
            session.Place(pieces, Pasting ? $"Paste {pieces.Count} pieces" : "Place " + ItemName, bones);
        }
    }
}
