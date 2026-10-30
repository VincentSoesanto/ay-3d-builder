using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Builder.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// Saves and loads projects as JSON files: one file per project, named by its id, in the app's data folder
    /// (projects/). Deleted projects are moved to projects/trash instead of being erased.
    /// Export / import of single projects goes through a folder the user can see:
    /// Documents\AY's 3D Builder\Projects on Windows.
    /// </summary>
    public static class ProjectStore
    {
        /// <summary>Tests point this somewhere else so they never touch the real projects.</summary>
        public static string RootOverride;

        /// <summary>The app's data folder (projects, blueprints, settings live under it).</summary>
        public static string Root => RootOverride ?? Application.persistentDataPath;
        public static string Folder => Path.Combine(Root, "projects");
        public static string TrashFolder => Path.Combine(Folder, "trash");

        /// <summary>
        /// The folder for the files you see and use yourself (Exports, Models, Blueprints, Projects to share):
        /// next to the exe on Windows (e.g. Build/Windows), in Documents\AY's 3D Builder while working in the Unity
        /// editor, and in the app's own folder on a phone. (The app's working data stays in its data folder, Root.)
        /// </summary>
        public static string UserFolder
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Application.productName);
#elif UNITY_STANDALONE_WIN
                return Path.GetDirectoryName(Application.dataPath);   // AY3DBuilder_Data's parent = the exe's folder
#else
                return Application.persistentDataPath;
#endif
            }
        }

        /// <summary>Where "Export .json" writes and "Import" looks.</summary>
        public static string ShareFolder
        {
            get
            {
                if (RootOverride != null) return Path.Combine(RootOverride, "share");
                return Path.Combine(ProjectStore.UserFolder, "Projects");
            }
        }

        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter() },   // enums as words ("Grid"), safer than numbers
        };

        /// <summary>Short facts about a saved project, for the project list.</summary>
        public class Info
        {
            public string id;
            public string name;
            public long modifiedUtc;
            public string path;
        }

        // ---------- JSON ----------

        public static string ToJson(ProjectDocument doc) => JsonConvert.SerializeObject(doc, Formatting.None, Settings);

        /// <summary>Reads a project, upgrading old formats first.</summary>
        public static ProjectDocument FromJson(string json)
        {
            var root = JObject.Parse(json);
            SchemaMigrator.Upgrade(root, ProjectDocument.CurrentSchemaVersion);
            var doc = root.ToObject<ProjectDocument>(JsonSerializer.Create(Settings));
            if (string.IsNullOrEmpty(doc.id)) doc.id = Ids.New();
            if (doc.editor.hotbar == null || doc.editor.hotbar.Length != 9) doc.editor.hotbar = new string[9];
            return doc;
        }

        // ---------- saved projects ----------

        public static string PathOf(string id) => Path.Combine(Folder, id + ".json");
        static string LastFile => Path.Combine(Folder, "last.txt");

        /// <summary>The id of the project that was open last time (opened again at start-up).</summary>
        public static string LastId
        {
            get => File.Exists(LastFile) ? File.ReadAllText(LastFile).Trim() : null;
            set
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(LastFile, value ?? "");
            }
        }

        public static bool Exists(string id) => !string.IsNullOrEmpty(id) && File.Exists(PathOf(id));

        /// <summary>
        /// Saves the project (stamping the time, a new revision and this device). Nothing is written when nothing
        /// changed since the last save (so cloud sync doesn't see a "newer" copy that isn't really new).
        /// </summary>
        /// <returns>True if the file was written (false: nothing had changed).</returns>
        public static bool Save(ProjectDocument doc)
        {
            if (Exists(doc.id))
            {
                try
                {
                    if (File.ReadAllText(PathOf(doc.id)) == ToJson(doc)) return false;
                }
                catch (IOException) { }
            }
            doc.modifiedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            doc.revision++;
            doc.lastDeviceId = SystemInfo.deviceUniqueIdentifier;
            WriteSafely(PathOf(doc.id), ToJson(doc));
            return true;
        }

        public static ProjectDocument Load(string id) => FromJson(File.ReadAllText(PathOf(id)));

        /// <summary>All saved projects, newest first.</summary>
        public static List<Info> List()
        {
            var list = new List<Info>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    // Only the few header fields are needed; the rest of the file is skipped.
                    var info = JsonConvert.DeserializeObject<Info>(File.ReadAllText(file));
                    if (info?.id == null) continue;
                    info.path = file;
                    list.Add(info);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Builder] Skipping unreadable project file {file}: {e.Message}");
                }
            }
            return list.OrderByDescending(i => i.modifiedUtc).ToList();
        }

        /// <summary>Moves the project file into the trash folder (it can be put back by hand).</summary>
        public static void Delete(string id)
        {
            if (!Exists(id)) return;
            Directory.CreateDirectory(TrashFolder);
            string target = Path.Combine(TrashFolder, id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
            File.Move(PathOf(id), target);
        }

        // ---------- export / import ----------

        /// <summary>Writes a copy of the project into the share folder; returns the file path.</summary>
        public static string Export(ProjectDocument doc)
        {
            string path = Path.Combine(ShareFolder, GlbExporter.SafeName(doc.name) + ".json");
            WriteSafely(path, ToJson(doc));
            return path;
        }

        /// <summary>The .json files waiting in the share folder.</summary>
        public static List<string> ImportCandidates() =>
            Directory.Exists(ShareFolder) ? Directory.GetFiles(ShareFolder, "*.json").OrderBy(f => f).ToList() : new List<string>();

        /// <summary>Reads a project file from anywhere and saves it as a NEW project (new id, so nothing is overwritten).</summary>
        public static ProjectDocument Import(string path)
        {
            var doc = FromJson(File.ReadAllText(path));
            doc.id = Ids.New();
            doc.revision = 0;
            Save(doc);
            return doc;
        }

        /// <summary>Writes to a temporary file first, then swaps it in, so a crash can't leave a half-written save.</summary>
        public static void WriteSafely(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
