using System.Collections.Generic;
using System.Linq;
using Builder.Input;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The menu (Esc / gamepad Start). Building pauses and the mouse is free while it is open.
    ///   Main page: Resume, Projects (open / new / rename / delete / export / import), Save, Controls,
    ///   Controls help (F1 list), Quit.
    ///   Controls page: every keyboard/mouse control with its key. "Change" -> press the new key (Esc cancels);
    ///   "Reset" puts one key back, "Reset all" puts every key back. Changes are saved right away
    ///   (see BindingStore) and a key used twice is marked in orange.
    /// Esc / Start / gamepad B goes back one page, then closes the menu.
    /// </summary>
    public class MenuScreen : MonoBehaviour
    {
        [SerializeField] BuilderInput input;
        [SerializeField] UiTheme theme;
        [SerializeField] Hud hud;
        [SerializeField] ProjectsPage projectsPage;
        [SerializeField] ModelsPage modelsPage;
        [SerializeField] BlueprintFilesPage blueprintFilesPage;
        [SerializeField] CloudPage cloudPage;
        [SerializeField] Builder.IO.ProjectController projectController;
        [SerializeField] Builder.IO.ExportController exporter;
        [SerializeField] RectTransform canvasRoot;

        const string Orange = "#FFB347";

        class Row
        {
            public ControlsCatalog.Entry entry;
            public TextMeshProUGUI label, key;
        }

        GameObject screen, mainPage, controlsPage, projectsRoot, modelsRoot, blueprintFilesRoot, cloudRoot;
        Button resumeButton, backButton;
        TextMeshProUGUI controlsHint;
        RectTransform listContent;
        readonly List<Row> rows = new List<Row>();
        InputActionRebindingExtensions.RebindingOperation rebinding;
        int rebindEndedOnFrame = -1;

        public bool IsOpen => screen != null && screen.activeSelf;

        void Start()
        {
            BuildScreen();
            screen.SetActive(false);
        }

        void OnDestroy() => rebinding?.Dispose();

        void Update()
        {
            // While waiting for a new key (and in the frame it arrived), the keys belong to the rebinding.
            if (rebinding != null || Time.frameCount == rebindEndedOnFrame) return;

            if (!IsOpen)
            {
                if (input.Pressed(input.Menu)) Open();
                return;
            }
            if (input.PressedInMenu(input.Menu) || input.PressedInMenu(input.Cancel))
            {
                if (controlsPage.activeSelf || projectsRoot.activeSelf || modelsRoot.activeSelf) ShowMain();
                else Close();
            }
        }

        public void Open()
        {
            screen.SetActive(true);
            input.SetMenuOpen(true);
            ShowMain();
        }

        public void Close()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            screen.SetActive(false);
            input.SetMenuOpen(false);
        }

        void ShowMain()
        {
            mainPage.SetActive(true);
            controlsPage.SetActive(false);
            projectsRoot.SetActive(false);
            modelsRoot.SetActive(false);
            blueprintFilesRoot.SetActive(false);
            cloudRoot.SetActive(false);
            Select(resumeButton);
        }

        void ShowCloud()
        {
            mainPage.SetActive(false);
            cloudPage.Show();
            Select(cloudPage.FirstButton);
        }

        void ShowBlueprintFiles()
        {
            mainPage.SetActive(false);
            blueprintFilesPage.Show();
            Select(blueprintFilesPage.FirstButton);
        }

        void ShowModels()
        {
            mainPage.SetActive(false);
            modelsPage.Show();
            Select(modelsPage.FirstButton);
        }

        void ShowProjects()
        {
            mainPage.SetActive(false);
            projectsPage.Show();
            Select(projectsPage.FirstButton);
        }

        void ShowControls()
        {
            mainPage.SetActive(false);
            controlsPage.SetActive(true);
            controlsHint.text = $"Click <b>Change</b>, then press the new key or mouse button.  <color={UiKit.SubtleHex}>Esc cancels.</color>";
            Refresh();
            Select(backButton);
        }

        static void Select(Button b)
        {
            if (EventSystem.current != null && b != null) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        // ---------- building the screen ----------

        void BuildScreen()
        {
            // Dark see-through layer over everything (also blocks clicks to the world).
            var overlay = UiKit.Rect(canvasRoot, "Menu");
            UiKit.Fill(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            screen = overlay.gameObject;

            var panel = UiKit.Box(overlay, "Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 1020f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            BuildMainPage(panel.transform);
            BuildControlsPage(panel.transform);
            projectsRoot = projectsPage.Build(panel.transform, ShowMain);
            modelsRoot = modelsPage.Build(panel.transform, ShowMain);
            blueprintFilesRoot = blueprintFilesPage.Build(panel.transform, ShowMain);
            cloudRoot = cloudPage.Build(panel.transform, ShowMain);
        }

        void BuildMainPage(Transform panel)
        {
            var page = UiKit.Rect(panel, "Main");
            UiKit.Fill(page, 40f);
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            mainPage = page.gameObject;

            var title = UiKit.Label(page, "Title", theme, 40f, UiKit.Text, TextAlignmentOptions.Center, "<b>Menu</b>");
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;

            resumeButton = BigButton(page, "Resume", Close);
            BigButton(page, "Projects  (open, new, import)", ShowProjects);
            BigButton(page, "Save  (Ctrl+S)", () => projectController.Save());
            BigButton(page, "Export .glb  (Ctrl+E)", () => { Close(); exporter.Export(); });
            BigButton(page, "Export .obj  (for Mixamo)", () => { Close(); exporter.ExportObj(); });
            BigButton(page, "Import 3D model  (.glb .obj)", ShowModels);
            BigButton(page, "Blueprint files  (import, share)", ShowBlueprintFiles);
            BigButton(page, "Cloud sync  (PC + phone)", ShowCloud);
            BigButton(page, "Controls  (change keys)", ShowControls);
            BigButton(page, "Controls help  (F1)", () => { Close(); hud.ToggleHelp(); });
            BigButton(page, "Quit  (saves first)", Quit);
        }

        Button BigButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var b = UiKit.TextButton(parent, text, theme, 420f, onClick, 24f);
            b.GetComponent<LayoutElement>().preferredHeight = 60f;
            return b;
        }

        void Quit()
        {
            projectController.Save(quiet: true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void BuildControlsPage(Transform panel)
        {
            var page = UiKit.Rect(panel, "Controls");
            UiKit.Fill(page, 24f);
            controlsPage = page.gameObject;

            var title = UiKit.Label(page, "Title", theme, 30f, UiKit.Text, TextAlignmentOptions.Left, "<b>Controls</b>  <size=60%>keyboard & mouse</size>");
            UiKit.TopStrip(title.rectTransform, 0f, 44f);

            var buttons = UiKit.Row(page, "Buttons", 40f);
            UiKit.TopStrip((RectTransform)buttons.transform, 50f, 40f);
            backButton = UiKit.TextButton(buttons.transform, "< Back", theme, 110f, ShowMain, 18f);
            UiKit.TextButton(buttons.transform, "Reset all", theme, 130f, ResetAll, 18f);

            controlsHint = UiKit.Label(page, "Hint", theme, 17f, UiKit.Text, TextAlignmentOptions.Left);
            controlsHint.textWrappingMode = TextWrappingModes.Normal;
            UiKit.TopStrip(controlsHint.rectTransform, 98f, 44f);

            var area = UiKit.Rect(page, "List");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = new Vector2(0f, -150f);
            listContent = UiKit.ScrollList(area);

            string section = null;
            foreach (var entry in ControlsCatalog.All)
            {
                if (entry.section != section)
                {
                    section = entry.section;
                    var heading = UiKit.Label(listContent, "Section", theme, 19f, UiKit.Accent, TextAlignmentOptions.BottomLeft, "<b>" + section + "</b>");
                    heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
                }
                rows.Add(AddRow(entry));
            }
        }

        Row AddRow(ControlsCatalog.Entry entry)
        {
            var line = UiKit.Row(listContent, entry.label, 36f);
            var row = new Row { entry = entry };

            row.label = UiKit.Label(line.transform, "Label", theme, 17f, UiKit.Text, TextAlignmentOptions.Left, entry.label);
            row.label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var keyBox = UiKit.Box(line.transform, "Key", theme, UiKit.Slot);
            keyBox.gameObject.AddComponent<LayoutElement>().preferredWidth = 200f;
            row.key = UiKit.Label(keyBox.transform, "Text", theme, 17f, UiKit.Text, TextAlignmentOptions.Center);
            UiKit.Fill(row.key.rectTransform, 4f);

            if (entry.Rebindable)
            {
                UiKit.TextButton(line.transform, "Change", theme, 90f, () => StartRebind(row), 16f);
                UiKit.TextButton(line.transform, "Reset", theme, 70f, () => ResetOne(row), 16f);
            }
            else
            {
                var fixedLabel = UiKit.Label(line.transform, "Fixed", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Center, "fixed");
                fixedLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 166f;
            }
            return row;
        }

        // ---------- showing keys ----------

        void Refresh()
        {
            var asset = input.Actions;
            // Which keys are used by more than one control? (The same binding listed under two modes doesn't count.)
            var users = new Dictionary<string, Dictionary<string, string>>();   // key path -> binding id -> first label
            foreach (var e in ControlsCatalog.All.Where(e => e.Rebindable))
            {
                string path = e.EffectivePath(asset);
                string binding = e.BindingId(asset);
                if (string.IsNullOrEmpty(path) || binding == null) continue;
                if (!users.TryGetValue(path, out var byBinding)) users[path] = byBinding = new Dictionary<string, string>();
                if (!byBinding.ContainsKey(binding)) byBinding[binding] = e.label;
            }

            foreach (var row in rows)
            {
                row.key.text = row.entry.KeyText(asset);
                string path = row.entry.Rebindable ? row.entry.EffectivePath(asset) : null;
                string mine = row.entry.Rebindable ? row.entry.BindingId(asset) : null;
                var others = path != null && users.TryGetValue(path, out var byBinding)
                    ? byBinding.Where(kv => kv.Key != mine).Select(kv => kv.Value).ToList()
                    : null;
                row.label.text = others != null && others.Count > 0
                    ? $"{row.entry.label}  <color={Orange}><size=85%>(same key as {string.Join(", ", others)})</size></color>"
                    : row.entry.label;
            }
        }

        // ---------- changing keys ----------

        void StartRebind(Row row)
        {
            if (rebinding != null) return;
            var action = input.Actions.FindAction(row.entry.action);
            int index = action != null ? row.entry.BindingIndex(action) : -1;
            if (index < 0) return;

            row.key.text = $"<color={Orange}>press a key...</color>";
            controlsHint.text = row.entry.ctrl || row.entry.shift
                ? $"Press the new key for <b>{row.entry.label}</b> (just the letter, {(row.entry.ctrl ? "Ctrl" : "Shift")} stays).  <color={UiKit.SubtleHex}>Esc cancels.</color>"
                : $"Press the new key or mouse button for <b>{row.entry.label}</b>.  <color={UiKit.SubtleHex}>Esc cancels.</color>";

            // An action must be switched off while its key is being changed.
            input.Actions.Disable();
            var op = action.PerformInteractiveRebinding(index)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(_ => EndRebind(true))
                .OnCancel(_ => EndRebind(false));
            if (row.entry.ctrl) op.WithControlsExcluding("<Keyboard>/ctrl").WithControlsExcluding("<Keyboard>/leftCtrl").WithControlsExcluding("<Keyboard>/rightCtrl");
            if (row.entry.shift) op.WithControlsExcluding("<Keyboard>/shift").WithControlsExcluding("<Keyboard>/leftShift").WithControlsExcluding("<Keyboard>/rightShift");
            rebinding = op.Start();
        }

        void EndRebind(bool changed)
        {
            rebinding?.Dispose();
            rebinding = null;
            input.Actions.Enable();
            rebindEndedOnFrame = Time.frameCount;
            if (changed) BindingStore.Save(input.Actions);
            controlsHint.text = changed ? "Saved. The new key works right away." : "Cancelled - the key was not changed.";
            Refresh();
        }

        void ResetOne(Row row)
        {
            var action = input.Actions.FindAction(row.entry.action);
            int index = action != null ? row.entry.BindingIndex(action) : -1;
            if (index < 0) return;
            action.RemoveBindingOverride(index);
            BindingStore.Save(input.Actions);
            controlsHint.text = $"<b>{row.entry.label}</b> is back to its normal key.";
            Refresh();
        }

        void ResetAll()
        {
            input.Actions.RemoveAllBindingOverrides();
            BindingStore.Save(input.Actions);
            controlsHint.text = "All keys are back to normal.";
            Refresh();
        }
    }
}
