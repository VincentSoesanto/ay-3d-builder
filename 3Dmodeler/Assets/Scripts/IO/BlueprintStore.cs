using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Builder.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// The blueprint library: one JSON file + one PNG picture per blueprint, in the app's data folder (blueprints/).
    /// Separate from projects, so every project can use every blueprint. Deleted ones go to blueprints/trash.
    /// </summary>
    public static class BlueprintStore
    {
        public static string Folder => Path.Combine(ProjectStore.Root, "blueprints");
        static string TrashFolder => Path.Combine(Folder, "trash");

        static string JsonPath(string id) => Path.Combine(Folder, id + ".json");
        public static string PicturePath(string id) => Path.Combine(Folder, id + ".png");

        public static string ToJson(Blueprint bp) => JsonConvert.SerializeObject(bp, Formatting.None, ProjectStore.Settings);

        public static Blueprint FromJson(string json)
        {
            var root = JObject.Parse(json);
            // Blueprints use the same step-by-step upgrades as projects.
            SchemaMigrator.Upgrade(root, Blueprint.CurrentSchemaVersion);
            return root.ToObject<Blueprint>(JsonSerializer.Create(ProjectStore.Settings));
        }

        /// <summary>Saves the blueprint (and its picture, if given).</summary>
        public static void Save(Blueprint bp, byte[] png = null)
        {
            bp.modifiedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ProjectStore.WriteSafely(JsonPath(bp.id), ToJson(bp));
            if (png != null) File.WriteAllBytes(PicturePath(bp.id), png);
        }

        /// <summary>Every blueprint in the library, sorted by category, then name.</summary>
        public static List<Blueprint> LoadAll()
        {
            var list = new List<Blueprint>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var bp = FromJson(File.ReadAllText(file));
                    if (bp?.id != null) list.Add(bp);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Builder] Skipping unreadable blueprint {file}: {e.Message}");
                }
            }
            return list.OrderBy(b => b.category).ThenBy(b => b.name).ToList();
        }

        // ---------- blueprint files ----------

        public const string FileExtension = ".aybp";

        /// <summary>Where blueprint files are saved and imported from: the Blueprints folder next to the exe.</summary>
        public static string FileFolder
        {
            get
            {
                if (ProjectStore.RootOverride != null) return Path.Combine(ProjectStore.RootOverride, "blueprint-files");
                return Path.Combine(ProjectStore.UserFolder, "Blueprints");
            }
        }

        /// <summary>What a blueprint file holds: the main blueprint, the blueprints it uses, and their pictures.</summary>
        class FileContent
        {
            public string format = FileFormat;
            public int fileVersion = 1;
            public string main;
            public List<Blueprint> blueprints = new List<Blueprint>();
            public Dictionary<string, string> pictures = new Dictionary<string, string>();   // id -> PNG as base64
        }

        const string FileFormat = "AY's 3D Builder blueprint";

        public static List<string> FileCandidates() => Directory.Exists(FileFolder)
            ? Directory.GetFiles(FileFolder, "*" + FileExtension).OrderBy(f => f).ToList()
            : new List<string>();

        /// <summary>Writes the blueprint file (name = the blueprint's name) and returns its path.</summary>
        public static string WriteFile(Blueprint main, List<Blueprint> blueprints)
        {
            var file = new FileContent { main = main.id, blueprints = blueprints };
            foreach (var bp in blueprints)
                if (File.Exists(PicturePath(bp.id))) file.pictures[bp.id] = Convert.ToBase64String(File.ReadAllBytes(PicturePath(bp.id)));
            Directory.CreateDirectory(FileFolder);
            var bad = Path.GetInvalidFileNameChars();
            string safe = string.Concat((main.name ?? "").Select(c => bad.Contains(c) ? '_' : c)).Trim();
            string path = Path.Combine(FileFolder, (safe.Length > 0 ? safe : "Blueprint") + FileExtension);
            ProjectStore.WriteSafely(path, JsonConvert.SerializeObject(file, Formatting.Indented, ProjectStore.Settings));
            return path;
        }

        /// <summary>Reads a blueprint file. Throws with a readable message if it isn't one.</summary>
        public static (string main, List<Blueprint> blueprints, Dictionary<string, byte[]> pictures) ReadFile(string path)
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if ((string)root["format"] != FileFormat || !(root["blueprints"] is JArray list))
                throw new InvalidOperationException("this is not an AY's 3D Builder blueprint file");
            var blueprints = new List<Blueprint>();
            foreach (var item in list)
            {
                var bp = FromJson(item.ToString());
                if (bp?.id != null) blueprints.Add(bp);
            }
            var pictures = new Dictionary<string, byte[]>();
            if (root["pictures"] is JObject pics)
                foreach (var pair in pics)
                    pictures[pair.Key] = Convert.FromBase64String((string)pair.Value);
            return ((string)root["main"], blueprints, pictures);
        }

        /// <summary>Moves the blueprint (and picture) into the trash folder.</summary>
        public static void Delete(string id)
        {
            Directory.CreateDirectory(TrashFolder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (File.Exists(JsonPath(id))) File.Move(JsonPath(id), Path.Combine(TrashFolder, $"{id}_{stamp}.json"));
            if (File.Exists(PicturePath(id))) File.Move(PicturePath(id), Path.Combine(TrashFolder, $"{id}_{stamp}.png"));
        }
    }
}
