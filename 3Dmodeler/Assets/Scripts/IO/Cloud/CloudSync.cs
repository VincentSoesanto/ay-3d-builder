using System;
using System.IO;
using System.Threading.Tasks;
using Builder.UI;
using Newtonsoft.Json;
using UnityEngine;

namespace Builder.IO.Cloud
{
    /// <summary>
    /// Cloud sync for your own devices (Phase 4): projects, blueprints and imported models stay the same on your PC and
    /// phone, through your own free Firebase project. The app works fully without it (local files are the real copy).
    ///   - Sign in once (Esc > Cloud sync); the app remembers you (cloud/user.json in its data folder).
    ///   - Syncs by itself: at start, a few seconds after a save / new blueprint / imported model, and every 5 minutes.
    ///   - The open project is saved first; if the cloud has a newer version of it, it is opened again.
    ///   - Offline: it just tries again later.
    /// </summary>
    public class CloudSync : MonoBehaviour
    {
        const float AfterChangeSeconds = 5f;
        const float EverySeconds = 300f;

        [SerializeField] BuilderSession session;
        [SerializeField] ProjectController projects;
        [SerializeField] BlueprintLibrary blueprints;
        [SerializeField] ModelLibrary models;

        public CloudConfig Config { get; private set; }
        public CloudUser User { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "";
        public DateTime? LastSync { get; private set; }
        public bool SignedIn => User != null && Config.IsSet;

        /// <summary>Raised when the status changes (the Cloud page redraws).</summary>
        public event Action Changed;

        float dueAt = -1f;
        bool ownReload;   // our own reload after a download raises Changed events: don't sync again because of them

        string root;   // the data folder this sync works on (fixed at start)
        string UserFile => Path.Combine(root, "cloud", "user.json");

        /// <summary>False if the data folder changed since start (only in tests): then nothing is synced or written.</summary>
        bool SameFolder => this != null && ProjectStore.Root == root;

        void Start()
        {
            root = ProjectStore.Root;
            Config = CloudConfig.Load();
            try
            {
                if (File.Exists(UserFile)) User = JsonConvert.DeserializeObject<CloudUser>(File.ReadAllText(UserFile));
            }
            catch (Exception) { User = null; }
            SetStatus(!Config.IsSet ? "Not set up yet" : User == null ? "Not signed in" : "Signed in as " + User.email);

            projects.Saved += () => SyncSoon();
            blueprints.Changed += () => { if (!ownReload) SyncSoon(); };
            models.Changed += () => { if (!ownReload) SyncSoon(); };
            if (SignedIn) SyncSoon(2f);
        }

        void Update()
        {
            if (!SignedIn || Busy || !SameFolder) return;
            if (dueAt >= 0f && Time.unscaledTime >= dueAt) SyncNow();
            else if (dueAt < 0f && LastSync != null && (DateTime.UtcNow - LastSync.Value).TotalSeconds > EverySeconds) SyncNow();
        }

        /// <summary>Sync a few seconds from now (changes in a row are sent together).</summary>
        public void SyncSoon(float seconds = AfterChangeSeconds)
        {
            if (SignedIn) dueAt = Time.unscaledTime + seconds;
        }

        void SetStatus(string text)
        {
            Status = text;
            Changed?.Invoke();
        }

        // ---------- setup and account ----------

        public void SaveConfig(string apiKey, string projectId)
        {
            Config.apiKey = apiKey?.Trim();
            Config.projectId = projectId?.Trim();
            Config.Save();
            SignOut();
            SetStatus(Config.IsSet ? "Firebase project set - now sign in or make an account" : "Not set up yet");
        }

        /// <summary>Signs in (or makes a new account). Returns an error message, or null when it worked.</summary>
        public async Task<string> SignIn(string email, string password, bool newAccount)
        {
            if (!Config.IsSet) return "Set up the Firebase project first";
            try
            {
                Busy = true;
                SetStatus(newAccount ? "Making your account..." : "Signing in...");
                var client = new CloudClient(Config);
                User = newAccount ? await client.SignUp(email.Trim(), password) : await client.SignIn(email.Trim(), password);
                if (!SameFolder) return "stopped";
                Directory.CreateDirectory(Path.GetDirectoryName(UserFile));
                ProjectStore.WriteSafely(UserFile, JsonConvert.SerializeObject(User));
                SetStatus("Signed in as " + User.email);
                Busy = false;
                SyncNow();
                return null;
            }
            catch (CloudException e)
            {
                Busy = false;
                SetStatus("Not signed in");
                return e.Message;
            }
        }

        public async Task<string> ResetPassword(string email)
        {
            try
            {
                await new CloudClient(Config).ResetPassword(email.Trim());
                return null;
            }
            catch (CloudException e) { return e.Message; }
        }

        public void SignOut()
        {
            User = null;
            if (File.Exists(UserFile)) File.Delete(UserFile);
            SetStatus(Config.IsSet ? "Not signed in" : "Not set up yet");
        }

        // ---------- syncing ----------

        public async void SyncNow()
        {
            if (!SignedIn || Busy || !SameFolder) return;
            Busy = true;
            dueAt = -1f;
            SetStatus("Syncing...");
            try
            {
                if (session.Dirty) projects.Save(quiet: true);   // the open project goes up as it is now
                var engine = new SyncEngine(new CloudClient(Config), User, SystemInfo.deviceUniqueIdentifier, ProjectStore.Root)
                {
                    IsBusy = (kind, id) => kind == "projects" && id == session.Document.id && session.Dirty,
                };
                var report = await engine.Sync(new ISyncKind[] { new ModelsKind(), new BlueprintsKind(), new ProjectsKind() });
                if (!SameFolder) return;
                ProjectStore.WriteSafely(UserFile, JsonConvert.SerializeObject(User));   // the refresh token may be new
                ApplyDownloads(report);
                LastSync = DateTime.UtcNow;
                SetStatus($"Synced {DateTime.Now:HH:mm} - {report}");
                if (report.up + report.down + report.deleted + report.conflicts > 0) Toast.Show("Cloud: " + report);
            }
            catch (CloudException e)
            {
                LastSync = DateTime.UtcNow;   // try again later (every 5 minutes)
                SetStatus("Not synced: " + e.Message);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                LastSync = DateTime.UtcNow;
                SetStatus("Not synced: " + e.Message);
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>Shows what came down: libraries read again, and the open project opened again if it changed.</summary>
        void ApplyDownloads(SyncReport report)
        {
            if (report.downloaded.Count == 0 && report.deleted == 0) return;
            ownReload = true;
            try
            {
                if (report.downloaded.Exists(d => d.StartsWith("models/"))) models.Reload();
                if (report.downloaded.Exists(d => d.StartsWith("blueprints/")) || report.deleted > 0) blueprints.ReloadAll();
            }
            finally
            {
                ownReload = false;
            }
            string open = session.Document.id;
            if (report.downloaded.Contains("projects/" + open) && !session.Dirty) session.Use(ProjectStore.Load(open));
            else if (!ProjectStore.Exists(open) && !session.Dirty && report.deleted > 0)
            {
                var next = ProjectStore.List();
                session.Use(next.Count > 0 ? ProjectStore.Load(next[0].id) : BuilderSession.NewDocument("Untitled"));
            }
        }
    }
}
