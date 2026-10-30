using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Input;
using Builder.UI;
using Builder.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Builder.Tools
{
    /// <summary>
    /// Handles for bones, the same kind as for pieces (Select tool, while the Bones panel is open):
    ///   - Click a JOINT ball: arrows (X red, Y green, Z blue) move that joint; joints at the same spot move with it,
    ///     so a chain stays joined.
    ///   - Click a bone's TUBE (or its name in the tree): arrows move the bone with everything that hangs on it, rings turn
    ///     it around its start joint, the white cube at its end makes it longer / shorter.
    ///   - Ctrl / Shift + click: add more bones; the handles then work on all chosen bones together (each one lengthens /
    ///     turns by the same amount). "All" in the skeleton tree = the whole skeleton.
    ///   - Pose test: rings turn the chosen bone (inside its limits); only the picture moves.
    /// GRID / SNAP mode: grid-size steps and 15 degree turns; FREE: no steps. One undo step per drag.
    /// </summary>
    public class BoneGizmo : MonoBehaviour
    {
        public enum Mode { Joint, Bone, Skeleton }

        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] BonesPanel panel;
        [SerializeField] PoseTest pose;
        [SerializeField] Camera viewCamera;
        [SerializeField] Material overlayMaterial;
        [SerializeField] float reach = 100f;

        const float ScreenSize = 0.13f;
        const float PickPixels = 12f;
        const float TurnStep = 15f;
        const float MinLength = 0.02f;

        enum Kind { None, Move, Turn, Length }

        struct Handle { public Kind kind; public int index; }

        static readonly Vector3[] AxisDirs = { Vector3.right, Vector3.up, Vector3.forward };
        static readonly Color[] AxisColors = { new Color(1f, 0.35f, 0.35f, 0.9f), new Color(0.4f, 0.9f, 0.4f, 0.9f), new Color(0.35f, 0.6f, 1f, 0.9f) };
        static readonly Color HoverColor = new Color(1f, 0.88f, 0.2f, 1f);
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly MeshRenderer[] arrows = new MeshRenderer[3];
        readonly MeshRenderer[] rings = new MeshRenderer[3];
        MeshRenderer lengthHandle;
        MaterialPropertyBlock block;
        Transform root;   // the handles live in the world, not under the screen (UI) object this script sits on

        public Mode TargetMode { get; private set; } = Mode.Bone;
        public bool AtTail { get; private set; }
        string boneId;

        // This frame's layout.
        Vector3 center, lengthPos, boneDir;
        Quaternion frame = Quaternion.identity;   // the axes of the handles (pose test: the parent bone's turned axes)
        float scale;
        bool showMove, showTurn, showLength;

        // The drag in progress.
        Handle hovered, dragging;
        List<Bone> start;
        Vector3 dragOrigin, dragAxis, turnReference;
        float dragStart, lastDelta;
        Vector3 startAngles;
        readonly Dictionary<string, Vector3> startAnglesAll = new Dictionary<string, Vector3>();

        /// <summary>The bones the handles work on: all chosen ones (the main one included).</summary>
        List<string> Chosen()
        {
            var list = panel.ChosenBones.Where(id => Content.FindBone(id) != null).ToList();
            if (!list.Contains(boneId)) list.Add(boneId);
            return list;
        }

        public bool Dragging => dragging.kind != Kind.None;

        /// <summary>E switched on: the mouse wheel makes the chosen bones bigger / smaller (10 % per step).</summary>
        public bool WheelScales { get; private set; }
        int tickedFrame = -1;

        /// <summary>True while the Bones panel is open (then the bone handles are used instead of the piece handles).</summary>
        public bool Active => panel != null && panel.IsOpen;
        BuildContent Content => session.Document.content;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            root = new GameObject("Bone Handles").transform;
            for (int i = 0; i < 3; i++)
            {
                arrows[i] = Part("Bone Move " + "XYZ"[i], GizmoMeshes.StraightArrow);
                rings[i] = Part("Bone Turn " + "XYZ"[i], GizmoMeshes.Ring);
            }
            lengthHandle = Part("Bone Length", GizmoMeshes.Cube);
            Hide();
        }

        MeshRenderer Part(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = overlayMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public void Hide()
        {
            if (root == null) return;   // already cleaned up (the scene is closing)
            foreach (var r in arrows) r.gameObject.SetActive(false);
            foreach (var r in rings) r.gameObject.SetActive(false);
            lengthHandle.gameObject.SetActive(false);
            if (Dragging) EndDrag();
        }

        /// <summary>
        /// The mouse went down (Select tool): a click on a bone or joint chooses what the handles work on.
        /// Returns true if it was on a bone (so the click doesn't also select pieces).
        /// </summary>
        public bool TryPick(Vector2 screen)
        {
            if (panel == null || !panel.IsOpen) return false;
            var ray = viewCamera.ScreenPointToRay(screen);
            // Bones are drawn on top of the model, so a bone under the mouse wins even behind a piece. Balls before tubes.
            var hit = Physics.RaycastAll(ray, reach).Select(h => h.collider.GetComponent<BoneJointView>()).Where(j => j != null)
                             .OrderBy(j => j.IsTube ? 1 : 0).FirstOrDefault();
            if (hit == null || Content.FindBone(hit.BoneId) == null) return false;
            if (BuilderInput.CtrlHeld || BuilderInput.ShiftHeld)
            {
                panel.ToggleChosen(hit.BoneId);   // several bones: the handles move them together
                boneId = panel.ChosenBone;
                TargetMode = Mode.Bone;
                return true;
            }
            boneId = hit.BoneId;
            TargetMode = hit.IsTube || pose.Active ? Mode.Bone : Mode.Joint;
            AtTail = hit.IsTail;
            panel.Choose(boneId);
            return true;
        }

        /// <summary>
        /// Every frame from the Select tool. Draws the handles on the chosen bone; returns true while the mouse is on a
        /// handle or dragging one (so the click is for the handle).
        /// </summary>
        public bool Tick(bool mouseActive)
        {
            tickedFrame = Time.frameCount;
            if (panel == null || !panel.IsOpen || Content.FindBone(panel.ChosenBone) == null)
            {
                Hide();
                return false;
            }
            if (panel.ChosenBone != boneId)
            {
                boneId = panel.ChosenBone;   // chosen in the tree: the whole bone
                TargetMode = Mode.Bone;
            }
            if (panel.ChosenBones.Count > 1 && TargetMode == Mode.Joint) TargetMode = Mode.Bone;   // joints: one at a time

            Layout();
            if (!pose.Active && !Dragging) HandleWheelScale();
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

        /// <summary>Not drawn by the Select tool this frame (another tool is on): hide the handles.</summary>
        void LateUpdate()
        {
            if (root != null && tickedFrame != Time.frameCount && (arrows[0].gameObject.activeSelf || rings[0].gameObject.activeSelf || lengthHandle.gameObject.activeSelf))
                Hide();
        }

        /// <summary>E = the wheel scales the chosen bones (around the main bone's start; all bones: around the feet).</summary>
        void HandleWheelScale()
        {
            if (input.Pressed(input.ScaleMode) && !BuilderInput.CtrlHeld)
            {
                WheelScales = !WheelScales;
                Toast.Show(WheelScales ? "Wheel = bone size (10 % per step) - E again = off" : "Wheel bone size off");
            }
            if (!WheelScales) return;
            float factor = input.Pressed(input.TurnPlus) ? 1.1f : input.Pressed(input.TurnMinus) ? 1f / 1.1f : 1f;
            if (factor == 1f) return;
            ScaleChosen(factor);
        }

        /// <summary>The chosen bones (and what hangs on them) get bigger / smaller by <paramref name="factor"/>.</summary>
        public void ScaleChosen(float factor)
        {
            start = Content.bones.ConvertAll(b => b.Clone());
            var group = Group();
            if (group.Count == 0) return;
            List<Bone> after;
            if (group.Count == start.Count) after = RigTools.Scaled(start, factor);   // everything: stays on its feet
            else
            {
                var pivot = start.First(b => b.id == boneId).head.ToUnity();
                after = group.ConvertAll(b =>
                {
                    var c = b.Clone();
                    c.head = (pivot + (b.head.ToUnity() - pivot) * factor).ToDoc();
                    c.tail = (pivot + (b.tail.ToUnity() - pivot) * factor).ToDoc();
                    return c;
                });
            }
            var ids = new HashSet<string>(after.Select(b => b.id));
            session.ChangeBones(Content.bones.Where(b => ids.Contains(b.id)).ToList(), after, factor > 1f ? "Bigger bones" : "Smaller bones", mergeKey: "bonescale");
        }

        // ---------- layout ----------

        void Layout()
        {
            var bone = Content.FindBone(boneId);
            var head = bone.head.ToUnity();
            var tail = bone.tail.ToUnity();
            frame = Quaternion.identity;
            if (pose.Active)
            {
                var m = pose.Matrices();
                var parent = bone.parentId != null && m.TryGetValue(bone.parentId, out var pm) ? pm : Matrix4x4.identity;
                center = (m.TryGetValue(bone.id, out var bm) ? bm : parent).MultiplyPoint3x4(head);
                frame = parent.rotation;
                showMove = false;
                showTurn = true;
                showLength = false;
            }
            else
            {
                switch (TargetMode)
                {
                    case Mode.Joint: center = AtTail ? tail : head; break;
                    case Mode.Skeleton:
                        var root = Content.bones.FirstOrDefault(b => Content.FindBone(b.parentId) == null) ?? bone;
                        center = root.head.ToUnity();
                        break;
                    default: center = head; break;
                }
                showMove = true;
                showTurn = TargetMode != Mode.Joint;
                showLength = TargetMode == Mode.Bone;
            }
            boneDir = (tail - head).sqrMagnitude > 1e-8f ? (tail - head).normalized : Vector3.up;
            lengthPos = tail;

            scale = Vector3.Distance(viewCamera.transform.position, center) * ScreenSize;
            for (int i = 0; i < 3; i++)
            {
                var turn = frame * Quaternion.FromToRotation(Vector3.up, AxisDirs[i]);
                Show(arrows[i], showMove, center, turn, scale);
                Show(rings[i], showTurn, center, turn, scale * 0.75f);
            }
            Show(lengthHandle, showLength, lengthPos, Quaternion.LookRotation(boneDir), scale * 0.09f);
        }

        static void Show(MeshRenderer r, bool on, Vector3 position, Quaternion rotation, float size)
        {
            r.gameObject.SetActive(on);
            if (!on) return;
            r.transform.SetPositionAndRotation(position, rotation);
            r.transform.localScale = Vector3.one * size;
        }

        void Paint(Handle active)
        {
            for (int i = 0; i < 3; i++)
            {
                SetColor(arrows[i], active.kind == Kind.Move && active.index == i ? HoverColor : AxisColors[i]);
                var ring = AxisColors[i];
                ring.a = 0.6f;
                SetColor(rings[i], active.kind == Kind.Turn && active.index == i ? HoverColor : ring);
            }
            SetColor(lengthHandle, active.kind == Kind.Length ? HoverColor : new Color(1f, 1f, 1f, 0.9f));
        }

        void SetColor(MeshRenderer r, Color c)
        {
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        // ---------- picking ----------

        Handle Pick(Vector2 mouse)
        {
            var best = new Handle();
            float bestDistance = PickPixels;
            void Consider(Kind kind, int index, float d)
            {
                if (d < bestDistance) { bestDistance = d; best = new Handle { kind = kind, index = index }; }
            }

            if (showLength && OnScreen(lengthPos, out var lp)) Consider(Kind.Length, 0, Vector2.Distance(mouse, lp) - 4f);
            for (int i = 0; i < 3; i++)
            {
                var axis = frame * AxisDirs[i];
                if (showMove && OnScreen(center, out var a) && OnScreen(center + axis * scale, out var b))
                    Consider(Kind.Move, i, GizmoMath.DistanceToSegment(mouse, a, b));
                if (!showTurn) continue;
                var turn = frame * Quaternion.FromToRotation(Vector3.up, AxisDirs[i]);
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

        void BeginDrag(Handle h)
        {
            dragging = h;
            start = Content.bones.ConvertAll(b => b.Clone());
            startAngles = pose.Angles(boneId);
            startAnglesAll.Clear();
            foreach (var id in Chosen()) startAnglesAll[id] = pose.Angles(id);
            lastDelta = float.NaN;
            dragOrigin = h.kind == Kind.Length ? lengthPos : center;
            dragAxis = h.kind == Kind.Length ? boneDir : frame * AxisDirs[h.index];
            var ray = viewCamera.ScreenPointToRay(BuilderInput.PointerPosition);
            if (h.kind == Kind.Turn)
            {
                turnReference = GizmoMath.Perpendicular(dragAxis);
                dragStart = GizmoMath.PlaneAngle(ray, dragOrigin, dragAxis, turnReference) ?? 0f;
            }
            else dragStart = GizmoMath.AxisParam(ray, dragOrigin, dragAxis) ?? 0f;
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
            if (Mathf.Approximately(delta, lastDelta)) return;
            lastDelta = delta;
            Apply(dragging, delta);
        }

        /// <summary>Does what a handle drag of <paramref name="delta"/> (meters, or degrees for rings) means.</summary>
        void Apply(Handle h, float delta)
        {
            if (pose.Active)
            {
                if (h.kind != Kind.Turn) return;
                // Turn around the ring's axis, inside the parent bone's (already turned) space.
                // Every chosen bone turns by the same amount, each around its own start joint.
                foreach (var pair in startAnglesAll)
                {
                    var q = Quaternion.AngleAxis(delta, AxisDirs[h.index]) * Quaternion.Euler(pair.Value);
                    var e = q.eulerAngles;
                    for (int axis = 0; axis < 3; axis++) pose.SetAngle(pair.Key, axis, Mathf.DeltaAngle(0f, e[axis]));
                }
                return;
            }

            var bone = start.First(b => b.id == boneId);
            List<Bone> after;
            string label;
            switch (h.kind)
            {
                case Kind.Move when TargetMode == Mode.Joint:
                    var joint = (AtTail ? bone.tail : bone.head).ToUnity();
                    after = RigTools.MoveJoint(start, joint, joint + dragAxis * delta);
                    label = "Move joint";
                    break;
                case Kind.Move:
                    after = Moved(Group(), dragAxis * delta);
                    label = TargetMode == Mode.Skeleton ? "Move skeleton" : "Move bone";
                    break;
                case Kind.Turn:
                    after = Turned(Group(), dragOrigin, Quaternion.AngleAxis(delta, dragAxis));
                    label = TargetMode == Mode.Skeleton ? "Turn skeleton" : "Turn bone";
                    break;
                default:   // length: each chosen bone's end (and everything hanging on it) slides along that bone
                    after = Lengthened(delta);
                    label = "Bone length";
                    break;
            }
            if (after.Count == 0) return;
            var ids = new HashSet<string>(after.Select(b => b.id));
            session.ChangeBones(Content.bones.Where(b => ids.Contains(b.id)).ToList(), after, label, mergeKey: "bonegizmo");
        }

        /// <summary>
        /// The bones a Bone / Skeleton handle works on (as they were when the drag began): the chosen bones and
        /// everything that hangs on them.
        /// </summary>
        List<Bone> Group()
        {
            if (TargetMode == Mode.Skeleton) return start;
            var tree = new BuildContent { bones = start };
            var chosen = Chosen();
            return start.Where(b => chosen.Any(id => Rig.IsInside(b.id, id, tree))).ToList();
        }

        /// <summary>Each chosen bone gets <paramref name="delta"/> longer; what hangs on it slides along (parents first).</summary>
        List<Bone> Lengthened(float delta)
        {
            var work = start.ToDictionary(b => b.id, b => b.Clone());
            var tree = new BuildContent { bones = start };
            var chosen = new HashSet<string>(Chosen());
            foreach (var b in Rig.ParentsFirst(tree).Where(b => chosen.Contains(b.id)))
            {
                var bone = work[b.id];
                var dir = bone.tail.ToUnity() - bone.head.ToUnity();
                float length = dir.magnitude;
                dir = length > 1e-6f ? dir / length : Vector3.up;
                float change = Mathf.Max(MinLength, length + delta) - length;
                bone.tail = (bone.tail.ToUnity() + dir * change).ToDoc();
                foreach (var other in work.Values)
                    if (other.id != b.id && Rig.IsInside(other.id, b.id, tree))
                    {
                        other.head = (other.head.ToUnity() + dir * change).ToDoc();
                        other.tail = (other.tail.ToUnity() + dir * change).ToDoc();
                    }
            }
            var touched = Group().Select(b => b.id).ToHashSet();
            return work.Values.Where(b => touched.Contains(b.id)).ToList();
        }

        static List<Bone> Moved(List<Bone> bones, Vector3 by) => bones.ConvertAll(b =>
        {
            var c = b.Clone();
            c.head = (b.head.ToUnity() + by).ToDoc();
            c.tail = (b.tail.ToUnity() + by).ToDoc();
            return c;
        });

        static List<Bone> Turned(List<Bone> bones, Vector3 pivot, Quaternion turn) => bones.ConvertAll(b =>
        {
            var c = b.Clone();
            c.head = (pivot + turn * (b.head.ToUnity() - pivot)).ToDoc();
            c.tail = (pivot + turn * (b.tail.ToUnity() - pivot)).ToDoc();
            return c;
        });

        void EndDrag()
        {
            dragging = default;
            session.History.SealMerge();
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }

        // For tests: what a drag would do, without the mouse.
        public void TestDrag(string bone, Mode mode, bool atTail, string kind, int index, float delta)
        {
            if (!panel.ChosenBones.Contains(bone)) panel.Choose(bone);   // like clicking it
            boneId = bone;
            TargetMode = mode;
            AtTail = atTail;
            Layout();
            var h = new Handle { kind = kind == "move" ? Kind.Move : kind == "turn" ? Kind.Turn : Kind.Length, index = index };
            BeginDrag(h);
            Apply(h, delta);
            EndDrag();
        }
    }
}
