using Builder.CameraControl;
using Builder.Input;
using Builder.IO;
using Builder.Tools;
using Builder.UI;
using Builder.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using Selection = Builder.Tools.Selection;   // not UnityEditor.Selection
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static Builder.EditorTools.BuilderSceneSetup;

namespace Builder.EditorTools
{
    /// <summary>
    /// Builds the on-screen UI: crosshair, info panel, help panel, hotbar and inventory.
    /// Runtime scripts fill in the parts that change (slots, shape buttons).
    /// </summary>
    public static class BuilderHudSetup
    {
        public class Parts
        {
            public TMP_FontAsset font;
            public BuilderSession session;
            public BuilderInput input;
            public CameraRig cameraRig;
            public BuildTool buildTool;
            public Hotbar hotbar;
            public DocumentView documentView;
            public ShapeIcons icons;
            public ToolManager tools;
            public Selection selection;
            public PaintTool paintTool;
            public EraseTool eraseTool;
            public ExportController exporter;
            public ProjectController projectController;
            public PrecisionMode precision;
            public BlueprintLibrary library;
            public ModelLibrary models;
            public Camera camera;
            public Material overlayMaterial;
            public BoneView boneView;
            public PoseTest pose;
            public SelectTool selectTool;
            public Builder.IO.Cloud.CloudSync cloud;
        }

        const string Sub = UiKit.SubtleHex;

