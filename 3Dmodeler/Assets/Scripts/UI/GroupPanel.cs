using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Builder.Core;
using Builder.Input;
using Builder.IO;
using Builder.Tools;
using Builder.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The Groups panel (bottom left, in the Select tool; H shows/hides it). Groups are the model's parts
    /// (Body, Head, LeftArm ...); each becomes one object in the exported .glb.
    ///   - "+ New group": makes a group from the selected pieces.
    ///   - Each row: Hide/Show, name (type to rename), piece count, Sel (select all its pieces),
    ///     +Add (put the selected pieces in it), On: (the group it hangs on, e.g. an arm on the Body - click to change;
    ///     nested like that in the export, and used by "Bones from groups"), Pivot (edit its pivot point),
    ///     X (delete the group, pieces stay).
    ///   - Pivot: the point the part turns around in your game (e.g. the shoulder for an arm). Type X/Y/Z, or use
    ///     Center / Bottom / Top of the group's pieces. The pivot shows as small arrows in the world.
    ///   - "Export .glb": same as Ctrl+E.
    /// </summary>
    public class GroupPanel : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] ToolManager tools;
        [SerializeField] Selection selection;
        [SerializeField] DocumentView documentView;
        [SerializeField] ExportController exporter;
        [SerializeField] UiTheme theme;
        [SerializeField] GameObject panel;
        [SerializeField] RectTransform header;
        [SerializeField] RectTransform rowParent;
        [SerializeField] GameObject pivotArea;
        [SerializeField] TextMeshProUGUI pivotTitle;
        [SerializeField] Camera viewCamera;
        [SerializeField] Material overlayMaterial;
        [SerializeField] BonesPanel bones;   // the Bones panel takes this spot while it is open

        static readonly Color ActiveRow = new Color(0.36f, 0.74f, 1f, 0.18f);
        static readonly int ColorId = Shader.PropertyToID("_Color");

        bool hiddenByUser;
        string builtSignature;
        string activeGroupId;
        TMP_InputField[] pivotBoxes;
        Transform marker;
        readonly StringBuilder sb = new StringBuilder();

        BuildContent Content => session.Document.content;

        void Start()
        {
            UiKit.TextButton(header, "+ New group", theme, 150f, NewGroup);
            UiKit.TextButton(header, "Export .glb", theme, 130f, () => exporter.Export());
            UiKit.TextButton(header, "Export .obj", theme, 130f, () => exporter.ExportObj());
            BuildPivotEditor();
            BuildMarker();
            panel.SetActive(false);
        }

        void Update()
        {
            if (input.Pressed(input.GroupsPanel)) hiddenByUser = !hiddenByUser;
            bool show = tools.Current == ToolKind.Select && !hiddenByUser && (bones == null || !bones.IsOpen);
            if (panel.activeSelf != show) panel.SetActive(show);

            var active = Content.FindGroup(activeGroupId);
            if (active == null) activeGroupId = null;
            marker.gameObject.SetActive(show && active != null);
            if (!show) return;

            string signature = Signature();
            if (signature != builtSignature) Rebuild(signature);
            if (active != null) ShowPivot(active);
        }

        // ---------- the list ----------

        /// <summary>A short text that changes whenever the list needs redrawing.</summary>
        string Signature()
        {
            sb.Clear();
            foreach (var g in Content.groups)
                sb.Append(g.id).Append(g.name).Append(g.visible).Append(g.parentId).Append(Count(g.id)).Append('|');
            sb.Append(Count(null)).Append(activeGroupId);
            return sb.ToString();
        }

        int Count(string groupId) => groupId == null
            ? Content.pieces.Count(p => Content.FindGroup(p.groupId) == null)
            : Content.pieces.Count(p => p.groupId == groupId);

        List<string> Members(string groupId) => groupId == null
            ? Content.pieces.Where(p => Content.FindGroup(p.groupId) == null).Select(p => p.id).ToList()
            : Content.pieces.Where(p => p.groupId == groupId).Select(p => p.id).ToList();

        void Rebuild(string signature)
        {
            builtSignature = signature;
            foreach (Transform child in rowParent) Destroy(child.gameObject);

            foreach (var group in Content.groups) AddGroupRow(group);
            AddUngroupedRow();
        }

        void AddGroupRow(Group group)
        {
            var row = UiKit.Row(rowParent, group.name, 36f);
            var bg = row.gameObject.AddComponent<Image>();
            bg.color = group.id == activeGroupId ? ActiveRow : Color.clear;
            bg.raycastTarget = false;
            string id = group.id;

            UiKit.TextButton(row.transform, group.visible ? "Hide" : "Show", theme, 52f, () =>
            {
                var g = Content.FindGroup(id)?.Clone();
                if (g == null) return;
                g.visible = !g.visible;
                session.ChangeGroup(g, (g.visible ? "Show " : "Hide ") + g.name);
            });

            var name = UiKit.InputBox(row.transform, theme, 120f, true, TMP_InputField.ContentType.Standard);
            name.SetTextWithoutNotify(group.name);
            name.onEndEdit.AddListener(text =>
            {
                var g = Content.FindGroup(id)?.Clone();
                if (g == null || string.IsNullOrWhiteSpace(text) || text.Trim() == g.name) return;
                string old = g.name;
                g.name = text.Trim();
                session.ChangeGroup(g, $"Rename {old} to {g.name}");
            });

            var count = UiKit.Label(row.transform, "Count", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Center, Count(id).ToString());
            count.gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;

            UiKit.TextButton(row.transform, "Sel", theme, 40f, () => selection.SetMany(Members(id)));
            UiKit.TextButton(row.transform, "+Add", theme, 50f, () => AddSelectionTo(id));
            string parentName = Content.FindGroup(group.parentId)?.name;
            UiKit.TextButton(row.transform, parentName != null ? "On: " + parentName : "On: -", theme, 84f, () => NextParent(id), 13f);
            UiKit.TextButton(row.transform, "Pivot", theme, 52f, () => activeGroupId = activeGroupId == id ? null : id);
            UiKit.TextButton(row.transform, "X", theme, 30f, () => session.DeleteGroup(id));
        }

        void AddUngroupedRow()
        {
            var row = UiKit.Row(rowParent, "Ungrouped", 36f);
            var label = UiKit.Label(row.transform, "Name", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Left, "  Ungrouped");
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var count = UiKit.Label(row.transform, "Count", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Center, Count(null).ToString());
            count.gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;
            UiKit.TextButton(row.transform, "Sel", theme, 40f, () => selection.SetMany(Members(null)));
            UiKit.TextButton(row.transform, "+Add", theme, 50f, () => AddSelectionTo(null));
        }

        /// <summary>Picks the next group this one hangs on (none -> each other group -> none), skipping loops.</summary>
        void NextParent(string groupId)
        {
            var g = Content.FindGroup(groupId);
            if (g == null) return;
            var options = new List<string> { null };
            foreach (var other in Content.groups)
                if (other.id != groupId && !HangsOn(other.id, groupId)) options.Add(other.id);
            int at = options.IndexOf(g.parentId);
            var c = g.Clone();
            c.parentId = options[(at + 1) % options.Count];
            var parent = Content.FindGroup(c.parentId);
            session.ChangeGroup(c, parent != null ? $"{g.name} hangs on {parent.name}" : $"{g.name} hangs on nothing");
        }

        /// <summary>True if group <paramref name="id"/> hangs (directly or further up) on <paramref name="ancestor"/>.</summary>
        bool HangsOn(string id, string ancestor)
        {
            for (int i = 0; id != null && i < 64; i++, id = Content.FindGroup(id)?.parentId)
                if (id == ancestor) return true;
            return false;
        }

        void AddSelectionTo(string groupId)
        {
            if (selection.Count == 0)
            {
                Toast.Show("Select pieces first, then press +Add");
                return;
            }
            session.AssignToGroup(selection.Ids.ToList(), groupId);
        }

        void NewGroup()
        {
            if (selection.Count == 0)
            {
                Toast.Show("Select the pieces for the new group first");
                return;
            }
            // Start the pivot at the bottom middle of the selected pieces.
            var b = selection.Bounds ?? new Bounds();
            var pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            var group = session.CreateGroup(session.NextGroupName(), selection.Ids.ToList(), pivot.ToDoc());
            activeGroupId = group.id;
            Toast.Show($"New group {group.name} - type in the name box to rename it");
        }

        // ---------- pivot editor ----------

        void BuildPivotEditor()
        {
            var t = pivotArea.transform;
            var boxes = UiKit.Row(t, "XYZ", 34f);
            pivotBoxes = new TMP_InputField[3];
            for (int axis = 0; axis < 3; axis++)
            {
                var label = UiKit.Label(boxes.transform, "Axis", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Right, "XYZ"[axis].ToString());
                label.gameObject.AddComponent<LayoutElement>().preferredWidth = 16f;
                var box = UiKit.InputBox(boxes.transform, theme, 80f, true, TMP_InputField.ContentType.DecimalNumber);
                int a = axis;
                box.onEndEdit.AddListener(text =>
                {
                    if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        SetPivotAxis(a, v);
                });
                pivotBoxes[axis] = box;
            }

            var buttons = UiKit.Row(t, "Buttons", 34f);
            UiKit.TextButton(buttons.transform, "Center", theme, 80f, () => PivotFromPieces(0f));
            UiKit.TextButton(buttons.transform, "Bottom", theme, 80f, () => PivotFromPieces(-1f));
            UiKit.TextButton(buttons.transform, "Top", theme, 70f, () => PivotFromPieces(1f));
            UiKit.TextButton(buttons.transform, "Done", theme, 70f, () => activeGroupId = null);
        }

        void ShowPivot(Group group)
        {
            pivotArea.SetActive(true);
            pivotTitle.text = $"Pivot of <b>{group.name}</b>  <color={UiKit.SubtleHex}>(the point it turns around)</color>";
            float[] values = { group.pivot.x, group.pivot.y, group.pivot.z };
            for (int i = 0; i < 3; i++)
                if (!pivotBoxes[i].isFocused)
                    pivotBoxes[i].SetTextWithoutNotify(values[i].ToString("0.###", CultureInfo.InvariantCulture));

            // Arrows at the pivot, the same size on screen at any distance.
            marker.position = group.pivot.ToUnity();
            marker.localScale = Vector3.one * Vector3.Distance(viewCamera.transform.position, marker.position) * 0.08f;
        }

        void OnDestroy()
        {
            if (marker != null) Destroy(marker.gameObject);
        }

        void LateUpdate()
        {
            if (Content.FindGroup(activeGroupId) == null || !panel.activeSelf) pivotArea.SetActive(false);
        }

        void SetPivotAxis(int axis, float value)
        {
            var g = Content.FindGroup(activeGroupId)?.Clone();
            if (g == null) return;
            if (axis == 0) g.pivot.x = value; else if (axis == 1) g.pivot.y = value; else g.pivot.z = value;
            session.ChangeGroup(g, "Move pivot of " + g.name);
        }

        /// <summary>Pivot at the middle of the group's pieces: height -1 = bottom, 0 = center, 1 = top.</summary>
        void PivotFromPieces(float height)
        {
            var g = Content.FindGroup(activeGroupId)?.Clone();
            var bounds = g != null ? documentView.GetBounds(Members(g.id)) : null;
            if (bounds == null)
            {
                Toast.Show("This group has no pieces yet");
                return;
            }
            var b = bounds.Value;
            g.pivot = new Vector3(b.center.x, b.center.y + b.extents.y * height, b.center.z).ToDoc();
            session.ChangeGroup(g, "Move pivot of " + g.name);
        }

        /// <summary>Three small arrows (X red, Y green, Z blue), drawn on top of everything.</summary>
        void BuildMarker()
        {
            marker = new GameObject("Pivot Marker").transform;   // in the world, not under the screen (UI) object
            Vector3[] dirs = { Vector3.right, Vector3.up, Vector3.forward };
            Color[] colors = { new Color(1f, 0.35f, 0.35f, 0.95f), new Color(0.4f, 0.9f, 0.4f, 0.95f), new Color(0.35f, 0.6f, 1f, 0.95f) };
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < 3; i++)
            {
                var arrow = new GameObject("Arrow " + "XYZ"[i]);
                arrow.transform.SetParent(marker, false);
                arrow.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dirs[i]);
                arrow.AddComponent<MeshFilter>().sharedMesh = GizmoMeshes.StraightArrow;
                var r = arrow.AddComponent<MeshRenderer>();
                r.sharedMaterial = overlayMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                block.SetColor(ColorId, colors[i]);
                r.SetPropertyBlock(block);
            }
            marker.gameObject.SetActive(false);
        }
    }
}
