using System;
using System.IO;
using Builder.IO;
using Builder.Shapes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The "Import 3D model" page of the menu: lists the .glb / .gltf / .obj / .stl / .3mf files in the Models folder
    /// (Open folder shows where) with an Import button each, and the models already imported.
    /// Imported models appear in the inventory under "Models".
    /// </summary>
    public class ModelsPage : MonoBehaviour
    {
        [SerializeField] ModelLibrary models;
        [SerializeField] UiTheme theme;

        GameObject page;
        RectTransform list;
        Button backButton;
        bool busy;

        public Button FirstButton => backButton;

        /// <summary>Makes the page inside the menu panel (hidden). <paramref name="back"/> = go back to the menu.</summary>
        public GameObject Build(Transform panel, Action back)
        {
            var root = UiKit.Rect(panel, "Models");
            UiKit.Fill(root, 24f);
            page = root.gameObject;

            var title = UiKit.Label(root, "Title", theme, 30f, UiKit.Text, TextAlignmentOptions.Left, "<b>Import 3D model</b>");
            UiKit.TopStrip(title.rectTransform, 0f, 44f);

            var buttons = UiKit.Row(root, "Buttons", 40f);
            UiKit.TopStrip((RectTransform)buttons.transform, 50f, 40f);
            backButton = UiKit.TextButton(buttons.transform, "< Back", theme, 110f, () => back(), 18f);
            UiKit.TextButton(buttons.transform, "Open models folder", theme, 210f, OpenFolder, 18f);
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

            Note("Put <b>.glb</b>, <b>.gltf</b>, <b>.obj</b>, <b>.stl</b> or <b>.3mf</b> files in this folder, then press Import:\n" + ModelLibrary.ImportFolder);
            Note($"<color={UiKit.SubtleHex}>From Blender: File > Export > glTF 2.0 (.glb) or Wavefront (.obj). " +
                 $"3D-print files (MakerWorld, Printables ...): .stl or .3mf, made 1 m big (resize with E). " +
                 $"Up to {ModelLibrary.MaxTriangles} triangles; colors are kept (up to 8, one piece each).</color>");

            Heading("Files to import");
            var files = ModelLibrary.ImportCandidates();
            if (files.Count == 0) Note("No 3D files in the folder yet.");
            foreach (var file in files) FileRow(file);

            Heading("Imported models  (inventory > Models)");
            if (ShapeRegistry.Models.Count == 0) Note("None yet.");
            foreach (var shape in ShapeRegistry.Models)
            {
                var model = ((ModelShape)shape).Model;
                Note($"{model.name}   <color={UiKit.SubtleHex}>{model.size.x:0.##} x {model.size.y:0.##} x {model.size.z:0.##} m, " +
                     $"{model.triangles.Length / 3} triangles</color>");
            }
        }

        void Heading(string text)
        {
            var h = UiKit.Label(list, "Heading", theme, 19f, UiKit.Accent, TextAlignmentOptions.BottomLeft, "<b>" + text + "</b>");
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
        }

        void Note(string text)
        {
            var n = UiKit.Label(list, "Note", theme, 16f, UiKit.Text, TextAlignmentOptions.Left, text);
            n.textWrappingMode = TextWrappingModes.Normal;
            n.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        }

        void FileRow(string file)
        {
            var row = UiKit.Row(list, "File", 38f);
            var name = UiKit.Label(row.transform, "Name", theme, 17f, UiKit.Text, TextAlignmentOptions.Left, Path.GetFileName(file));
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UiKit.TextButton(row.transform, "Import", theme, 100f, () => Import(file));
        }

        async void Import(string file)
        {
            if (busy) return;
            busy = true;
            Toast.Show("Importing " + Path.GetFileName(file) + "...");
            try
            {
                var model = await models.Import(file);
                Toast.Show($"Imported \"{model.name}\" - it is in the inventory (Tab) > Models");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Could not import " + Path.GetFileName(file) + ": " + e.Message);
            }
            busy = false;
            if (page.activeSelf) Refresh();
        }

        static void OpenFolder()
        {
            Directory.CreateDirectory(ModelLibrary.ImportFolder);
            Application.OpenURL("file:///" + ModelLibrary.ImportFolder.Replace('\\', '/'));
        }
    }
}