        public static void Build(Parts p)
        {
            // The canvas: everything on screen lives under it. Scales with the screen like a 1920x1080 layout.
            var hud = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var theme = hud.AddComponent<UiTheme>();
            theme.rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            theme.font = p.font;
            var root = hud.transform;

            // ---------- crosshair: a small "+" in the middle ----------
            var crosshair = UiKit.Rect(root, "Crosshair");
            UiKit.Pin(crosshair, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
            crosshair.pivot = new Vector2(0.5f, 0.5f);
            Bar(crosshair, "H", new Vector2(22, 2));
            Bar(crosshair, "V", new Vector2(2, 22));

            // ---------- info panel (top left) ----------
            var statusBox = UiKit.Box(root, "Info", theme, UiKit.Panel);
            UiKit.Pin(statusBox.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(400f, 0f));
            AutoHeight(statusBox.gameObject, 16, 14);
            var statusText = UiKit.Label(statusBox.transform, "Text", theme, 20f, UiKit.Text, TextAlignmentOptions.TopLeft);
            statusText.lineSpacing = 8f;

            // ---------- help hint + help panel (right) ----------
            var hint = UiKit.Label(root, "Help Hint", theme, 17f, UiKit.Text, TextAlignmentOptions.BottomRight,
                                   $"<color={Sub}>F1</color>  Controls     <color={Sub}>Esc / Start</color>  Menu");
            UiKit.Pin(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(520f, 30f));

            var helpBox = UiKit.Box(root, "Help", theme, UiKit.Panel);
            UiKit.Pin(helpBox.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(690f, 0f));
            helpBox.rectTransform.pivot = new Vector2(1f, 0.5f);
            AutoHeight(helpBox.gameObject, 22, 18);
            // The text is made at runtime from the current keys (Hud.ToggleHelp).
            var helpText = UiKit.Label(helpBox.transform, "Text", theme, 16f, UiKit.Text, TextAlignmentOptions.TopLeft);
            helpText.lineSpacing = 4f;
            helpBox.gameObject.SetActive(false);

            // ---------- attach point list (right of the crosshair, shown while Q is held) ----------
            var attachBox = UiKit.Box(root, "Attach List", theme, UiKit.Panel);
            UiKit.Pin(attachBox.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(60f, 0f), new Vector2(340f, 0f));
            attachBox.rectTransform.pivot = new Vector2(0f, 0.5f);
            AutoHeight(attachBox.gameObject, 16, 12);
            var attachText = UiKit.Label(attachBox.transform, "Text", theme, 19f, UiKit.Text, TextAlignmentOptions.TopLeft);
            attachText.lineSpacing = 6f;
            attachBox.gameObject.SetActive(false);
            var attachList = hud.AddComponent<AttachList>();
            Set(attachList, "buildTool", p.buildTool);
            Set(attachList, "panel", attachBox.gameObject);
            Set(attachList, "text", attachText);

            // ---------- hotbar (bottom center) ----------
            var hotbarBox = UiKit.Box(root, "Hotbar", theme, UiKit.Panel);
            UiKit.Pin(hotbarBox.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), Vector2.zero);
            var row = hotbarBox.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(10, 10, 10, 10);
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fit = hotbarBox.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var shapeName = UiKit.Label(root, "Shape Name", theme, 24f, UiKit.Text, TextAlignmentOptions.Bottom);
            UiKit.Pin(shapeName.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(600f, 34f));
            shapeName.fontStyle = FontStyles.Bold;

            var touch = hud.AddComponent<TouchControls>();
            Set(touch, "input", p.input);
            Set(touch, "theme", theme);
            Set(touch, "canvasRoot", (RectTransform)hud.transform);
            Set(touch, "hotbarRow", hotbarBox.rectTransform);

            var hotbarView = hud.AddComponent<HotbarView>();
            Set(hotbarView, "hotbar", p.hotbar);
            Set(hotbarView, "icons", p.icons);
            Set(hotbarView, "theme", theme);
            Set(hotbarView, "slotRow", hotbarBox.rectTransform);
            Set(hotbarView, "shapeName", shapeName);

            // ---------- color palette (Paint tool, above the hotbar) ----------
            var paletteBox = UiKit.Box(root, "Palette", theme, UiKit.Panel);
            UiKit.Pin(paletteBox.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 168f), Vector2.zero);
            var paletteRow = paletteBox.gameObject.AddComponent<HorizontalLayoutGroup>();
            paletteRow.padding = new RectOffset(10, 10, 8, 8);
            paletteRow.spacing = 6f;
            paletteRow.childAlignment = TextAnchor.MiddleCenter;
            paletteRow.childControlWidth = paletteRow.childControlHeight = true;
            paletteRow.childForceExpandWidth = paletteRow.childForceExpandHeight = false;
            var paletteFit = paletteBox.gameObject.AddComponent<ContentSizeFitter>();
            paletteFit.horizontalFit = paletteFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            paletteBox.gameObject.SetActive(false);
            var paletteView = hud.AddComponent<PaletteView>();
            Set(paletteView, "paintTool", p.paintTool);
            Set(paletteView, "tools", p.tools);
            Set(paletteView, "theme", theme);
            Set(paletteView, "row", paletteBox.rectTransform);

            // ---------- short messages (above the hotbar) ----------
            var toastText = UiKit.Label(root, "Toast", theme, 22f, UiKit.Text, TextAlignmentOptions.Bottom);
            UiKit.Pin(toastText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(900f, 34f));
            toastText.outlineWidth = 0.2f;
            Set(hud.AddComponent<Toast>(), "label", toastText);

