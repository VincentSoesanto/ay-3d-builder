using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Input;
using Builder.IO;
using Builder.Tools;
using Builder.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// "Save as Blueprint" window: name + category (with buttons for the categories already used), then Save.
    /// The blueprint gets the selected pieces, measured from their bottom-middle point (its pivot), and a picture.
    /// The pieces in the project are not changed.
    /// </summary>
    public class BlueprintDialog : MonoBehaviour
    {
        [SerializeField] BuilderInput input;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform canvasRoot;
        [SerializeField] BlueprintLibrary library;
        [SerializeField] ShapeIcons icons;
        [SerializeField] Selection selection;
        [SerializeField] BuilderSession session;

        GameObject screen;
        TMP_InputField nameBox, categoryBox;
        RectTransform categoryButtons;
        TextMeshProUGUI info;
        static string lastCategory = "Parts";

        public bool IsOpen => screen != null && screen.activeSelf;

        void Update()
        {
            if (IsOpen && input.PressedInMenu(input.Cancel)) Close();
        }

        public void Open()
        {
            if (selection.Count == 0)
            {
                Toast.Show("Select pieces first");
                return;
            }
            if (screen == null) Build();

            nameBox.SetTextWithoutNotify(NextName());
            categoryBox.SetTextWithoutNotify(lastCategory);
            info.text = $"{selection.Count} piece{(selection.Count == 1 ? "" : "s")}. Pivot: their bottom middle. " +
                        $"<color={UiKit.SubtleHex}>Find it later in the inventory (Tab). Using an existing name replaces that blueprint.</color>";
            FillCategoryButtons();

            screen.SetActive(true);
            input.SetMenuOpen(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(nameBox.gameObject);
            nameBox.ActivateInputField();
        }

        public void Close()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            screen.SetActive(false);
            input.SetMenuOpen(false);
        }

        string NextName()
        {
            int n = library.All.Count + 1;
            while (library.All.Any(b => b.name == "Part " + n)) n++;
            return "Part " + n;
        }

        void Save()
        {
            var bounds = selection.Bounds;
            if (bounds == null) { Close(); return; }
            string name = string.IsNullOrWhiteSpace(nameBox.text) ? NextName() : nameBox.text.Trim();
            string category = string.IsNullOrWhiteSpace(categoryBox.text) ? "Parts" : categoryBox.text.Trim();

            // Pieces measured from the bottom-middle of the selection (that point becomes the pivot).
            var b = bounds.Value;
            var origin = new Vector3(b.center.x, b.min.y, b.center.z);
            // Same name as an existing blueprint = replace it (keeps its id, so hotbar slots show the new version).
            var existing = library.All.FirstOrDefault(x => string.Equals(x.name, name, System.StringComparison.OrdinalIgnoreCase));
            var bp = new Blueprint
            {
                id = existing?.id ?? Ids.New(), name = name, category = category,
                version = (existing?.version ?? 0) + 1, pivot = Vec3.Zero,
            };
            // Whole linked copies of other blueprints stay linked inside this one (blueprint inside blueprint).
            var saved = selection.Pieces().ToList();
            var content = session.Document.content;
            var copies = LinkedCopies.CopyRelative(saved, content, origin);
            for (int i = 0; i < copies.Count; i++)
            {
                var c = copies[i];
                c.id = Ids.New();
                c.groupId = null;           // groups and mirror links belong to the project, not the blueprint
                c.boneId = content.BoneOf(saved[i]);   // bones the pieces move with come along (below)
                if (c.link?.blueprintId == bp.id) c.link = null;   // a copy of this same blueprint: just its pieces
                bp.content.pieces.Add(c);
            }
            // The bones the saved pieces are bound to (measured from the pivot too); a parent bone that is not
            // among them is left out, so that bone becomes a root bone in the blueprint.
            var boneIds = new HashSet<string>(bp.content.pieces.Where(c => c.boneId != null).Select(c => c.boneId));
            foreach (var bone in content.bones.Where(x => boneIds.Contains(x.id)))
            {
                var c = bone.Clone();
                c.head = (bone.head.ToUnity() - origin).ToDoc();
                c.tail = (bone.tail.ToUnity() - origin).ToDoc();
                if (!boneIds.Contains(c.parentId)) c.parentId = null;
                bp.content.bones.Add(c);
            }
            if (bp.content.pieces.Any(c => c.link != null && LinkedCopies.Uses(library.Get(c.link.blueprintId), bp.id, library.Get)))
            {
                Toast.Show($"Can't save: a blueprint inside it already uses \"{name}\" (a blueprint can't contain itself)");
                return;
            }

            // The saved pieces become a linked copy of the blueprint (one undo step).
            var before = new System.Collections.Generic.List<Piece>();
            var after = new System.Collections.Generic.List<Piece>();
            LinkedCopies.LinkSaved(saved, session.Document.content, bp, origin, before, after);
            session.ModifyExactly(before, after, "Link to blueprint " + name);

            var picture = icons.RenderPieces(bp.content.pieces);
            library.Add(bp, picture != null ? picture.EncodeToPNG() : null);
            if (picture != null) Destroy(picture);
            lastCategory = category;
            Close();
            Toast.Show(existing != null ? $"Replaced blueprint \"{name}\"" : $"Saved blueprint \"{name}\" - it is in the inventory (Tab)");
        }

        void FillCategoryButtons()
        {
            foreach (Transform child in categoryButtons) Destroy(child.gameObject);
            var used = library.All.Select(x => x.category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Take(6).ToList();
            if (used.Count == 0) used.Add("Parts");
            foreach (var c in used)
            {
                string category = c;
                UiKit.TextButton(categoryButtons, category, theme, 110f, () => categoryBox.text = category, 15f);
            }
        }

        void Build()
        {
            var overlay = UiKit.Rect(canvasRoot, "Blueprint Dialog");
            UiKit.Fill(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            screen = overlay.gameObject;

            var panel = UiKit.Box(overlay, "Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 0f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 22, 22);
            layout.spacing = 12f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UiKit.Label(panel.transform, "Title", theme, 28f, UiKit.Text, TextAlignmentOptions.Left, "<b>Save as Blueprint</b>");
            info = UiKit.Label(panel.transform, "Info", theme, 16f, UiKit.Text, TextAlignmentOptions.Left);
            info.textWrappingMode = TextWrappingModes.Normal;

            nameBox = Field(panel.transform, "Name");
            categoryBox = Field(panel.transform, "Category");
            categoryButtons = (RectTransform)UiKit.Row(panel.transform, "Categories", 34f).transform;

            var buttons = UiKit.Row(panel.transform, "Buttons", 46f);
            buttons.childAlignment = TextAnchor.MiddleRight;
            UiKit.TextButton(buttons.transform, "Cancel", theme, 140f, Close, 19f);
            UiKit.TextButton(buttons.transform, "Save", theme, 160f, Save, 19f);

            nameBox.onSubmit.AddListener(_ => Save());
            categoryBox.onSubmit.AddListener(_ => Save());
            screen.SetActive(false);
        }

        TMP_InputField Field(Transform parent, string label)
        {
            var row = UiKit.Row(parent, label, 40f);
            var l = UiKit.Label(row.transform, "Label", theme, 18f, UiKit.SubtleText, TextAlignmentOptions.Left, label);
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
            return UiKit.InputBox(row.transform, theme, 300f, true, TMP_InputField.ContentType.Standard, 19f);
        }
    }
}
