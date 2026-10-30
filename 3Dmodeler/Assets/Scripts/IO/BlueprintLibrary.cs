using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Builder.Core;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// The blueprint library while the app runs: all blueprints (loaded at start) and their pictures.
    /// A hotbar slot holds a blueprint as "bp:" + its id (a plain shape is just its shape id, e.g. "box").
    /// Saving a blueprint again also updates its linked copies: inside other blueprints (which then update theirs)
    /// and in the open project (projects opened later update when they open). See <see cref="LinkedCopies"/>.
    /// </summary>
    public class BlueprintLibrary : MonoBehaviour
    {
        public const string Prefix = "bp:";

        [SerializeField] BuilderSession session;
        [SerializeField] UI.ShapeIcons icons;
        bool updateProject;

        List<Blueprint> all = new List<Blueprint>();
        readonly Dictionary<string, Texture2D> pictures = new Dictionary<string, Texture2D>();

        /// <summary>Raised when blueprints were added, changed or deleted (the inventory redraws).</summary>
        public event Action Changed;

        public IReadOnlyList<Blueprint> All => all;

        public static bool IsBlueprintItem(string item) => item != null && item.StartsWith(Prefix, StringComparison.Ordinal);
        public static string IdOf(string item) => item.Substring(Prefix.Length);
        public static string ItemOf(Blueprint bp) => Prefix + bp.id;

        void Awake()
        {
            PartLibrary.Load();   // the built-in parts (their shapes must be known before projects are drawn)
            Reload();
        }

        void Start()
        {
            if (UpdateBlueprints() > 0) Reload();
            if (session == null) return;
            session.Replaced += () => updateProject = true;   // a project was opened: update its copies (next frame)
            updateProject = true;
        }

        void Update()
        {
            if (!updateProject) return;
            updateProject = false;
            UpdateProject();
        }

        /// <summary>Rebuilds out-of-date linked copies inside blueprints, level by level. Returns how many blueprints changed.</summary>
        int UpdateBlueprints()
        {
            int changed = 0;
            for (int round = 0; round < 32; round++)
            {
                bool any = false;
                foreach (var bp in all)
                {
                    if (LinkedCopies.RebuildInPlace(bp.content, Get) == 0) continue;
                    bp.version++;   // its own copies update in the next round
                    var picture = icons != null ? icons.RenderPieces(bp.content.pieces) : null;
                    BlueprintStore.Save(bp, picture != null ? picture.EncodeToPNG() : null);
                    if (picture != null) Destroy(picture);
                    pictures.Remove(bp.id);
                    changed++;
                    any = true;
                }
                if (!any) break;
            }
            return changed;
        }

        /// <summary>Rebuilds out-of-date linked copies in the open project (one undo step). Returns how many.</summary>
        public int UpdateProject()
        {
            if (session == null || session.Document == null) return 0;
            var (remove, add, copies) = LinkedCopies.Rebuild(session.Document.content, Get, out var bonesBefore, out var bonesAfter);
            if (copies == 0) return 0;
            session.UpdateCopies(remove, add, "Update blueprint copies", bonesBefore, bonesAfter);
            UI.Toast.Show(copies == 1 ? "Updated 1 blueprint copy" : $"Updated {copies} blueprint copies");
            return copies;
        }

        /// <summary>Reads everything again, pictures too (e.g. after cloud sync brought new versions).</summary>
        public void ReloadAll()
        {
            pictures.Clear();
            Reload();
            if (UpdateBlueprints() > 0) Reload();
            updateProject = true;
        }

        public void Reload()
        {
            all = BlueprintStore.LoadAll();
            Changed?.Invoke();
        }

        public Blueprint Get(string id) => id == null ? null : all.Find(b => b.id == id);

        /// <summary>
        /// The blueprint a hotbar item points at: one of yours ("bp:"), or a built-in library part ("lib:"),
        /// or null (plain shape, or a deleted blueprint).
        /// </summary>
        public Blueprint FromItem(string item) =>
            IsBlueprintItem(item) ? Get(IdOf(item)) : PartLibrary.FromItem(item)?.blueprint ?? ModelLibrary.ColoredBlueprint(item);

        /// <summary>The blueprint's picture (loaded once), or null if it has none.</summary>
        public Texture2D Picture(string id)
        {
            if (pictures.TryGetValue(id, out var tex)) return tex;
            string path = BlueprintStore.PicturePath(id);
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2) { name = "Blueprint " + id };
                if (!tex.LoadImage(File.ReadAllBytes(path))) tex = null;
            }
            pictures[id] = tex;
            return tex;
        }

        /// <summary>Adds (or replaces) a blueprint in the library.</summary>
        public void Add(Blueprint bp, byte[] png)
        {
            BlueprintStore.Save(bp, png);
            pictures.Remove(bp.id);
            all = BlueprintStore.LoadAll();
            UpdateBlueprints();     // blueprints that contain this one
            Reload();
            updateProject = true;   // copies in the open project
        }

        /// <summary>
        /// Moves a blueprint to the trash. Copies already placed keep their pieces (they just stop updating);
        /// blueprints that use it keep their copy of it too.
        /// </summary>
        public void Delete(string id)
        {
            BlueprintStore.Delete(id);
            pictures.Remove(id);
            Reload();
        }

        /// <summary>Gives a blueprint a new name (its copies don't change).</summary>
        public void Rename(string id, string name)
        {
            var bp = Get(id);
            if (bp == null || string.IsNullOrWhiteSpace(name)) return;
            bp.name = name.Trim();
            BlueprintStore.Save(bp);
            Reload();
        }

        // ---------- blueprint files (to share or back up) ----------

        /// <summary>
        /// Saves the blueprint as one file in the Blueprints folder (with every blueprint it uses inside it, and the
        /// pictures). Returns the file's path.
        /// </summary>
        public string ExportFile(string id)
        {
            var bp = Get(id);
            if (bp == null) return null;
            var used = new List<Blueprint> { bp };
            for (int i = 0; i < used.Count; i++)
                foreach (var inner in used[i].content.pieces.Where(p => p.link != null).Select(p => Get(p.link.blueprintId)))
                    if (inner != null && !used.Contains(inner)) used.Add(inner);
            return BlueprintStore.WriteFile(bp, used);
        }

        /// <summary>
        /// Adds the blueprints in a blueprint file to the library. One that is already in the library (same blueprint,
        /// e.g. imported before) is replaced, and its copies update. Returns the main blueprint.
        /// </summary>
        public Blueprint ImportFile(string path)
        {
            var (main, blueprints, pngs) = BlueprintStore.ReadFile(path);
            foreach (var bp in blueprints)
            {
                var existing = Get(bp.id);
                if (existing != null) bp.version = System.Math.Max(bp.version, existing.version + 1);   // copies notice the change
                pngs.TryGetValue(bp.id, out var png);
                BlueprintStore.Save(bp, png);
                pictures.Remove(bp.id);
            }
            all = BlueprintStore.LoadAll();
            UpdateBlueprints();
            Reload();
            updateProject = true;
            return Get(main);
        }
    }
}
