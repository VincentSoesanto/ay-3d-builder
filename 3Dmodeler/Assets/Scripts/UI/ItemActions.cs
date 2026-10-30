using System;
using Builder.IO;
using Builder.Shapes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>Calls <see cref="OnRightClick"/> when the right mouse button clicks this UI element.</summary>
    public class RightClickTracker : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
        }
    }

    /// <summary>
    /// The small window that opens when you right-click a blueprint or an imported model in the inventory:
    /// Rename, Delete (asks first), Save as file (blueprints: to share or back up), Cancel.
    /// </summary>
    public class ItemActions
    {
        readonly BlueprintLibrary library;
        readonly ModelLibrary models;
        readonly UiTheme theme;
        readonly GameObject root;
        readonly TextMeshProUGUI title, message;
        readonly TMP_InputField nameBox;
        readonly RectTransform buttons;
        string item;

        public bool IsOpen => root.activeSelf;

        public ItemActions(Transform parent, UiTheme theme, BlueprintLibrary library, ModelLibrary models)
        {
            this.theme = theme;
            this.library = library;
            this.models = models;

            // A see-through layer over the inventory (clicks outside the window do nothing), with the window in the middle.
            var overlay = UiKit.Rect(parent, "Item Actions");
            UiKit.Fill(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            root = overlay.gameObject;

            var panel = UiKit.Box(overlay, "Panel", theme, UiKit.Panel, clickable: true);
            UiKit.Pin(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 0f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 12f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = UiKit.Label(panel.transform, "Title", theme, 24f, UiKit.Text, TextAlignmentOptions.Left);
            message = UiKit.Label(panel.transform, "Message", theme, 17f, UiKit.SubtleText, TextAlignmentOptions.Left);
            message.textWrappingMode = TextWrappingModes.Normal;
            nameBox = UiKit.InputBox(panel.transform, theme, 400f, true, TMP_InputField.ContentType.Standard, 19f);
            nameBox.onSubmit.AddListener(_ => Rename());
            buttons = (RectTransform)UiKit.Row(panel.transform, "Buttons", 44f).transform;
            root.SetActive(false);
        }

        static bool IsModel(string item) => item.StartsWith(ModelShape.Prefix, StringComparison.Ordinal);
        static string IdOf(string item) => IsModel(item) ? item.Substring(ModelShape.Prefix.Length) : BlueprintLibrary.IdOf(item);

        string NameOf(string item) => IsModel(item)
            ? ShapeRegistry.Get(item)?.DisplayName ?? "model"
            : library.FromItem(item)?.name ?? "blueprint";

        /// <summary>Opens the window for a blueprint ("bp:...") or model ("model:...") item.</summary>
        public void Open(string forItem)
        {
            item = forItem;
            root.SetActive(true);
            ShowChoices();
        }

        public void Close()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            root.SetActive(false);
        }

        void ShowChoices()
        {
            bool model = IsModel(item);
            title.text = $"<b>{NameOf(item)}</b>  <size=70%><color={UiKit.SubtleHex}>{(model ? "imported model" : "blueprint")}</color></size>";
            message.text = model ? "" : $"<color={UiKit.SubtleHex}>Save as file: one file you can share or back up (blueprints it uses go inside it too).</color>";
            message.gameObject.SetActive(!model);
            nameBox.gameObject.SetActive(false);
            SetButtons(("Rename", ShowRename), ("Delete", ShowDelete), model ? (null, null) : ("Save as file", SaveFile), ("Cancel", Close));
        }

        void ShowRename()
        {
            title.text = "<b>Rename</b>";
            message.gameObject.SetActive(false);
            nameBox.gameObject.SetActive(true);
            nameBox.text = NameOf(item);
            SetButtons(("Save name", Rename), ("Cancel", ShowChoices));
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(nameBox.gameObject);
            nameBox.ActivateInputField();
        }

        void ShowDelete()
        {
            bool model = IsModel(item);
            title.text = $"<b>Delete \"{NameOf(item)}\"?</b>";
            message.gameObject.SetActive(true);
            message.text = model
                ? "It leaves the inventory. Pieces already made from it stay in your projects."
                : "It goes to the blueprint trash. Copies already placed keep their pieces, but stop updating.";
            nameBox.gameObject.SetActive(false);
            SetButtons(("Delete", Delete), ("Cancel", ShowChoices));
        }

        void Rename()
        {
            string name = nameBox.text.Trim();
            if (name.Length == 0) return;
            if (IsModel(item)) models.Rename(IdOf(item), name);
            else library.Rename(IdOf(item), name);
            Toast.Show($"Renamed to \"{name}\"");
            Close();
        }

        void Delete()
        {
            string name = NameOf(item);
            if (IsModel(item)) models.Delete(IdOf(item));
            else library.Delete(IdOf(item));
            Toast.Show($"Deleted \"{name}\"");
            Close();
        }

        void SaveFile()
        {
            try
            {
                string path = library.ExportFile(IdOf(item));
                Toast.Show("Saved the file: " + path);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Could not save the file: " + e.Message);
            }
            Close();
        }

        void SetButtons(params (string text, Action action)[] list)
        {
            foreach (Transform child in buttons) UnityEngine.Object.Destroy(child.gameObject);
            Button first = null;
            foreach (var (text, action) in list)
            {
                if (text == null) continue;
                var a = action;
                var b = UiKit.TextButton(buttons, text, theme, 120f, () => a(), 18f);
                first ??= b;
            }
            if (EventSystem.current != null && first != null && !nameBox.gameObject.activeSelf)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
        }
    }
}
