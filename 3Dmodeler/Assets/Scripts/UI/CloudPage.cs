using System;
using Builder.IO.Cloud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The "Cloud sync" page of the menu: set up the Firebase project (once), sign in / make an account,
    /// see the sync status, Sync now, Sign out.
    /// </summary>
    public class CloudPage : MonoBehaviour
    {
        [SerializeField] CloudSync cloud;
        [SerializeField] UiTheme theme;

        GameObject page;
        RectTransform list;
        Button backButton;
        TextMeshProUGUI status;
        TMP_InputField email, password, apiKey, projectId;
        bool showSetup;

        public Button FirstButton => backButton;

        public GameObject Build(Transform panel, Action back)
        {
            var root = UiKit.Rect(panel, "Cloud");
            UiKit.Fill(root, 24f);
            page = root.gameObject;

            var title = UiKit.Label(root, "Title", theme, 30f, UiKit.Text, TextAlignmentOptions.Left, "<b>Cloud sync</b>");
            UiKit.TopStrip(title.rectTransform, 0f, 44f);
            var buttons = UiKit.Row(root, "Buttons", 40f);
            UiKit.TopStrip((RectTransform)buttons.transform, 50f, 40f);
            backButton = UiKit.TextButton(buttons.transform, "< Back", theme, 110f, () => back(), 18f);

            var area = UiKit.Rect(root, "List");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = new Vector2(0f, -100f);
            list = UiKit.ScrollList(area);

            cloud.Changed += () => { if (page.activeSelf && status != null) status.text = StatusText(); };
            page.SetActive(false);
            return page;
        }

        public void Show()
        {
            page.SetActive(true);
            showSetup = !cloud.Config.IsSet;
            Refresh();
        }

        string StatusText() =>
            $"<b>Status:</b> {cloud.Status}" + (cloud.Busy ? "  <color=#FFB347>(working...)</color>" : "");

        void Refresh()
        {
            foreach (Transform child in list) Destroy(child.gameObject);
            status = Note(StatusText());

            if (showSetup || !cloud.Config.IsSet) BuildSetup();
            else if (!cloud.SignedIn) BuildSignIn();
            else BuildSignedIn();
        }

        void BuildSetup()
        {
            Heading("1. Your Firebase project (once)");
            Note("Cloud sync uses your own free Firebase project (Spark plan). Make it in the Firebase console - " +
                 "the step-by-step list is in the 3D folder's CLOUD_SETUP.md - then paste its two values here:");
            apiKey = Field("Web API key", cloud.Config.apiKey, false);
            projectId = Field("Project ID", cloud.Config.projectId, false);
            var row = Row();
            UiKit.TextButton(row, "Save", theme, 120f, () =>
            {
                cloud.SaveConfig(apiKey.text, projectId.text);
                showSetup = false;
                Refresh();
            }, 18f);
            if (cloud.Config.IsSet) UiKit.TextButton(row, "Cancel", theme, 120f, () => { showSetup = false; Refresh(); }, 18f);
        }

        void BuildSignIn()
        {
            Heading("2. Sign in");
            Note("Use the same email and password on every device. New here? Type them and press Create account.");
            email = Field("Email", "", false);
            password = Field("Password", "", true);
            var row = Row();
            UiKit.TextButton(row, "Sign in", theme, 130f, () => SignIn(false), 18f);
            UiKit.TextButton(row, "Create account", theme, 170f, () => SignIn(true), 18f);
            UiKit.TextButton(row, "Forgot password", theme, 170f, ForgotPassword, 18f);
            var row2 = Row();
            UiKit.TextButton(row2, "Change Firebase project", theme, 240f, () => { showSetup = true; Refresh(); }, 16f);
        }

        void BuildSignedIn()
        {
            Heading("Signed in as " + cloud.User.email);
            Note("Your projects, blueprints and imported models are kept the same on every device you sign in on. " +
                 $"<color={UiKit.SubtleHex}>It syncs by itself: at start, a few seconds after you save, and every 5 minutes. " +
                 "If a project was changed on two devices, the other version is kept as \"(conflict copy)\". " +
                 "Without internet everything still works; it syncs later.</color>");
            var row = Row();
            UiKit.TextButton(row, "Sync now", theme, 140f, () => cloud.SyncNow(), 18f);
            UiKit.TextButton(row, "Sign out", theme, 130f, () => { cloud.SignOut(); Refresh(); }, 18f);
        }

        async void SignIn(bool create)
        {
            string error = await cloud.SignIn(email.text, password.text, create);
            if (error != null) Toast.Show("Cloud: " + error);
            if (page.activeSelf) Refresh();
        }

        async void ForgotPassword()
        {
            if (string.IsNullOrWhiteSpace(email.text)) { Toast.Show("Type your email first"); return; }
            string error = await cloud.ResetPassword(email.text);
            Toast.Show(error == null ? "Sent a password reset email to " + email.text.Trim() : "Cloud: " + error);
        }

        // ---------- little builders ----------

        void Heading(string text)
        {
            var h = UiKit.Label(list, "Heading", theme, 20f, UiKit.Accent, TextAlignmentOptions.BottomLeft, "<b>" + text + "</b>");
            h.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        TextMeshProUGUI Note(string text)
        {
            var n = UiKit.Label(list, "Note", theme, 16f, UiKit.Text, TextAlignmentOptions.Left, text);
            n.textWrappingMode = TextWrappingModes.Normal;
            n.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
            return n;
        }

        Transform Row()
        {
            var row = UiKit.Row(list, "Row", 42f);
            return row.transform;
        }

        TMP_InputField Field(string label, string value, bool secret)
        {
            var row = UiKit.Row(list, label, 40f);
            var l = UiKit.Label(row.transform, "Label", theme, 17f, UiKit.SubtleText, TextAlignmentOptions.Left, label);
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;
            var box = UiKit.InputBox(row.transform, theme, 420f, true,
                secret ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard, 18f);
            box.text = value ?? "";
            return box;
        }
    }
}
