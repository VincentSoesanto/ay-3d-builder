using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using Newtonsoft.Json;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>One ready-made part in the built-in library (e.g. "Gear", "Tank 2", "Blaster c").</summary>
    public class LibraryItem
    {
        public string id, name, tab, category;
        public string pack;          // which pack it came from (for the credits)
        public Blueprint blueprint;  // its colored pieces, placed like a blueprint (not linked)
    }

    /// <summary>
    /// The built-in part library: ready-made parts in the inventory tabs Mech parts / Tank parts / Aircraft parts /
    /// Car parts / Weapons / Space. Read once from StreamingAssets/Library/*.json (made by tools/library/build_library.py).
    ///   - Each item is a few colored pieces (one per color), each piece an imported-model shape, so it is placed
    ///     like a blueprint and every piece can be moved, resized, painted, cut and exported like any other piece.
    ///   - Hotbar item = "lib:" + item id. Placed copies are normal pieces (not linked to the library).
    ///   - Sources: AY mech parts (made for this app) and free CC0 models (Kenney, Quaternius ...; see CREDITS.md).
    /// </summary>
    public static class PartLibrary
    {
        public const string Prefix = "lib:";
        public static readonly string[] Tabs = { "Mech parts", "Tank parts", "Aircraft parts", "Car parts", "Weapons", "Space" };

        static readonly List<LibraryItem> items = new List<LibraryItem>();
        static readonly Dictionary<string, LibraryItem> byId = new Dictionary<string, LibraryItem>();
        static bool loaded;

        public static IReadOnlyList<LibraryItem> Items { get { Load(); return items; } }

        public static bool IsLibraryItem(string item) => item != null && item.StartsWith(Prefix, StringComparison.Ordinal);
        public static string ItemOf(LibraryItem it) => Prefix + it.id;

        public static LibraryItem Get(string id) { Load(); return id != null && byId.TryGetValue(id, out var it) ? it : null; }
        public static LibraryItem FromItem(string item) => IsLibraryItem(item) ? Get(item.Substring(Prefix.Length)) : null;

        public static string Folder => Path.Combine(Application.streamingAssetsPath, "Library");

        class PackFile
        {
            public string pack, author, license;
            public List<ItemFile> items;
        }

        class ItemFile
        {
            public string id, name, tab, category;
            public List<PartFile> parts;
        }

        class PartFile
        {
            public string color;
            public float[] v;
            public int[] t;
        }

        /// <summary>Reads the library (once). Safe to call many times.</summary>
        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // On Android the files are packed inside the app (a "jar:" address): read the list from index.txt.
            bool packed = Folder.Contains("://");
            string[] files;
            try
            {
                files = packed
                    ? ReadText(Folder + "/index.txt").Split('\n').Select(l => l.Trim()).Where(l => l.EndsWith(".json")).Select(l => Folder + "/" + l).ToArray()
                    : Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json").OrderBy(f => f).ToArray() : new string[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Builder] No part library at " + Folder + ": " + e.Message);
                return;
            }
            foreach (var file in files)
            {
                try
                {
                    var pack = JsonConvert.DeserializeObject<PackFile>(ReadText(file));
                    foreach (var it in pack?.items ?? new List<ItemFile>()) Add(it, pack.pack);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Builder] Skipping part library file {file}: {e.Message}");
                }
            }
            Debug.Log($"[Builder] Part library: {items.Count} parts loaded in {clock.ElapsedMilliseconds} ms");
        }

        static void Add(ItemFile it, string pack)
        {
            if (it?.id == null || byId.ContainsKey(it.id) || it.parts == null) return;
            var bp = new Blueprint { id = Prefix + it.id, name = it.name, category = it.category, version = 1 };
            for (int i = 0; i < it.parts.Count; i++)
            {
                var part = it.parts[i];
                if (part.v == null || part.t == null || part.v.Length < 9 || part.t.Length < 3) continue;
                var vertices = new List<Vector3>(part.v.Length / 3);
                for (int k = 0; k + 2 < part.v.Length; k += 3) vertices.Add(new Vector3(part.v[k], part.v[k + 1], part.v[k + 2]));
                var box = new Bounds(vertices[0], Vector3.zero);
                foreach (var v in vertices) box.Encapsulate(v);

                var model = ModelLibrary.FromTriangles(it.name, vertices, part.t.ToList());
                model.id = $"lib.{it.id}.{i}";
                model.sourceFile = pack;
                ShapeRegistry.RegisterModel(new ModelShape(model), listed: false);
                bp.content.pieces.Add(new Piece
                {
                    id = "p" + i, shape = ModelShape.Prefix + model.id, color = part.color ?? "#B0B0B0",
                    position = box.center.ToDocVec(),
                    p = new ShapeParams { width = model.size.x, height = model.size.y, length = model.size.z },
                });
            }
            if (bp.content.pieces.Count == 0) return;
            var item = new LibraryItem { id = it.id, name = it.name, tab = it.tab, category = it.category, pack = pack, blueprint = bp };
            items.Add(item);
            byId[it.id] = item;
        }

        static Vec3 ToDocVec(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>Reads a text file: a normal file, or (Android) one packed inside the app.</summary>
        static string ReadText(string path)
        {
            if (!path.Contains("://")) return File.ReadAllText(path);
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(path))
            {
                var op = request.SendWebRequest();
                while (!op.isDone) { }   // a file inside the app: fast, and needed right at start-up
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success) throw new IOException(request.error);
                return request.downloadHandler.text;
            }
        }
    }
}
