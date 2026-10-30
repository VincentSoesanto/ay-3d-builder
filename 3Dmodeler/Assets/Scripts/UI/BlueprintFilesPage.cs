using System;
using System.IO;
using Builder.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The "Blueprint files" page of the menu: lists the blueprint files (.aybp) in the Blueprints folder with an
    /// Import button each. Files are made with Tab > right-click a blueprint > Save as file.
    /// </summary>
    public class BlueprintFilesPage : MonoBehaviour
    {
        [SerializeField] BlueprintLibrary library;
        [SerializeField] UiTheme theme;

        GameObject page;
        RectTransform list;
        Button backButton;

        public Button FirstButton => backButton;

        /// <summary>Makes the page inside the menu panel (hidden). <paramref name="back"/> = go back to the menu.</summary>
        public GameObject Build(Transform panel, Action back)
        {
            var root = UiKit.Rect(panel, "Blueprint Files");
            UiKit.Fill(root, 24f);
            page = root.gameObject;

            var title = UiKit.Label(root, "Title", theme, 30f, UiKit.Text, TextAlignmentOptions.Left, "<b>Blueprint files</b>");
            UiKit.TopStrip(title.rectTransform, 0f, 44f);

            var buttons = UiKit.Row(root, "Buttons", 40f);
            UiKit.TopStrip((RectTransform)buttons.transform, 50f, 40f);
            backButton = UiKit.TextButton(buttons.transform, "< Back", theme, 110f, () => back(), 18f);
            UiKit.TextButton(buttons.transform, "Open blueprints folder", theme, 240f, OpenFolder, 18f);
            UiKit.TextButton(buttons.transform, "Refresh", theme, 110f, Refresh, 18f);

            var area = UiKit.Rect(root, "List");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = new Vector2(0f, -100f);
            list = UiKit.ScrollList(area);

            page.SetActive(false);
            return page;
        }

        public void Show()
        {
            page.SetActive(true);
            Refresh();
        }

        void Refresh()
        {
            foreach (Transform child in list) Destroy(child.gameObject);

            Note("<b>Share or back up:</b> Tab (inventory) > right-click a blueprint > <b>Save as file</b>. The file goes into this folder:\n" +
                 BlueprintStore.FileFolder);
            Note($"<b>Import:</b> put <b>{BlueprintStore.FileExtension}</b> files in that folder, then press Import. " +
                 $"<color={UiKit.SubtleHex}>Blueprints used inside it come along. Importing a blueprint you already have replaces it " +
                 "(and its copies update).</color>");

            var h = UiKit.Label(list, "Heading", theme, 19f, UiKit.Accent, TextAlignmentOptions.BottomLeft, "<b>Files</b>");
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            var files = BlueprintStore.FileCandidates();
            if (files.Count == 0) Note("No blueprint files in the folder yet.");
            foreach (var file in files)
            {
                var row = UiKit.Row(list, "File", 38f);
                var name = UiKit.Label(row.transform, "Name", theme, 17f, UiKit.Text, TextAlignmentOptions.Left, Path.GetFileName(file));
                name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                string path = file;
                UiKit.TextButton(row.transform, "Import", theme, 100f, () => Import(path));
            }
        }

        void Note(string text)
        {
            var n = UiKit.Label(list, "Note", theme, 16f, UiKit.Text, TextAlignmentOptions.Left, text);
            n.textWrappingMode = TextWrappingModes.Normal;
            n.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        }

        void Import(string file)
        {
            try
            {
                var bp = library.ImportFile(file);
                Toast.Show($"Imported \"{bp?.name}\" - it is in the inventory (Tab) > Blueprints");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Could not import " + Path.GetFileName(file) + ": " + e.Message);
            }
        }

        static void OpenFolder()
        {
            Directory.CreateDirectory(BlueprintStore.FileFolder);
            Application.OpenURL("file:///" + BlueprintStore.FileFolder.Replace('\\', '/'));
        }
    }
}
