using System.Collections.Generic;
using System.Linq;
using Builder.Input;
using Builder.IO;
using Builder.Shapes;
using Builder.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// Creative-style inventory (Tab / gamepad Select), in tabs:
    ///   Basic (shapes, points, bones, reference) | Mech parts | Tank parts | Aircraft parts | Car parts | Weapons | Space | My stuff
    ///   (your imported models and blueprints). The library tabs hold ready-made parts (<see cref="PartLibrary"/>).
    ///   - The search box looks through everything (names and categories).
    ///   - Click a hotbar slot (or LB/RB) to choose which slot to fill.
    ///   - Click an item (gamepad: A) to put it into that slot, or point at one and press 1-9.
    ///   - Right-click one of your blueprints or models: rename, delete, or save it as a file (<see cref="ItemActions"/>).
    /// Tab, Esc or gamepad B closes it. Pictures of library parts are made a few per frame (the first time only).
    /// </summary>
    public class InventoryScreen : MonoBehaviour
    {
        const float Cell = 150f;
        const int IconsPerFrame = 4;
        public static readonly string[] AllTabs = { "Basic", "Mech parts", "Tank parts", "Aircraft parts", "Car parts", "Weapons", "Space", "My stuff" };
        static readonly float[] TabWidths = { 70f, 104f, 100f, 124f, 92f, 90f, 72f, 92f };

        [SerializeField] BuilderInput input;
        [SerializeField] Hotbar hotbar;
        [SerializeField] ShapeIcons icons;
        [SerializeField] BlueprintLibrary library;
        [SerializeField] ModelLibrary models;
        [SerializeField] UiTheme theme;
        [SerializeField] GameObject screen;          // the whole overlay, hidden while closed
        [SerializeField] RectTransform itemArea;     // tabs + search on top, the scrolling list below
        [SerializeField] RectTransform slotRow;      // gets one button per hotbar slot
        [SerializeField] TextMeshProUGUI info;

        readonly List<Button> itemButtons = new List<Button>();
        readonly List<Button> tabButtons = new List<Button>();
        readonly List<(Button button, RawImage picture, Outline frame)> slotButtons = new List<(Button, RawImage, Outline)>();
        readonly Queue<(RawImage image, string item)> pendingIcons = new Queue<(RawImage, string)>();
        string pointedItem;   // the item under the mouse / selected with the gamepad
        RectTransform list;
        TMP_InputField search;
        ItemActions actions;
        int tab;

        public bool IsOpen => screen.activeSelf;
        public int Tab => tab;

        void Start()
        {
            screen.SetActive(false);

            var top = UiKit.Row(itemArea, "Tabs", 40f);
            UiKit.TopStrip((RectTransform)top.transform, 0f, 40f);
            for (int i = 0; i < AllTabs.Length; i++)
            {
                int t = i;
                tabButtons.Add(UiKit.TextButton(top.transform, AllTabs[i], theme, TabWidths[i], () => SetTab(t), 15f));
            }
            var gap = UiKit.Rect(top.transform, "Gap");
            gap.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            search = UiKit.InputBox(top.transform, theme, 150f, false, TMP_InputField.ContentType.Standard);
            search.onValueChanged.AddListener(_ => Rebuild());
            if (search.placeholder is TextMeshProUGUI hint) hint.text = "Search all...";

            var area = UiKit.Rect(itemArea, "List Area");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = new Vector2(0f, -48f);
            list = UiKit.ScrollList(area);

            for (int i = 0; i < Hotbar.SlotCount; i++) MakeSlotButton(i);
            actions = new ItemActions(screen.transform, theme, library, models);
            models.Changed += Rebuild;
            library.Changed += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (library != null) library.Changed -= Rebuild;
            if (models != null) models.Changed -= Rebuild;
        }

        public void SetTab(int index)
        {
            tab = Mathf.Clamp(index, 0, AllTabs.Length - 1);
            if (search.text.Length > 0) search.SetTextWithoutNotify("");
            Rebuild();
        }

        void Update()
        {
            if (!IsOpen)
            {
                if (input.Pressed(input.Inventory)) Open();
                return;
            }

            // Pictures of library parts, a few per frame.
            for (int i = 0; i < IconsPerFrame && pendingIcons.Count > 0; i++)
            {
                var (image, item) = pendingIcons.Dequeue();
                if (image == null) continue;
                image.texture = icons.Get(item);
                image.enabled = image.texture != null;
            }

            if (actions.IsOpen)
            {
                // The right-click window is open: Esc closes just that window; keys are for typing a name.
                if (input.PressedInMenu(input.Cancel)) actions.Close();
                return;
            }

            if (input.PressedInMenu(input.Inventory) || input.PressedInMenu(input.Cancel))
            {
                Close();
                return;
            }
            if (search.isFocused) return;   // typing in the search box: number keys are letters, not slots

            // Point at an item + number key = put it in that slot.
            for (int i = 0; i < Hotbar.SlotCount; i++)
                if (pointedItem != null && input.PressedInMenu(input.Slots[i]))
                {
                    hotbar.Assign(i, pointedItem);
                    hotbar.Select(i);
                    Refresh();
                }

            if (input.PressedInMenu(input.HotbarNext)) { hotbar.Select((hotbar.Selected + 1) % Hotbar.SlotCount); Refresh(); }
            if (input.PressedInMenu(input.HotbarPrev)) { hotbar.Select((hotbar.Selected + Hotbar.SlotCount - 1) % Hotbar.SlotCount); Refresh(); }
        }

        public void Open()
        {
            screen.SetActive(true);
            input.SetMenuOpen(true);
            Refresh();
            // Select the first item so a gamepad can start moving around right away.
            if (EventSystem.current != null && itemButtons.Count > 0)
                EventSystem.current.SetSelectedGameObject(itemButtons[0].gameObject);
        }

        public void Close()
        {
            actions?.Close();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            screen.SetActive(false);
            input.SetMenuOpen(false);
        }

        // ---------- building the lists ----------

        void Rebuild()
        {
            if (list == null) return;
            foreach (Transform child in list) Destroy(child.gameObject);
            itemButtons.Clear();
            pendingIcons.Clear();
            for (int i = 0; i < tabButtons.Count; i++)
                tabButtons[i].image.color = i == tab && search.text.Length == 0 ? UiKit.Accent * new Color(1f, 1f, 1f, 0.55f) : UiKit.Slot;

            string find = search.text.Trim();
            if (find.Length > 0) BuildSearch(find);
            else if (tab == 0) BuildBasic();
            else if (tab == AllTabs.Length - 1) BuildMyStuff();
            else BuildLibraryTab(AllTabs[tab]);
        }

        void BuildBasic()
        {
            Heading(list, "Shapes");
            var shapes = Grid(list, "Shape Grid");
            foreach (var shape in ShapeRegistry.All) MakeItemButton(shapes, shape.Id, shape.DisplayName, icons.Get(shape.Id));

            Heading(list, "Points  <size=75%><color=#9EA7B3>place corners, then press B to build the solid around them</color></size>");
            var pointGrid = Grid(list, "Point Grid");
            foreach (var shape in ShapeRegistry.Points) MakeItemButton(pointGrid, shape.Id, shape.DisplayName, icons.Get(shape.Id));

            Heading(list, "Bones  <size=75%><color=#9EA7B3>rigger tube: click the start joint, then the end joint (K = bones panel)</color></size>");
            var boneGrid = Grid(list, "Bone Grid");
            foreach (var shape in ShapeRegistry.Bones) MakeItemButton(boneGrid, shape.Id, shape.DisplayName, icons.Get(shape.Id));

            Heading(list, "Reference  <size=75%><color=#9EA7B3>size guides, not exported</color></size>");
            var referenceGrid = Grid(list, "Reference Grid");
            foreach (var shape in ShapeRegistry.Reference) MakeItemButton(referenceGrid, shape.Id, shape.DisplayName, icons.Get(shape.Id));
        }

        void BuildLibraryTab(string tabName)
        {
            var inTab = PartLibrary.Items.Where(it => it.tab == tabName).ToList();
            if (inTab.Count == 0) Note(list, "Nothing here yet.");
            foreach (var category in inTab.GroupBy(it => it.category))
            {
                Heading(list, $"{category.Key}  <size=75%><color=#9EA7B3>{category.Count()}</color></size>");
                var grid = Grid(list, "Category " + category.Key);
                foreach (var it in category) MakeItemButton(grid, PartLibrary.ItemOf(it), it.name, null, lazyPicture: true);
            }
            Note(list, "Ready-made parts: each one is a few pieces (one per color) - move, resize, paint or cut them like any piece. " +
                       "Free CC0 models by Kenney, Quaternius and others (StreamingAssets/Library/CREDITS.md).");
        }

        void BuildMyStuff()
        {
            Heading(list, "Models  <size=75%><color=#9EA7B3>imported 3D files (Esc > Import 3D model)</color></size>");
            if (ShapeRegistry.Models.Count == 0) Note(list, "No imported models yet.");
            var modelGrid = Grid(list, "Model Grid");
            foreach (var shape in ShapeRegistry.Models) MakeItemButton(modelGrid, shape.Id, shape.DisplayName, null, lazyPicture: true);

            Heading(list, "Blueprints");
            if (library.All.Count == 0)
                Note(list, "No blueprints yet. Select pieces (I), then press \"Save as Blueprint\" in the edit panel.");
            foreach (var category in library.All.GroupBy(b => string.IsNullOrWhiteSpace(b.category) ? "Other" : b.category))
            {
                Heading(list, category.Key, 17f, UiKit.Text);
                var grid = Grid(list, "Category " + category.Key);
                foreach (var bp in category)
                    MakeItemButton(grid, BlueprintLibrary.ItemOf(bp), bp.name, library.Picture(bp.id));
            }
        }

        static bool Matches(string find, params string[] texts) =>
            texts.Any(t => !string.IsNullOrEmpty(t) && t.IndexOf(find, System.StringComparison.OrdinalIgnoreCase) >= 0);

        void BuildSearch(string find)
        {
            var found = new List<(string item, string name)>();
            foreach (var s in ShapeRegistry.All.Concat(ShapeRegistry.Points).Concat(ShapeRegistry.Bones).Concat(ShapeRegistry.Reference).Concat(ShapeRegistry.Models))
                if (Matches(find, s.DisplayName)) found.Add((s.Id, s.DisplayName));
            foreach (var bp in library.All)
                if (Matches(find, bp.name, bp.category)) found.Add((BlueprintLibrary.ItemOf(bp), bp.name));
            foreach (var it in PartLibrary.Items)
                if (Matches(find, it.name, it.category, it.tab)) found.Add((PartLibrary.ItemOf(it), it.name));

            Heading(list, $"Search \"{find}\"  <size=75%><color=#9EA7B3>{found.Count} found</color></size>");
            if (found.Count == 0) Note(list, "Nothing found. Try another word (e.g. gear, tank, wheel, gun, pipe).");
            var grid = Grid(list, "Results");
            foreach (var (item, name) in found.Take(200)) MakeItemButton(grid, item, name, null, lazyPicture: true);
        }

        void Heading(Transform parent, string text, float size = 20f, Color? color = null)
        {
            var h = UiKit.Label(parent, "Heading", theme, size, color ?? UiKit.Accent, TextAlignmentOptions.BottomLeft, "<b>" + text + "</b>");
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 14f;
        }

        RectTransform Grid(Transform parent, string name)
        {
            var grid = UiKit.Rect(parent, name);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(Cell, Cell);
            g.spacing = new Vector2(12f, 12f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 5;
            g.childAlignment = TextAnchor.UpperLeft;
            return grid;
        }

        void Note(Transform parent, string text)
        {
            var n = UiKit.Label(parent, "Note", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Left, text);
            n.textWrappingMode = TextWrappingModes.Normal;
            n.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
        }

        void MakeItemButton(Transform parent, string item, string displayName, Texture picture, bool lazyPicture = false)
        {
            var button = UiKit.Button(parent, item, theme);
            var image = UiKit.Rect(button.transform, "Picture").gameObject.AddComponent<RawImage>();
            image.rectTransform.anchorMin = new Vector2(0f, 0.22f);
            image.rectTransform.anchorMax = new Vector2(1f, 1f);
            image.rectTransform.offsetMin = new Vector2(14f, 0f);
            image.rectTransform.offsetMax = new Vector2(-14f, -10f);
            image.texture = picture;
            image.enabled = picture != null;
            image.raycastTarget = false;
            if (lazyPicture) pendingIcons.Enqueue((image, item));   // made in Update, a few per frame

            var name = UiKit.Label(button.transform, "Name", theme, 17f, UiKit.Text, TextAlignmentOptions.Center, displayName);
            name.rectTransform.anchorMin = Vector2.zero;
            name.rectTransform.anchorMax = new Vector2(1f, 0.22f);
            name.rectTransform.offsetMin = new Vector2(4f, 0f);
            name.rectTransform.offsetMax = new Vector2(-4f, 0f);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;

            button.onClick.AddListener(() =>
            {
                hotbar.Assign(hotbar.Selected, item);
                Refresh();
            });
            button.gameObject.AddComponent<PointerTracker>().OnPointed = () => pointedItem = item;
            if (BlueprintLibrary.IsBlueprintItem(item) || item.StartsWith(ModelShape.Prefix, System.StringComparison.Ordinal))
                button.gameObject.AddComponent<RightClickTracker>().OnRightClick = () => actions.Open(item);
            itemButtons.Add(button);
        }

        void MakeSlotButton(int index)
        {
            var button = UiKit.Button(slotRow, "Slot " + (index + 1), theme);
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = layout.preferredHeight = 72f;

            var picture = UiKit.Rect(button.transform, "Picture").gameObject.AddComponent<RawImage>();
            UiKit.Fill(picture.rectTransform, 7f);
            picture.raycastTarget = false;
            var number = UiKit.Label(button.transform, "Number", theme, 14f, UiKit.SubtleText,
                                     TextAlignmentOptions.TopLeft, (index + 1).ToString());
            UiKit.Fill(number.rectTransform, 5f);

            var frame = button.gameObject.AddComponent<Outline>();
            frame.effectColor = UiKit.Accent;
            frame.effectDistance = new Vector2(3f, -3f);

            button.onClick.AddListener(() =>
            {
                hotbar.Select(index);
                Refresh();
            });
            slotButtons.Add((button, picture, frame));
        }

        void Refresh()
        {
            for (int i = 0; i < slotButtons.Count; i++)
            {
                var (_, picture, frame) = slotButtons[i];
                picture.texture = icons.Get(hotbar.ItemAt(i));
                picture.enabled = picture.texture != null;
                frame.enabled = i == hotbar.Selected;
            }
            info.text = $"Filling slot <color={UiKit.AccentHex}><b>{hotbar.Selected + 1}</b></color>  -  " +
                        $"click an item to put it there, or point at one and press <b>1-9</b>.  " +
                        $"<color={UiKit.SubtleHex}>Right-click a blueprint or model: rename, delete, save as file.</color>";
        }
    }
}
