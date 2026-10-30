using System;
using System.IO;
using Builder.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The Projects page of the menu:
    ///   - the open project: rename it (type + Enter), Save, Export .json
    ///   - your projects (newest first): Open, Delete (click twice; the file goes to a trash folder)
    ///   - New project, and Import: .json files put in the share folder (Open folder shows where)
    /// </summary>
    public class ProjectsPage : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] ProjectController projects;
        [SerializeField] UiTheme theme;

        const string Orange = "#FFB347";

        GameObject page;
        TMP_InputField nameBox;
        RectTransform list;
        Button backButton;
        string confirmDeleteId;
        float confirmUntil;

        /// <summary>Makes the page inside the menu panel (hidden). <paramref name="back"/> = go back to the menu.</summary>
        public GameObject Build(Transform panel, Action back)
        {
            var root = UiKit.Rect(panel, "Projects");
            UiKit.Fill(root, 24f);
            page = root.gameObject;

            var title = UiKit.Label(root, "Title", theme, 30f, UiKit.Text, TextAlignmentOptions.Left, "<b>Projects</b>");
            UiKit.TopStrip(title.rectTransform, 0f, 44f);

            var buttons = UiKit.Row(root, "Buttons", 40f);
            UiKit.TopStrip((RectTransform)buttons.transform, 50f, 40f);
            backButton = UiKit.TextButton(buttons.transform, "< Back", theme, 110f, () => back(), 18f);
            UiKit.TextButton(buttons.transform, "+ New project", theme, 170f, () => { projects.New(); Refresh(); }, 18f);
            UiKit.TextButton(buttons.transform, "Open share folder", theme, 200f, OpenShareFolder, 18f);

            // The open project: name box + Save + Export.
            var current = UiKit.Row(root, "Current", 40f);
            UiKit.TopStrip((RectTransform)current.transform, 100f, 40f);
            var label = UiKit.Label(current.transform, "Label", theme, 17f, UiKit.SubtleText, TextAlignmentOptions.Left, "Open project");
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
            nameBox = UiKit.InputBox(current.transform, theme, 200f, true, TMP_InputField.ContentType.Standard, 18f);
            nameBox.onEndEdit.AddListener(text => { projects.Rename(text); Refresh(); });
            UiKit.TextButton(current.transform, "Save", theme, 80f, () => { projects.Save(); Refresh(); }, 17f);
            UiKit.TextButton(current.transform, "Export .json", theme, 130f, () => { projects.ExportJson(); Refresh(); }, 17f);

            var area = UiKit.Rect(root, "List");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = new Vector2(0f, -152f);
            list = UiKit.ScrollList(area);

            page.SetActive(false);
            return page;
        }

        public Button FirstButton => backButton;

        public void Show()
        {
            page.SetActive(true);
            Refresh();
        }

        void Update()
        {
            // The "Sure?" on a Delete button runs out after a few seconds.
            if (confirmDeleteId != null && Time.unscaledTime > confirmUntil)
            {
                confirmDeleteId = null;
                if (page.activeSelf) Refresh();
            }
        }

        void Refresh()
        {
            nameBox.SetTextWithoutNotify(session.Document.name);
            foreach (Transform child in list) Destroy(child.gameObject);

            Heading("Your projects");
            var saved = ProjectStore.List();
            if (saved.Count == 0) Note("No saved projects yet. Ctrl+S saves the open one (it also saves by itself).");
            foreach (var p in saved) ProjectRow(p);

            Heading("Import a .json project");
            var files = ProjectStore.ImportCandidates();
            Note($"Put .json files in:  {ProjectStore.ShareFolder}");
            foreach (var file in files) ImportRow(file);
        }

        void Heading(string text)
        {
            var h = UiKit.Label(list, "Heading", theme, 19f, UiKit.Accent, TextAlignmentOptions.BottomLeft, "<b>" + text + "</b>");
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        }

        void Note(string text)
        {
            var n = UiKit.Label(list, "Note", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Left, text);
            n.textWrappingMode = TextWrappingModes.Normal;
            n.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        }

        void ProjectRow(ProjectStore.Info p)
        {
            bool isOpen = p.id == session.Document.id;
            var row = UiKit.Row(list, p.name, 38f);
            var name = UiKit.Label(row.transform, "Name", theme, 18f, UiKit.Text, TextAlignmentOptions.Left,
                isOpen ? $"<b>{p.name}</b>  <color={UiKit.AccentHex}><size=80%>(open)</size></color>" : p.name);
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            string when = DateTimeOffset.FromUnixTimeMilliseconds(p.modifiedUtc).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            var date = UiKit.Label(row.transform, "Date", theme, 15f, UiKit.SubtleText, TextAlignmentOptions.Center, when);
            date.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

            var open = UiKit.TextButton(row.transform, "Open", theme, 80f, () => { projects.Open(p.id); Refresh(); });
            open.interactable = !isOpen;

            bool confirming = confirmDeleteId == p.id;
            UiKit.TextButton(row.transform, confirming ? $"<color={Orange}>Sure?</color>" : "Delete", theme, 80f, () =>
            {
                if (confirmDeleteId == p.id)
                {
                    confirmDeleteId = null;
                    projects.Delete(p.id);
                }
                else
                {
                    confirmDeleteId = p.id;
                    confirmUntil = Time.unscaledTime + 3f;
                }
                Refresh();
            });
        }

        void ImportRow(string file)
        {
            var row = UiKit.Row(list, "Import", 38f);
            var name = UiKit.Label(row.transform, "File", theme, 17f, UiKit.Text, TextAlignmentOptions.Left, Path.GetFileName(file));
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UiKit.TextButton(row.transform, "Import", theme, 90f, () => { projects.ImportJson(file); Refresh(); });
        }

        static void OpenShareFolder()
        {
            Directory.CreateDirectory(ProjectStore.ShareFolder);
            Application.OpenURL("file:///" + ProjectStore.ShareFolder.Replace('\\', '/'));
        }
    }
}
