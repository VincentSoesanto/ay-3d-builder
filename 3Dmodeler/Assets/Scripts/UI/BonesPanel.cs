using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Builder.Core;
using Builder.Input;
using Builder.Tools;
using Builder.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The Bones panel (K; it opens the Select tool).
    ///   - Skeleton: Biped / Walker / Quad / Tank puts a ready-made skeleton in (fitted to your model's size) and binds
    ///     every piece to its nearest bone; "From groups" makes one bone per group (using the groups' pivots and
    ///     "hangs on" parents). Auto-bind binds pieces to their nearest bone. Smaller / Bigger scales the skeleton.
    ///   - Bones are moved / turned / lengthened with handles in the world (see <see cref="BoneGizmo"/>). Bones = rigger tubes, placed with the Rigger tube item.
    ///   - The bones are listed in the Skeleton tree (right side, <see cref="SkeletonTree"/>): click one to choose it.
    ///   - The chosen bone: rename it (type, or "Preset" for the next standard name), Bind / Unbind the selected
    ///     pieces, bind whole groups (click a group's button), and rotation limits per axis.
    ///   - Pose test: turn the chosen bone with the X / Y / Z sliders (inside its limits) to see how the model moves
    ///     and what bumps into what. Only the picture moves; Reset pose = straight again. Leaving it = rest pose.
    ///   - Hide / Show bones.
    /// </summary>
    public class BonesPanel : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] ToolManager tools;
        [SerializeField] Selection selection;
        [SerializeField] BoneView boneView;
        [SerializeField] PoseTest pose;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform canvasRoot;

        static readonly Color ActiveRow = new Color(1f, 0.62f, 0.15f, 0.2f);

        GameObject panel, editor, posePart, limitPart;
        RectTransform rows, groupButtons;
        TextMeshProUGUI empty, chosenTitle;
        TMP_InputField nameBox;
        TMP_InputField[] limitBoxes;
        Slider[] sliders;
        TextMeshProUGUI[] sliderValues;
        Button poseButton, showButton, limitButton;
        bool open, wasShown;
        TMP_InputField heightBox;
        string chosenId, builtSignature;               // chosenId = the main chosen bone (the last one clicked)
        readonly List<string> chosenAll = new List<string>();   // every chosen bone (Ctrl / Shift + click adds more)
        readonly StringBuilder sb = new StringBuilder();

        public bool IsOpen => open && panel != null && panel.activeSelf;
        public string ChosenBone => chosenId;

        /// <summary>All chosen bones (the main one last). The handles move / turn / lengthen them together.</summary>
        public IReadOnlyList<string> ChosenBones => chosenAll;

        BuildContent Content => session.Document.content;

        void Start()
        {
            Build();
            panel.SetActive(false);
        }

        void Update()
        {
            if (input.Pressed(input.BonesPanel))
            {
                open = !open || tools.Current != ToolKind.Select;
                if (open) tools.SetTool(ToolKind.Select);
            }
            bool show = open && tools.Current == ToolKind.Select;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (show || wasShown) boneView.JointsAimable = show;   // joints can be clicked / dragged while the panel is open
            wasShown = show;
            if (!show && pose.Active) SetPose(false);   // leaving the panel = back to the rest pose

            if (chosenAll.RemoveAll(id => Content.FindBone(id) == null) > 0 || Content.FindBone(chosenId) == null)
                chosenId = chosenAll.Count > 0 ? chosenAll[chosenAll.Count - 1] : null;
            boneView.SetHighlighted(show ? chosenAll : null);
            if (!show) return;

            string signature = Signature();
            if (signature != builtSignature) Rebuild(signature);
            if (heightBox != null && !heightBox.isFocused)
                heightBox.SetTextWithoutNotify(Content.bones.Count > 0 ? SkeletonHeight().ToString("0.##", CultureInfo.InvariantCulture) : "");
        }

        /// <summary>Opens the panel (and the Select tool).</summary>
        public void Open()
        {
            open = true;
            tools.SetTool(ToolKind.Select);
        }

        /// <summary>Ctrl + click: adds the bone to the chosen ones, or takes it out again.</summary>
        public void ToggleChosen(string boneId)
        {
            if (boneId == null) return;
            if (!chosenAll.Remove(boneId)) chosenAll.Add(boneId);
            chosenId = chosenAll.Contains(boneId) ? boneId : chosenAll.Count > 0 ? chosenAll[chosenAll.Count - 1] : null;
            builtSignature = null;
        }

        /// <summary>Shift + click: chooses every bone from the main chosen one to this one, in the given (tree) order.</summary>
        public void ChooseRange(string boneId, IList<string> order)
        {
            int a = chosenId != null ? order.IndexOf(chosenId) : -1;
            int b = order.IndexOf(boneId);
            if (a < 0 || b < 0) { Choose(boneId); return; }
            for (int i = Mathf.Min(a, b); i <= Mathf.Max(a, b); i++)
                if (!chosenAll.Contains(order[i])) chosenAll.Add(order[i]);
            chosenAll.Remove(boneId);
            chosenAll.Add(boneId);
            chosenId = boneId;
            builtSignature = null;
        }

        /// <summary>Chooses every bone (the handles then work on the whole skeleton).</summary>
        public void ChooseAll()
        {
            chosenAll.Clear();
            foreach (var b in Rig.ParentsFirst(Content)) chosenAll.Add(b.id);
            chosenId = chosenAll.Count > 0 ? chosenAll[0] : null;
            builtSignature = null;
        }

        public void Choose(string boneId)
        {
            chosenAll.Clear();
            if (boneId != null) chosenAll.Add(boneId);
            chosenId = boneId;
            builtSignature = null;
        }

        // ---------- building ----------

        void Build()
        {
            var box = UiKit.Box(canvasRoot, "Bones Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(box.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(560f, 0f));
            box.rectTransform.pivot = new Vector2(0f, 0f);
            var layout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel = box.gameObject;
            var t = panel.transform;

            var header = UiKit.Row(t, "Header", 36f);
            var title = UiKit.Label(header.transform, "Title", theme, 20f, UiKit.Accent, TextAlignmentOptions.Left, "<b>Bones</b>  <size=70%>(K)</size>");
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            poseButton = UiKit.TextButton(header.transform, "Pose test", theme, 120f, () => SetPose(!pose.Active));
            showButton = UiKit.TextButton(header.transform, "Hide bones", theme, 120f, () => boneView.Visible = !boneView.Visible);
            UiKit.TextButton(header.transform, "Close", theme, 70f, () => open = false);

            var skeletonRow = UiKit.Row(t, "Skeleton", 34f);
            Small(skeletonRow.transform, "Skeleton", 70f);
            foreach (var name in RigTools.Templates)
            {
                string n = name;
                UiKit.TextButton(skeletonRow.transform, n, theme, 72f, () => ApplyTemplate(n), 15f);
            }
            UiKit.TextButton(skeletonRow.transform, "From groups", theme, 108f, FromGroups, 15f);

            var toolsRow = UiKit.Row(t, "Rig Tools", 34f);
            UiKit.TextButton(toolsRow.transform, "Auto-bind all", theme, 120f, () => AutoBind(false), 15f);
            UiKit.TextButton(toolsRow.transform, "Bind loose only", theme, 130f, () => AutoBind(true), 15f);

            // Inner frame: pick a preset in the list, then add it (pressing again fits it to the bones again).
            var frameRow = UiKit.Row(t, "Frame", 34f);
            Small(frameRow.transform, "Frame", 56f);
            var pick = UiKit.TextButton(frameRow.transform, "", theme, 200f, () => frameList.SetActive(!frameList.activeSelf), 15f);
            frameLabel = pick.GetComponentInChildren<TextMeshProUGUI>();
            UiKit.TextButton(frameRow.transform, "+ Add frame", theme, 110f, AddFrame, 15f);
            UiKit.TextButton(frameRow.transform, "Remove", theme, 80f, RemoveFrame, 15f);
            frameList = UiKit.Rect(t, "Frame Presets").gameObject;
            var listLayout = frameList.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 2f;
            listLayout.childControlWidth = listLayout.childControlHeight = true;
            listLayout.childForceExpandWidth = true;
            foreach (var preset in MechFrame.Presets)
            {
                string name = preset;
                var row = UiKit.Row(frameList.transform, name, 30f);
                UiKit.TextButton(row.transform, name, theme, 190f, () => PickFrame(name), 15f);
                Small(row.transform, MechFrame.About(name), 330f);
            }
            frameList.SetActive(false);
            ShowFramePreset();

            // Size of the whole skeleton: 10 % steps, or type the height in meters.
            var sizeRow = UiKit.Row(t, "Skeleton Size", 34f);
            Small(sizeRow.transform, "Size", 40f);
            UiKit.TextButton(sizeRow.transform, "Smaller", theme, 80f, () => ScaleSkeleton(1f / 1.1f), 15f);
            UiKit.TextButton(sizeRow.transform, "Bigger", theme, 80f, () => ScaleSkeleton(1.1f), 15f);
            Small(sizeRow.transform, "Height", 56f);
            heightBox = UiKit.InputBox(sizeRow.transform, theme, 70f, false, TMP_InputField.ContentType.DecimalNumber, 15f);
            heightBox.onEndEdit.AddListener(SetHeight);
            Small(sizeRow.transform, "m", 20f);

            empty = UiKit.Label(t, "Empty", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Left,
                "No bones yet: pick a <b>Skeleton</b> above (it fits your model and binds the pieces), or place " +
                "<b>Rigger tubes</b> by hand (Tab > Basic > Bones). Then drag the joint balls to fit.");
            empty.textWrappingMode = TextWrappingModes.Normal;
            var dragHint = UiKit.Label(t, "Drag Hint", theme, 14f, UiKit.SubtleText, TextAlignmentOptions.Left,
                "Click a <b>joint ball</b> (arrows move it) or a <b>tube</b> (arrows move, rings turn, cube = length, " +
                "<b>E</b> + wheel = size); Ctrl / Shift + click = more bones; " +
                "Pose test: click a bone, turn it with the rings. " +
                "Bones only move the parts in the Pose test (and in your game): fit them first, then Auto-bind.");
            dragHint.textWrappingMode = TextWrappingModes.Normal;

            var rowsBox = UiKit.Rect(t, "Rows");
            var rowsLayout = rowsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 2f;
            rowsLayout.childControlWidth = rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rows = rowsBox;

            // ---- the chosen bone ----
            var ed = UiKit.Rect(t, "Chosen Bone");
            var edLayout = ed.gameObject.AddComponent<VerticalLayoutGroup>();
            edLayout.spacing = 6f;
            edLayout.padding = new RectOffset(0, 0, 8, 0);
            edLayout.childControlWidth = edLayout.childControlHeight = true;
            edLayout.childForceExpandWidth = true;
            edLayout.childForceExpandHeight = false;
            editor = ed.gameObject;

            chosenTitle = UiKit.Label(ed, "Chosen", theme, 17f, UiKit.Text, TextAlignmentOptions.Left);
            chosenTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            var nameRow = UiKit.Row(ed, "Name", 34f);
            Small(nameRow.transform, "Name", 52f);
            nameBox = UiKit.InputBox(nameRow.transform, theme, 200f, true, TMP_InputField.ContentType.Standard);
            nameBox.onEndEdit.AddListener(Rename);
            UiKit.TextButton(nameRow.transform, "Preset", theme, 80f, NextPreset);
            UiKit.TextButton(nameRow.transform, "Delete", theme, 80f, () =>
            {
                foreach (var id in chosenAll.ToList()) session.DeleteBone(id);   // every chosen bone
            });

            var bindRow = UiKit.Row(ed, "Bind", 34f);
            UiKit.TextButton(bindRow.transform, "Bind selected pieces", theme, 200f, () => Bind(true));
            UiKit.TextButton(bindRow.transform, "Unbind selected", theme, 160f, () => Bind(false));

            Small(ed, "Groups that move with this bone (click to bind / unbind):", 400f);
            groupButtons = UiKit.Rect(ed, "Groups");
            var grid = groupButtons.gameObject.AddComponent<GridLayoutGroup>();   // wraps onto more lines
            grid.cellSize = new Vector2(100f, 30f);
            grid.spacing = new Vector2(6f, 4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;

            var limitRow = UiKit.Row(ed, "Limits", 34f);
            Small(limitRow.transform, "Limits", 52f);
            limitButton = UiKit.TextButton(limitRow.transform, "Off", theme, 60f, ToggleLimits);
            limitPart = UiKit.Row(limitRow.transform, "Values", 34f).gameObject;
            limitPart.AddComponent<LayoutElement>().flexibleWidth = 1f;
            limitBoxes = new TMP_InputField[6];
            for (int axis = 0; axis < 3; axis++)
            {
                Small(limitPart.transform, "XYZ"[axis].ToString(), 14f);
                for (int end = 0; end < 2; end++)
                {
                    int a = axis, e = end;
                    var b = UiKit.InputBox(limitPart.transform, theme, 52f, false, TMP_InputField.ContentType.DecimalNumber);
                    b.onEndEdit.AddListener(text => SetLimit(a, e, text));
                    limitBoxes[axis * 2 + end] = b;
                }
            }

            // ---- pose test sliders ----
            var pp = UiKit.Rect(ed, "Pose");
            var ppLayout = pp.gameObject.AddComponent<VerticalLayoutGroup>();
            ppLayout.spacing = 4f;
            ppLayout.childControlWidth = ppLayout.childControlHeight = true;
            ppLayout.childForceExpandWidth = true;
            ppLayout.childForceExpandHeight = false;
            posePart = pp.gameObject;
            sliders = new Slider[3];
            sliderValues = new TextMeshProUGUI[3];
            for (int axis = 0; axis < 3; axis++)
            {
                var row = UiKit.Row(pp, "Turn " + "XYZ"[axis], 30f);
                Small(row.transform, "Turn " + "XYZ"[axis], 60f);
                int a = axis;
                sliders[axis] = MakeSlider(row.transform, v =>
                {
                    pose.SetAngle(chosenId, a, v);
                    sliderValues[a].text = pose.Angles(chosenId)[a].ToString("0") + "°";
                });
                sliderValues[axis] = Small(row.transform, "0°", 50f);
            }
            var poseButtons = UiKit.Row(pp, "Pose Buttons", 32f);
            UiKit.TextButton(poseButtons.transform, "Reset pose", theme, 120f, () => { pose.ResetPose(); builtSignature = null; });
            var hint = UiKit.Label(poseButtons.transform, "Hint", theme, 14f, UiKit.SubtleText, TextAlignmentOptions.Left,
                "only the picture moves - the model stays as built");
            hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        TextMeshProUGUI Small(Transform parent, string text, float width)
        {
            var l = UiKit.Label(parent, "Label", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Left, text);
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
            return l;
        }

        Slider MakeSlider(Transform parent, UnityEngine.Events.UnityAction<float> changed)
        {
            var res = new DefaultControls.Resources { standard = theme.rounded, background = theme.rounded, knob = theme.rounded };
            var go = DefaultControls.CreateSlider(res);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 200f;
            le.flexibleWidth = 1f;
            le.preferredHeight = 20f;
            go.transform.Find("Background").GetComponent<Image>().color = UiKit.Slot;
            go.transform.Find("Fill Area/Fill").GetComponent<Image>().color = UiKit.Accent;
            go.transform.Find("Handle Slide Area/Handle").GetComponent<Image>().color = Color.white;
            var slider = go.GetComponent<Slider>();
            slider.onValueChanged.AddListener(changed);
            return slider;
        }

        // ---------- the list ----------

        string Signature()
        {
            sb.Clear();
            foreach (var b in Content.bones)
                sb.Append(b.id).Append(b.name).Append(b.parentId).Append(BoundCount(b.id)).Append(b.limits == null ? "-" : Limits(b)).Append('|');
            foreach (var g in Content.groups) sb.Append(g.id).Append(g.name).Append(g.boneId).Append(';');
            sb.Append(chosenId).Append(string.Join(",", chosenAll)).Append(pose.Active).Append(boneView.Visible);
            return sb.ToString();
        }

        static string Limits(Bone b) => $"{b.limits.min.x},{b.limits.max.x},{b.limits.min.y},{b.limits.max.y},{b.limits.min.z},{b.limits.max.z}";

        int BoundCount(string boneId) => Content.pieces.Count(p => Content.BoneOf(p) == boneId);

        void Rebuild(string signature)
        {
            builtSignature = signature;
            foreach (Transform child in rows) Destroy(child.gameObject);
            empty.gameObject.SetActive(Content.bones.Count == 0);

            poseButton.GetComponentInChildren<TextMeshProUGUI>().text = pose.Active ? "Pose test: ON" : "Pose test";
            showButton.GetComponentInChildren<TextMeshProUGUI>().text = boneView.Visible ? "Hide bones" : "Show bones";

            var chosen = Content.FindBone(chosenId);
            editor.SetActive(chosen != null);
            if (chosen != null) ShowChosen(chosen);
        }

        void ShowChosen(Bone b)
        {
            var parent = Content.FindBone(b.parentId);
            chosenTitle.text = $"<b>{b.name}</b>  <color={UiKit.SubtleHex}>{(parent != null ? "child of " + parent.name : "root bone")}, " +
                               $"{Vector3.Distance(b.head.ToUnity(), b.tail.ToUnity()):0.##} m</color>" +
                               (chosenAll.Count > 1 ? $"   <color=#FFB347>+ {chosenAll.Count - 1} more chosen (handles move them all)</color>" : "");
            if (!nameBox.isFocused) nameBox.SetTextWithoutNotify(b.name);

            foreach (Transform child in groupButtons) Destroy(child.gameObject);
            if (Content.groups.Count == 0) Small(groupButtons, "(no groups)", 100f);
            foreach (var g in Content.groups)
            {
                string gid = g.id;
                bool bound = g.boneId == b.id;
                UiKit.TextButton(groupButtons, (bound ? "[x] " : "") + g.name, theme, 92f, () => ToggleGroup(gid), 14f);
            }

            limitButton.GetComponentInChildren<TextMeshProUGUI>().text = b.limits == null ? "Off" : "On";
            limitPart.SetActive(b.limits != null);
            if (b.limits != null)
            {
                float[] values = { b.limits.min.x, b.limits.max.x, b.limits.min.y, b.limits.max.y, b.limits.min.z, b.limits.max.z };
                for (int i = 0; i < 6; i++)
                    if (!limitBoxes[i].isFocused) limitBoxes[i].SetTextWithoutNotify(values[i].ToString("0", CultureInfo.InvariantCulture));
            }

            posePart.SetActive(pose.Active);
            if (pose.Active)
            {
                var angles = pose.Angles(b.id);
                for (int axis = 0; axis < 3; axis++)
                {
                    var (min, max) = Rig.Range(b, axis);
                    sliders[axis].SetValueWithoutNotify(0f);
                    sliders[axis].minValue = min;
                    sliders[axis].maxValue = max;
                    sliders[axis].SetValueWithoutNotify(angles[axis]);
                    sliderValues[axis].text = angles[axis].ToString("0") + "°";
                }
            }
        }

        // ---------- actions ----------

        /// <summary>The frame preset "+ Add frame" uses (remembered while the app runs).</summary>
        public static string FramePreset { get; set; } = MechFrame.Default;

        GameObject frameList;
        TextMeshProUGUI frameLabel;

        void ShowFramePreset() => frameLabel.text = FramePreset + "  v";

        /// <summary>Opens / closes the Frame preset list (the "v" button).</summary>
        public void ShowFrameList(bool show) => frameList.SetActive(show);

        /// <summary>Picks a preset in the Frame list. If a frame is in already, it is swapped for the new one.</summary>
        public void PickFrame(string preset)
        {
            FramePreset = preset;
            ShowFramePreset();
            frameList.SetActive(false);
            if (Content.pieces.Any(p => p.frame)) AddFrame();
            else Toast.Show($"Frame preset: {preset} - click + Add frame");
        }

        /// <summary>Puts the inner frame (the chosen preset) on the bones, or fits it again.</summary>
        void AddFrame()
        {
            if (Content.bones.Count == 0) { Toast.Show("No bones yet - pick a Skeleton first"); return; }
            bool again = Content.pieces.Any(p => p.frame);
            var frame = MechFrame.Build(Content, FramePreset);
            if (frame.Count == 0) { Toast.Show("The part library is missing - no frame parts"); return; }
            session.ReplaceFrame(frame, again ? "Fit frame" : "Add frame");
            Toast.Show(again ? $"{FramePreset} frame fitted to the bones ({frame.Count} pieces)"
                             : $"{FramePreset} frame added ({frame.Count} pieces), each bound to its bone. Now add your armor! (Ctrl+Z takes it away)");
        }

        void RemoveFrame()
        {
            int count = Content.pieces.Count(p => p.frame);
            if (count == 0) { Toast.Show("No frame parts to remove"); return; }
            session.ReplaceFrame(new List<Piece>(), "Remove frame parts");
            Toast.Show($"Removed {count} frame parts (Ctrl+Z brings them back)");
        }

        void ApplyTemplate(string name)
        {
            var box = RigTools.ModelBounds(Content) ?? new Bounds(new Vector3(0f, 3f, 0f), new Vector3(3f, 6f, 3f));
            bool had = Content.bones.Count > 0;
            session.ReplaceSkeleton(RigTools.Fitted(name, box), name + " skeleton", autoBind: true);
            Choose(null);
            int bound = Content.pieces.Count(p => Content.BoneOf(p) != null);
            Toast.Show($"{name} skeleton in, {bound} pieces bound to their nearest bone" +
                       (had ? " (the old bones were replaced - Ctrl+Z brings them back)" : "") + ". Drag the joint balls to fit.");
        }

        void FromGroups()
        {
            if (Content.groups.Count == 0)
            {
                Toast.Show("No groups yet - make groups first (Select tool, H = Groups panel)");
                return;
            }
            var bones = RigTools.FromGroups(Content, out var groupChanges);
            session.ReplaceSkeleton(bones, "Bones from groups", autoBind: true, groupBones: groupChanges);
            Toast.Show($"{bones.Count} bones made from the groups (each group moves with its bone)");
        }

        void AutoBind(bool onlyLoose)
        {
            if (Content.bones.Count == 0) { Toast.Show("No bones yet - pick a Skeleton first"); return; }
            int n = session.AutoBind(onlyLoose);
            Toast.Show(n == 0 ? "Nothing to change" : $"{n} pieces bound to their nearest bone");
        }

        /// <summary>How tall the skeleton is (lowest to highest joint), in meters.</summary>
        float SkeletonHeight()
        {
            if (Content.bones.Count == 0) return 0f;
            var ys = Content.bones.SelectMany(b => new[] { b.head.y, b.tail.y }).ToList();
            return ys.Max() - ys.Min();
        }

        void SetHeight(string text)
        {
            if (!float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var want) || want <= 0.05f) return;
            float now = SkeletonHeight();
            if (now < 1e-4f) return;
            ScaleSkeleton(want / now);
            Toast.Show($"Skeleton is now {want:0.##} m tall");
        }

        void ScaleSkeleton(float factor)
        {
            if (Content.bones.Count == 0) return;
            session.ChangeBones(Content.bones, RigTools.Scaled(Content.bones, factor), factor > 1f ? "Bigger skeleton" : "Smaller skeleton");
        }

        void SetPose(bool on)
        {
            pose.SetActive(on);
            builtSignature = null;
            if (on && chosenId == null && Content.bones.Count > 0) Choose(Rig.ParentsFirst(Content)[0].id);
            if (on) Toast.Show(Content.bones.Count == 0 ? "No bones yet" : "Pose test: choose a bone, then use the Turn sliders");
        }

        void ChangeChosen(System.Action<Bone> change, string label)
        {
            var b = Content.FindBone(chosenId);
            if (b == null) return;
            var c = b.Clone();
            change(c);
            session.ChangeBones(new[] { b }, new[] { c }, label);
        }

        void Rename(string text)
        {
            var b = Content.FindBone(chosenId);
            if (b == null || string.IsNullOrWhiteSpace(text) || text.Trim() == b.name) return;
            string name = text.Trim();
            if (Content.bones.Any(x => x.id != b.id && x.name == name))
            {
                Toast.Show($"Another bone is already called \"{name}\" (names must be different for your game)");
                nameBox.SetTextWithoutNotify(b.name);
                return;
            }
            ChangeChosen(c => c.name = name, $"Rename bone to {name}");
        }

        void NextPreset()
        {
            var b = Content.FindBone(chosenId);
            if (b == null) return;
            var presets = BoneNames.Presets;
            int start = System.Array.IndexOf(presets, b.name);
            for (int step = 1; step <= presets.Length; step++)
            {
                string name = presets[(start + step + presets.Length) % presets.Length];
                if (!Content.bones.Any(x => x.name == name))
                {
                    ChangeChosen(c => c.name = name, $"Rename bone to {name}");
                    return;
                }
            }
            Toast.Show("All preset names are used - type a name instead");
        }

        void Bind(bool bind)
        {
            if (selection.Count == 0)
            {
                Toast.Show("Select pieces first (click, Shift+click or drag a box)");
                return;
            }
            var b = Content.FindBone(chosenId);
            if (b == null) return;
            session.BindPieces(selection.Ids.ToList(), bind ? b.id : null, bind ? $"Bind to {b.name}" : "Unbind");
            Toast.Show(bind ? $"{selection.Count} piece(s) now move with {b.name}" : "Unbound the selected pieces");
        }

        void ToggleGroup(string groupId)
        {
            var g = Content.FindGroup(groupId);
            var b = Content.FindBone(chosenId);
            if (g == null || b == null) return;
            var c = g.Clone();
            c.boneId = g.boneId == b.id ? null : b.id;
            session.ChangeGroup(c, c.boneId != null ? $"Bind group {g.name} to {b.name}" : $"Unbind group {g.name}");
        }

        void ToggleLimits()
        {
            ChangeChosen(c => c.limits = c.limits == null
                ? new AxisLimits { min = new Vec3(-90f, -90f, -90f), max = new Vec3(90f, 90f, 90f) }
                : null, "Bone limits");
        }

        void SetLimit(int axis, int end, string text)
        {
            if (!float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
            v = Mathf.Clamp(v, -180f, 180f);
            ChangeChosen(c =>
            {
                if (c.limits == null) return;
                var limit = end == 0 ? c.limits.min : c.limits.max;
                if (axis == 0) limit.x = v; else if (axis == 1) limit.y = v; else limit.z = v;
                if (end == 0) c.limits.min = limit; else c.limits.max = limit;
            }, "Bone limits");
        }
    }
}