            // ---------- edit panel (Select tool, right side) ----------
            var editBox = UiKit.Box(root, "Edit Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(editBox.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(430f, 0f));
            editBox.rectTransform.pivot = new Vector2(1f, 0.5f);
            AutoHeight(editBox.gameObject, 14, 14);
            editBox.GetComponent<VerticalLayoutGroup>().spacing = 4f;
            var editTitle = UiKit.Label(editBox.transform, "Title", theme, 24f, UiKit.Text, TextAlignmentOptions.Left);
            var editRows = UiKit.Rect(editBox.transform, "Rows");
            var rowsLayout = editRows.gameObject.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 2f;
            rowsLayout.childControlWidth = rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            UiKit.Label(editBox.transform, "Hint", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Left,
                "Drag arrows: move   ·   rings: turn   ·   white cubes: resize\nHold right mouse: look   ·   Delete: remove   ·   Esc: deselect");
            var colorWindow = hud.AddComponent<ColorWindow>();
            Set(colorWindow, "session", p.session);
            Set(colorWindow, "input", p.input);
            Set(colorWindow, "paintTool", p.paintTool);
            Set(colorWindow, "theme", theme);
            Set(colorWindow, "canvasRoot", (RectTransform)hud.transform);
            Set(p.paintTool, "colorWindow", colorWindow);

            var blueprintDialog = hud.AddComponent<BlueprintDialog>();
            Set(blueprintDialog, "input", p.input);
            Set(blueprintDialog, "theme", theme);
            Set(blueprintDialog, "canvasRoot", (RectTransform)hud.transform);
            Set(blueprintDialog, "library", p.library);
            Set(blueprintDialog, "icons", p.icons);
            Set(blueprintDialog, "selection", p.selection);
            Set(blueprintDialog, "session", p.session);
            var editPanel = hud.AddComponent<EditPanel>();
            Set(editPanel, "blueprintDialog", blueprintDialog);
            Set(editPanel, "session", p.session);
            Set(editPanel, "input", p.input);
            Set(editPanel, "selection", p.selection);
            Set(editPanel, "tools", p.tools);
            Set(editPanel, "theme", theme);
            Set(editPanel, "panel", editBox.gameObject);
            Set(editPanel, "rowParent", editRows);
            Set(editPanel, "title", editTitle);

            // ---------- groups panel (Select tool, bottom left) ----------
            var groupBox = UiKit.Box(root, "Groups Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(groupBox.rectTransform, new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(590f, 0f));
            AutoHeight(groupBox.gameObject, 14, 12);
            groupBox.GetComponent<VerticalLayoutGroup>().spacing = 6f;
            UiKit.Label(groupBox.transform, "Title", theme, 22f, UiKit.Text, TextAlignmentOptions.Left,
                "<b>Groups</b>  <size=70%><color=#9EA7B3>(each group = one part in the .glb)   H: hide</color></size>");
            var groupHeader = UiKit.Row(groupBox.transform, "Header", 34f);
            var groupRows = UiKit.Rect(groupBox.transform, "Rows");
            var groupRowsLayout = groupRows.gameObject.AddComponent<VerticalLayoutGroup>();
            groupRowsLayout.spacing = 3f;
            groupRowsLayout.childControlWidth = groupRowsLayout.childControlHeight = true;
            groupRowsLayout.childForceExpandWidth = true;
            groupRowsLayout.childForceExpandHeight = false;
            var pivotArea = UiKit.Rect(groupBox.transform, "Pivot");
            var pivotLayout = pivotArea.gameObject.AddComponent<VerticalLayoutGroup>();
            pivotLayout.spacing = 4f;
            pivotLayout.childControlWidth = pivotLayout.childControlHeight = true;
            pivotLayout.childForceExpandWidth = true;
            pivotLayout.childForceExpandHeight = false;
            var pivotTitle = UiKit.Label(pivotArea, "Title", theme, 17f, UiKit.Text, TextAlignmentOptions.Left);
            var bonesPanel = hud.AddComponent<BonesPanel>();
            Set(bonesPanel, "session", p.session);
            Set(bonesPanel, "input", p.input);
            Set(bonesPanel, "tools", p.tools);
            Set(bonesPanel, "selection", p.selection);
            Set(bonesPanel, "boneView", p.boneView);
            Set(bonesPanel, "pose", p.pose);
            Set(bonesPanel, "theme", theme);
            Set(bonesPanel, "canvasRoot", (RectTransform)hud.transform);

            var tree = hud.AddComponent<SkeletonTree>();
            Set(tree, "session", p.session);
            Set(tree, "bonesPanel", bonesPanel);
            Set(tree, "selection", p.selection);
            Set(tree, "theme", theme);
            Set(tree, "canvasRoot", (RectTransform)hud.transform);
            Set(editPanel, "bones", bonesPanel);

            var boneGizmo = hud.AddComponent<BoneGizmo>();
            Set(boneGizmo, "session", p.session);
            Set(boneGizmo, "input", p.input);
            Set(boneGizmo, "panel", bonesPanel);
            Set(boneGizmo, "pose", p.pose);
            Set(boneGizmo, "viewCamera", p.camera);
            Set(boneGizmo, "overlayMaterial", p.overlayMaterial);
            Set(p.selectTool, "boneGizmo", boneGizmo);

            var groupPanel = hud.AddComponent<GroupPanel>();
            Set(groupPanel, "bones", bonesPanel);
            Set(groupPanel, "session", p.session);
            Set(groupPanel, "input", p.input);
            Set(groupPanel, "tools", p.tools);
            Set(groupPanel, "selection", p.selection);
            Set(groupPanel, "documentView", p.documentView);
            Set(groupPanel, "exporter", p.exporter);
            Set(groupPanel, "theme", theme);
            Set(groupPanel, "panel", groupBox.gameObject);
            Set(groupPanel, "header", (RectTransform)groupHeader.transform);
            Set(groupPanel, "rowParent", groupRows);
            Set(groupPanel, "pivotArea", pivotArea.gameObject);
            Set(groupPanel, "pivotTitle", pivotTitle);
            Set(groupPanel, "viewCamera", p.camera);
            Set(groupPanel, "overlayMaterial", p.overlayMaterial);

            // ---------- inventory (hidden until Tab) ----------
            BuildInventory(hud, theme, p);

            // ---------- info panel script ----------
            var hudScript = hud.AddComponent<Hud>();
            Set(hudScript, "session", p.session);
            Set(hudScript, "input", p.input);
            Set(hudScript, "cameraRig", p.cameraRig);
            Set(hudScript, "buildTool", p.buildTool);
            Set(hudScript, "tools", p.tools);
            Set(hudScript, "selection", p.selection);
            Set(hudScript, "paintTool", p.paintTool);
            Set(hudScript, "eraseTool", p.eraseTool);
            Set(hudScript, "documentView", p.documentView);
            Set(hudScript, "statusText", statusText);
            Set(hudScript, "helpPanel", helpBox.gameObject);
            Set(hudScript, "helpText", helpText);

            // Banner at the top while precision mode is on.
            var banner = UiKit.Box(root, "Precision Banner", theme, new Color(0.35f, 0.28f, 0.05f, 0.85f));
            UiKit.Pin(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1180f, 44f));
            var bannerText = UiKit.Label(banner.transform, "Text", theme, 19f, new Color(1f, 0.9f, 0.55f), TextAlignmentOptions.Center);
            UiKit.Fill(bannerText.rectTransform, 6f);
            Set(hudScript, "precision", p.precision);
            Set(hudScript, "precisionBanner", bannerText);

            // ---------- menu (Esc / Start): resume, controls (change keys), help, quit ----------
            var projectsPage = hud.AddComponent<ProjectsPage>();
            Set(projectsPage, "session", p.session);
            Set(projectsPage, "projects", p.projectController);
            Set(projectsPage, "theme", theme);
            var modelsPage = hud.AddComponent<ModelsPage>();
            Set(modelsPage, "models", p.models);
            Set(modelsPage, "theme", theme);
            var menu = hud.AddComponent<MenuScreen>();
            Set(menu, "modelsPage", modelsPage);
            var blueprintFilesPage = hud.AddComponent<BlueprintFilesPage>();
            Set(blueprintFilesPage, "library", p.library);
            Set(blueprintFilesPage, "theme", theme);
            Set(menu, "blueprintFilesPage", blueprintFilesPage);
            var cloudPage = hud.AddComponent<CloudPage>();
            Set(cloudPage, "cloud", p.cloud);
            Set(cloudPage, "theme", theme);
            Set(menu, "cloudPage", cloudPage);
            Set(menu, "projectsPage", projectsPage);
            Set(menu, "projectController", p.projectController);
            Set(menu, "exporter", p.exporter);
            Set(menu, "input", p.input);
            Set(menu, "theme", theme);
            Set(menu, "hud", hudScript);
            Set(menu, "canvasRoot", (RectTransform)hud.transform);
            Set(hudScript, "crosshair", crosshair.gameObject);

            // ---------- event system: lets the mouse and gamepad click UI buttons ----------
            var events = new GameObject("EventSystem", typeof(EventSystem));
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        static void BuildInventory(GameObject hud, UiTheme theme, Parts p)
        {
            // Dark see-through layer over the whole screen (also blocks clicks to the world).
            var screen = UiKit.Rect(hud.transform, "Inventory");
            UiKit.Fill(screen);
            var dim = screen.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            var panel = UiKit.Box(screen, "Panel", theme, UiKit.Panel);
            UiKit.Pin(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 720f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var t = panel.transform;

            var title = UiKit.Label(t, "Title", theme, 34f, UiKit.Text, TextAlignmentOptions.Left, "<b>Inventory</b>");
            Top(title.rectTransform, 24f, 44f);
            var info = UiKit.Label(t, "Info", theme, 18f, UiKit.SubtleText, TextAlignmentOptions.Left);
            Top(info.rectTransform, 74f, 28f);

            // Scrolling list of shapes + blueprints (filled by InventoryScreen).
            var items = UiKit.Rect(t, "Items");
            Top(items, 120f, 350f);

            var hotbarTitle = UiKit.Label(t, "Hotbar Title", theme, 20f, UiKit.Text, TextAlignmentOptions.Left, "<b>Hotbar</b>");
            Top(hotbarTitle.rectTransform, 478f, 30f);

            var slots = UiKit.Rect(t, "Slots");
            slots.anchorMin = slots.anchorMax = slots.pivot = new Vector2(0.5f, 1f);
            slots.anchoredPosition = new Vector2(0f, -516f);
            var slotLayout = slots.gameObject.AddComponent<HorizontalLayoutGroup>();
            slotLayout.spacing = 8f;
            slotLayout.childControlWidth = slotLayout.childControlHeight = true;
            slotLayout.childForceExpandWidth = slotLayout.childForceExpandHeight = false;
            var fit = slots.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hints = UiKit.Label(t, "Hints", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Left,
                "Tab / Esc  close        Gamepad:  stick or D-pad  move   ·   A  choose   ·   LB / RB  slot   ·   B  close");
            hints.rectTransform.anchorMin = new Vector2(0f, 0f);
            hints.rectTransform.anchorMax = new Vector2(1f, 0f);
            hints.rectTransform.pivot = new Vector2(0.5f, 0f);
            hints.rectTransform.offsetMin = new Vector2(32f, 22f);
            hints.rectTransform.offsetMax = new Vector2(-32f, 50f);

            var inventory = hud.AddComponent<InventoryScreen>();
            Set(inventory, "input", p.input);
            Set(inventory, "hotbar", p.hotbar);
            Set(inventory, "icons", p.icons);
            Set(inventory, "theme", theme);
            Set(inventory, "screen", screen.gameObject);
            Set(inventory, "itemArea", items);
            Set(inventory, "library", p.library);
            Set(inventory, "models", p.models);
            Set(inventory, "slotRow", slots);
            Set(inventory, "info", info);
        }

        static void Bar(Transform parent, string name, Vector2 size)
        {
            var img = UiKit.Rect(parent, name).gameObject.AddComponent<Image>();
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            img.rectTransform.sizeDelta = size;
            img.color = new Color(1f, 1f, 1f, 0.9f);
            img.raycastTarget = false;
        }

        /// <summary>Makes a panel grow to fit its text (fixed width, automatic height).</summary>
        static void AutoHeight(GameObject panel, int padX, int padY)
        {
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padX, padX, padY, padY);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>Full width (with side margins) strip at a given distance from the panel's top.</summary>
        static void Top(RectTransform rt, float fromTop, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(32f, -fromTop - height);
            rt.offsetMax = new Vector2(-32f, -fromTop);
        }
    }
}
