using System.Collections.Generic;
using System.Linq;
using System.Text;
using Builder.Core;
using Builder.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The Skeleton tree (right side, shown with the Bones panel), like Unreal's: every bone under its parent, with lines
    /// showing what hangs on what.
    ///   - Click a name: choose that bone (orange in the world; the Bones panel edits it). Ctrl + click: add / remove one,
    ///     Shift + click: a range, "All": every bone. The handles then move / turn / lengthen all chosen bones together.
    ///   - &gt; / v: fold / unfold a bone's children. "Open all" / "Close all".
    ///   - Under a chosen bone: what moves with it (bound groups and pieces), with Sel to select them.
    ///   - Drag a bone onto another bone: it now hangs on that one (drop on "Drop here = top" to make it a root bone).
    ///     The bone itself does not move. One undo step.
    ///   - Search: shows only the bones whose name matches (and the bones above them).
    /// </summary>
    public class SkeletonTree : MonoBehaviour
    {
        const float Indent = 18f;
        const float RowHeight = 26f;
        static readonly Color Line = new Color(0.55f, 0.6f, 0.68f, 0.8f);
        static readonly Color ChosenRow = new Color(1f, 0.62f, 0.15f, 0.25f);

        [SerializeField] BuilderSession session;
        [SerializeField] BonesPanel bonesPanel;
        [SerializeField] Selection selection;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform canvasRoot;

        GameObject panel;
        RectTransform list;
        TMP_InputField search;
        TextMeshProUGUI dragLabel;
        readonly HashSet<string> folded = new HashSet<string>();
        readonly List<string> order = new List<string>();   // the bones in the order the tree shows them (for Shift + click)
        readonly StringBuilder sb = new StringBuilder();
        string builtSignature;

        /// <summary>The bone being dragged onto another one (null = none).</summary>
        public string Dragged { get; private set; }

        BuildContent Content => session.Document.content;

        void Start()
        {
            Build();
            panel.SetActive(false);
        }

        void Update()
        {
            bool show = bonesPanel.IsOpen;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;
            if (Dragged != null)
            {
                dragLabel.rectTransform.position = Builder.Input.BuilderInput.PointerPosition + new Vector2(14f, -8f);
                if (UnityEngine.InputSystem.Mouse.current != null && !UnityEngine.InputSystem.Mouse.current.leftButton.isPressed) EndDrag();
            }
            string signature = Signature();
            if (signature != builtSignature) Rebuild(signature);
        }

        string Signature()
        {
            sb.Clear();
            foreach (var b in Content.bones) sb.Append(b.id).Append(b.name).Append(b.parentId).Append('|');
            foreach (var g in Content.groups) sb.Append(g.id).Append(g.name).Append(g.boneId).Append(';');
            sb.Append(Content.pieces.Count).Append(Content.pieces.Count(p => p.boneId != null));
            sb.Append(bonesPanel.ChosenBone).Append(string.Join(",", bonesPanel.ChosenBones)).Append(search.text);
            foreach (var f in folded) sb.Append(f);
            return sb.ToString();
        }

        // ---------- building ----------

        void Build()
        {
            var box = UiKit.Box(canvasRoot, "Skeleton Tree", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(box.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(400f, 620f));
            panel = box.gameObject;
            var t = box.rectTransform;

            var title = UiKit.Label(t, "Title", theme, 20f, UiKit.Accent, TextAlignmentOptions.Left, "<b>Skeleton tree</b>");
            UiKit.Pin(title.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(200f, 30f));
            var buttons = UiKit.Row(t, "Buttons", 30f);
            UiKit.Pin((RectTransform)buttons.transform, new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(250f, 30f));
            buttons.childAlignment = TextAnchor.MiddleRight;
            UiKit.TextButton(buttons.transform, "All", theme, 52f, () => bonesPanel.ChooseAll(), 14f);
            UiKit.TextButton(buttons.transform, "Open all", theme, 86f, () => { folded.Clear(); builtSignature = null; }, 14f);
            UiKit.TextButton(buttons.transform, "Close all", theme, 90f, () =>
            {
                foreach (var b in Content.bones) if (Content.bones.Any(c => c.parentId == b.id)) folded.Add(b.id);
                builtSignature = null;
            }, 14f);

            var searchRow = UiKit.Row(t, "Search", 32f);
            UiKit.Pin((RectTransform)searchRow.transform, new Vector2(0f, 1f), new Vector2(14f, -46f), new Vector2(372f, 32f));
            var label = UiKit.Label(searchRow.transform, "Label", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Left, "Search");
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;
            search = UiKit.InputBox(searchRow.transform, theme, 300f, true, TMP_InputField.ContentType.Standard, 15f);

            var area = UiKit.Rect(t, "Area");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(8f, 8f);
            area.offsetMax = new Vector2(-8f, -84f);
            list = UiKit.ScrollList(area);
            var layout = list.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.spacing = 0f;

            dragLabel = UiKit.Label(canvasRoot, "Dragged Bone", theme, 16f, UiKit.Text, TextAlignmentOptions.Left);
            dragLabel.rectTransform.pivot = new Vector2(0f, 1f);
            dragLabel.rectTransform.sizeDelta = new Vector2(300f, 24f);
            dragLabel.gameObject.SetActive(false);
        }

        void Rebuild(string signature)
        {
            builtSignature = signature;
            order.Clear();
            foreach (Transform child in list) Destroy(child.gameObject);

            if (Content.bones.Count == 0)
            {
                Note("No bones yet. In the Bones panel pick a Skeleton (Biped, Walker, Quad, Tank) or From groups.");
                return;
            }

            string find = search.text.Trim();
            var visible = new HashSet<string>();
            if (find.Length > 0)
            {
                // Matching bones + every bone above them (so the path to them stays visible).
                foreach (var b in Content.bones.Where(b => b.name.IndexOf(find, System.StringComparison.OrdinalIgnoreCase) >= 0))
                    for (var x = b; x != null && visible.Add(x.id); x = Content.FindBone(x.parentId)) { }
            }

            var roots = Content.bones.Where(b => Content.FindBone(b.parentId) == null).ToList();
            for (int i = 0; i < roots.Count; i++) AddBone(roots[i], new List<bool>(), i == roots.Count - 1, find.Length > 0 ? visible : null);
            int loose = Content.pieces.Count(p => Content.BoneOf(p) == null && !Shapes.ShapeRegistry.IsHelper(p.shape));
            if (loose > 0)
                Note($"<color=#FFB347>{loose} piece(s) move with no bone</color> - use Auto-bind or Bind selected pieces in the Bones panel.");
            DropTop();
        }

        /// <summary>One bone row (and, unless folded, its children). <paramref name="more"/>: for each level above, are there more bones below?</summary>
        void AddBone(Bone bone, List<bool> more, bool last, HashSet<string> only)
        {
            if (only != null && !only.Contains(bone.id)) return;
            var children = Content.bones.Where(c => c.parentId == bone.id && (only == null || only.Contains(c.id))).ToList();
            bool isFolded = folded.Contains(bone.id) && only == null;

            order.Add(bone.id);
            bool chosen = bonesPanel.ChosenBones.Contains(bone.id);
            var row = Row(bone.id == bonesPanel.ChosenBone ? ChosenRow : chosen ? ChosenRow * new Color(1f, 1f, 1f, 0.6f) : Color.clear);
            Lines(row, more, last);
            string id = bone.id;
            if (children.Count > 0)
                Plain(UiKit.TextButton(row, isFolded ? "+" : "-", theme, 22f, () =>
                {
                    if (!folded.Remove(id)) folded.Add(id);
                    builtSignature = null;
                }, 16f));
            else Space(row, 22f);

            var name = Plain(UiKit.TextButton(row, bone.name, theme, 150f, () => Click(id), 15f));
            name.GetComponent<LayoutElement>().flexibleWidth = 1f;
            name.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
            var drag = name.gameObject.AddComponent<TreeDrag>();
            drag.tree = this;
            drag.boneId = id;

            int count = Content.pieces.Count(p => Content.BoneOf(p) == id);
            var c = UiKit.Label(row, "Count", theme, 13f, UiKit.SubtleText, TextAlignmentOptions.Right, count > 0 ? count + " pcs" : "-");
            c.gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;

            // What moves with the chosen bone.
            if (id == bonesPanel.ChosenBone)
            {
                var childMore = new List<bool>(more) { !last };
                foreach (var g in Content.groups.Where(g => g.boneId == id))
                    PartsRow(childMore, $"group {g.name}", Content.pieces.Where(p => p.groupId == g.id && Content.BoneOf(p) == id).Select(p => p.id).ToList());
                var own = Content.pieces.Where(p => p.boneId == id).Select(p => p.id).ToList();
                if (own.Count > 0) PartsRow(childMore, "pieces bound to it", own);
            }

            if (isFolded) return;
            var next = new List<bool>(more) { !last };
            for (int i = 0; i < children.Count; i++) AddBone(children[i], next, i == children.Count - 1, only);
        }

        /// <summary>Click = choose only this bone (again = none). Ctrl + click = add / remove. Shift + click = a range.</summary>
        void Click(string id)
        {
            if (Builder.Input.BuilderInput.CtrlHeld) bonesPanel.ToggleChosen(id);
            else if (Builder.Input.BuilderInput.ShiftHeld) bonesPanel.ChooseRange(id, order);
            else bonesPanel.Choose(bonesPanel.ChosenBone == id && bonesPanel.ChosenBones.Count == 1 ? null : id);
        }

        void PartsRow(List<bool> more, string text, List<string> pieces)
        {
            var row = Row(new Color(1f, 1f, 1f, 0.04f));
            Lines(row, more, true, faint: true);
            Space(row, 22f);
            var l = UiKit.Label(row, "Parts", theme, 13f, UiKit.SubtleText, TextAlignmentOptions.Left, $"<i>{text}: {pieces.Count}</i>");
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UiKit.TextButton(row, "Sel", theme, 40f, () => selection.SetMany(pieces), 13f).GetComponent<LayoutElement>().preferredHeight = RowHeight - 4f;
        }

        Transform Row(Color background)
        {
            var row = UiKit.Row(list, "Row", RowHeight);
            row.spacing = 2f;
            row.padding = new RectOffset(0, 4, 0, 0);
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = background;
            bg.raycastTarget = false;
            return row.transform;
        }

        void Space(Transform row, float width) => UiKit.Rect(row, "Space").gameObject.AddComponent<LayoutElement>().preferredWidth = width;

        /// <summary>A button that looks like plain text (a soft light only when the mouse is on it), as tall as the row.</summary>
        Button Plain(Button b)
        {
            var colors = b.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = colors.selectedColor = new Color(1f, 1f, 1f, 0.12f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            b.colors = colors;
            b.GetComponent<LayoutElement>().preferredHeight = RowHeight;
            return b;
        }

        /// <summary>The tree lines in front of a row: │ for levels that continue below, ├ or └ for this bone.</summary>
        void Lines(Transform row, List<bool> more, bool last, bool faint = false)
        {
            int levels = more.Count + 1;
            var holder = UiKit.Rect(row, "Lines");
            var size = holder.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = levels * Indent;
            size.preferredHeight = RowHeight;   // the lines run the full row height
            if (more.Count == 0 && !faint) return;   // a top bone: no line in front of it
            var color = faint ? new Color(Line.r, Line.g, Line.b, 0.35f) : Line;
            for (int level = 0; level < more.Count; level++)
                if (more[level]) Bar(holder, color, level * Indent + Indent / 2f, 0f, 1f, 2f, true);
            float x = more.Count * Indent + Indent / 2f;
            Bar(holder, color, x, 0f, last ? 0.5f : 1f, 2f, true);   // │ from the top: to the middle if it is the last one
            Bar(holder, color, x, 0.5f, 0.5f, Indent / 2f, false);   // ─ over to the name
        }

        static void Bar(RectTransform parent, Color color, float x, float from, float to, float size, bool vertical)
        {
            var img = UiKit.Rect(parent, "Bar").gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            if (vertical)
            {
                rt.anchorMin = new Vector2(0f, 1f - to);
                rt.anchorMax = new Vector2(0f, 1f - from);
                rt.offsetMin = new Vector2(x - 1f, 0f);
                rt.offsetMax = new Vector2(x + 1f, 0f);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(size, 2f);
            }
        }

        void Note(string text)
        {
            var n = UiKit.Label(list, "Note", theme, 14f, UiKit.SubtleText, TextAlignmentOptions.Left, text);
            n.textWrappingMode = TextWrappingModes.Normal;
            n.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
        }

        void DropTop()
        {
            var row = Row(new Color(1f, 1f, 1f, 0.03f));
            var l = UiKit.Label(row, "Top", theme, 13f, UiKit.SubtleText, TextAlignmentOptions.Center, "drag a bone name here = top bone (no parent)");
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var target = row.gameObject.AddComponent<TreeDrag>();
            target.tree = this;
            target.boneId = null;
            row.GetComponent<Image>().raycastTarget = true;
        }

        // ---------- drag and drop ----------

        public void BeginDrag(string boneId)
        {
            Dragged = boneId;
            dragLabel.text = "<b>" + Content.FindBone(boneId)?.name + "</b>  <color=#9EA7B3>drop on its new parent</color>";
            dragLabel.gameObject.SetActive(true);
            dragLabel.transform.SetAsLastSibling();
        }

        public void EndDrag()
        {
            Dragged = null;
            dragLabel.gameObject.SetActive(false);
        }

        /// <summary>Hangs the dragged bone on <paramref name="parentId"/> (null = top). Loops are refused.</summary>
        public void DropOn(string parentId)
        {
            var bone = Content.FindBone(Dragged);
            EndDrag();
            if (bone == null || bone.parentId == parentId || bone.id == parentId) return;
            if (parentId != null && Rig.IsInside(parentId, bone.id, Content))
            {
                Toast.Show($"Can't: {Content.FindBone(parentId)?.name} hangs on {bone.name} (that would make a loop)");
                return;
            }
            var c = bone.Clone();
            c.parentId = parentId;
            string parentName = Content.FindBone(parentId)?.name;
            session.ChangeBones(new[] { bone }, new[] { c }, parentName != null ? $"{bone.name} hangs on {parentName}" : $"{bone.name} is a top bone");
            Toast.Show(parentName != null ? $"{bone.name} now hangs on {parentName}" : $"{bone.name} is now a top bone");
        }
    }

    /// <summary>A bone row's name: drag it onto another bone row (drop target) to change its parent.</summary>
    public class TreeDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public SkeletonTree tree;
        public string boneId;

        public void OnBeginDrag(PointerEventData e) { if (boneId != null) tree.BeginDrag(boneId); }
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }   // a drop on a row happens first; otherwise Update ends it
        public void OnDrop(PointerEventData e) { if (tree.Dragged != null) tree.DropOn(boneId); }
    }
}
