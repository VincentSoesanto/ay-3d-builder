using System;
using System.IO;
using System.Linq;
using Builder.Core;
using Builder.Input;
using Builder.UI;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// Keeps the open project saved and switches projects:
    ///   - Ctrl+S saves now; it also saves by itself every minute when something changed, and when the app closes
    ///     (or, on a phone, goes to the background).
    ///   - New / Open / Rename / Delete / Export .json / Import .json (used by the Projects page in the menu).
    /// Opening another project first saves the current one, so nothing is lost.
    /// </summary>
    public class ProjectController : MonoBehaviour
    {
        const float AutosaveSeconds = 60f;

        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;

        float lastSaveTime;

        /// <summary>Raised after the project file was really written (cloud sync uploads it soon after).</summary>
        public event Action Saved;

        ProjectDocument Doc => session.Document;

        void Update()
        {
            if (input.Pressed(input.Save)) Save();
            if (session.Dirty && Time.unscaledTime - lastSaveTime > AutosaveSeconds) Save(quiet: true, autosave: true);
        }

        // Save when the app closes, or (phones) when it goes to the background.
        void OnApplicationQuit() => SaveOnExit();
        void OnApplicationPause(bool paused) { if (paused) SaveOnExit(); }

        void SaveOnExit()
        {
            // Also keeps settings like grid size / hotbar of a project that was saved before.
            if (session.Dirty || ProjectStore.Exists(Doc.id)) Save(quiet: true);
        }

        /// <summary>Saves the open project. Returns false (and says why) if it failed.</summary>
        public bool Save(bool quiet = false, bool autosave = false)
        {
            lastSaveTime = Time.unscaledTime;
            try
            {
                bool written = ProjectStore.Save(Doc);
                ProjectStore.LastId = Doc.id;
                if (written) Saved?.Invoke();
                session.MarkSaved();
                if (!quiet) Toast.Show("Saved  " + Doc.name);
                else if (autosave) Toast.Show("Autosaved");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Could not save: " + e.Message);
                return false;
            }
        }

        public void New()
        {
            SaveIfChanged();
            session.Use(BuilderSession.NewDocument(NextFreeName()));
            Save(quiet: true);
            Toast.Show("New project  " + Doc.name);
        }

        public void Open(string id)
        {
            if (id == Doc.id) return;
            ProjectDocument doc;
            try
            {
                doc = ProjectStore.Load(id);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Could not open it: " + e.Message);
                return;
            }
            SaveIfChanged();
            session.Use(doc);
            ProjectStore.LastId = id;
            Toast.Show("Opened  " + doc.name);
        }

        public void Rename(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == Doc.name) return;
            Doc.name = name.Trim();
            Save(quiet: true);
            Toast.Show("Renamed to  " + Doc.name);
        }

        /// <summary>Moves a project to the trash folder. Deleting the open project opens the newest other one (or a new one).</summary>
        public void Delete(string id)
        {
            ProjectStore.Delete(id);
            if (id == Doc.id)
            {
                var next = ProjectStore.List().FirstOrDefault();
                if (next != null) { session.Use(ProjectStore.Load(next.id)); ProjectStore.LastId = next.id; }
                else session.Use(BuilderSession.NewDocument("Untitled"));
            }
            Toast.Show("Moved to the trash folder");
        }

        public void ExportJson()
        {
            Save(quiet: true);
            Toast.Show("Exported:  " + ProjectStore.Export(Doc));
        }

        public void ImportJson(string path)
        {
            try
            {
                var doc = ProjectStore.Import(path);
                SaveIfChanged();
                session.Use(doc);
                ProjectStore.LastId = doc.id;
                Toast.Show("Imported  " + doc.name);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast.Show("Could not import " + Path.GetFileName(path) + ": " + e.Message);
            }
        }

        void SaveIfChanged()
        {
            if (session.Dirty) Save(quiet: true);
        }

        /// <summary>"Mech 1", "Mech 2" ... the first name not used yet.</summary>
        static string NextFreeName()
        {
            var names = ProjectStore.List().Select(p => p.name).ToHashSet();
            int n = 1;
            while (names.Contains("Mech " + n)) n++;
            return "Mech " + n;
        }
    }
}
