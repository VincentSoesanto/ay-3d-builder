using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Builder.Core;
using Builder.Shapes;
using GLTFast;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Builder.IO
{
    /// <summary>
    /// Imported 3D models, usable as items in every project (inventory "Models").
    ///   - Import from a .glb / .gltf (read with glTFast), .obj (ObjReader) or 3D-print .stl / .3mf (PrintReader) file.
    ///   - The shape is kept in the app's own simple form (models/&lt;id&gt;.json in the data folder): all its
    ///     triangles, centered, in meters, plus its colors (at most 8, see <see cref="ModelColors"/>).
    ///   - Each model becomes a shape "model:&lt;id&gt;", so placing, snapping, cutting, mirroring and export just work.
    ///   - A model with colors is placed like a library part: one piece per color ("model:&lt;id&gt;.0", ".1" ...), each
    ///     in its own color (and each can still be painted, moved, cut ...).
    /// </summary>
    public class ModelLibrary : MonoBehaviour
    {
        public const int MaxTriangles = 30000;
        public static readonly string[] Extensions = { ".glb", ".gltf", ".obj", ".stl", ".3mf" };

        /// <summary>3D-print files (.stl / .3mf) are scaled so their biggest side is this long (they are made in mm).</summary>
        public const float PrintSize = 1f;

        public static string Folder => Path.Combine(ProjectStore.Root, "models");
        static string TrashFolder => Path.Combine(Folder, "trash");

        /// <summary>Where the files to import go: the Models folder next to the exe.</summary>
        public static string ImportFolder
        {
            get
            {
                if (ProjectStore.RootOverride != null) return Path.Combine(ProjectStore.RootOverride, "import-models");
                return Path.Combine(ProjectStore.UserFolder, "Models");
            }
        }

        /// <summary>Raised when a model was added (the inventory redraws).</summary>
        public event Action Changed;

        static readonly Dictionary<string, Blueprint> colored = new Dictionary<string, Blueprint>();

        /// <summary>For a model item with its own colors ("model:&lt;id&gt;"): its colored pieces, placed like a blueprint. Else null.</summary>
        public static Blueprint ColoredBlueprint(string item) =>
            item != null && colored.TryGetValue(item, out var bp) ? bp : null;

        /// <summary>Makes a model known as a shape (and its colored pieces, if it has colors).</summary>
        public static void Register(ImportedModel model, bool listed = true)
        {
            ShapeRegistry.RegisterModel(new ModelShape(model), listed);
            string item = ModelShape.Prefix + model.id;
            colored.Remove(item);
            int count = model.colors?.Length ?? 0;
            if (count == 0 || model.triColor == null || model.triColor.Length * 3 != model.triangles.Length) return;

            var bp = new Blueprint { id = item, name = model.name, version = 1 };
            for (int k = 0; k < count; k++)
            {
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var index = new Dictionary<int, int>();
                for (int t = 0; t < model.triColor.Length; t++)
                {
                    if (model.triColor[t] != k) continue;
                    for (int c = 0; c < 3; c++)
                    {
                        int v = model.triangles[t * 3 + c];
                        if (!index.TryGetValue(v, out int n))
                        {
                            index[v] = n = vertices.Count;
                            vertices.Add(new Vector3(model.vertices[v * 3], model.vertices[v * 3 + 1], model.vertices[v * 3 + 2]));
                        }
                        triangles.Add(n);
                    }
                }
                if (triangles.Count == 0) continue;
                var box = new Bounds(vertices[0], Vector3.zero);
                foreach (var v in vertices) box.Encapsulate(v);
                var part = FromTriangles(model.name, vertices, triangles);
                part.id = model.id + "." + k;
                part.sourceFile = model.sourceFile;
                ShapeRegistry.RegisterModel(new ModelShape(part), listed: false);
                bp.content.pieces.Add(new Piece
                {
                    id = "p" + k, shape = ModelShape.Prefix + part.id, color = model.colors[k],
                    position = new Vec3(box.center.x, box.center.y, box.center.z),
                    p = new ShapeParams { width = part.size.x, height = part.size.y, length = part.size.z },
                });
            }
            if (bp.content.pieces.Count > 0) colored[item] = bp;
        }

        void Awake() => LoadAll();

        /// <summary>Reads all models again (e.g. after cloud sync brought new ones).</summary>
        public void Reload() => LoadAll();

        void LoadAll()
        {
            if (!Directory.Exists(Folder)) return;
            // Deleted models (in the trash) are loaded too, but not listed: pieces made from them still show.
            var files = Directory.GetFiles(Folder, "*.json").Select(f => (f, true));
            if (Directory.Exists(TrashFolder)) files = Directory.GetFiles(TrashFolder, "*.json").Select(f => (f, false)).Concat(files);
            foreach (var (file, listed) in files)
            {
                try
                {
                    var model = JsonConvert.DeserializeObject<ImportedModel>(File.ReadAllText(file), ProjectStore.Settings);
                    if (model?.id != null) Register(model, listed);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Builder] Skipping unreadable model {file}: {e.Message}");
                }
            }
            Changed?.Invoke();
        }

        /// <summary>The 3D files waiting in the import folder.</summary>
        public static List<string> ImportCandidates() => Directory.Exists(ImportFolder)
            ? Directory.GetFiles(ImportFolder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList()
            : new List<string>();

        /// <summary>Imports a 3D file and adds it to the library. Throws with a readable message if it can't.</summary>
        public async Task<ImportedModel> Import(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string folder = Path.GetDirectoryName(path);
            (List<Vector3> vertices, List<int> triangles, List<Color32> colors) shape;
            if (ext == ".obj") shape = ObjReader.ReadColored(File.ReadAllText(path), name => File.ReadAllText(Path.Combine(folder, name)));
            else if (ext == ".stl") { var (v, t) = PrintReader.ReadStl(File.ReadAllBytes(path)); shape = (v, t, null); }
            else if (ext == ".3mf") shape = PrintReader.Read3mf(File.ReadAllBytes(path));
            else shape = await ReadGltf(path);
            if ((ext == ".stl" || ext == ".3mf") && shape.vertices.Count > 0)
            {
                // Print files are in millimeters and often tiny or huge for a mech: biggest side = 1 m (resize with E).
                var box = new Bounds(shape.vertices[0], Vector3.zero);
                foreach (var v in shape.vertices) box.Encapsulate(v);
                float biggest = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));
                if (biggest > 1e-6f)
                    for (int i = 0; i < shape.vertices.Count; i++) shape.vertices[i] *= PrintSize / biggest;
            }

            if (shape.triangles.Count == 0) throw new InvalidOperationException("the file has no triangles");
            if (shape.triangles.Count / 3 > MaxTriangles)
                throw new InvalidOperationException($"too detailed ({shape.triangles.Count / 3} triangles, the limit is {MaxTriangles})");

            var model = FromTriangles(Path.GetFileNameWithoutExtension(path), shape.vertices, shape.triangles);
            model.sourceFile = Path.GetFileName(path);
            if (shape.colors != null && shape.colors.Count * 3 == shape.triangles.Count)
                (model.colors, model.triColor) = ModelColors.Merge(shape.colors);
            Directory.CreateDirectory(Folder);
            ProjectStore.WriteSafely(Path.Combine(Folder, model.id + ".json"), JsonConvert.SerializeObject(model, ProjectStore.Settings));
            Register(model);
            Changed?.Invoke();
            return model;
        }

        static string JsonPath(string id) => Path.Combine(Folder, id + ".json");

        static ImportedModel Find(string id) =>
            ShapeRegistry.Models.Select(s => ((ModelShape)s).Model).FirstOrDefault(m => m.id == id);

        /// <summary>Gives an imported model a new name.</summary>
        public void Rename(string id, string name)
        {
            var model = Find(id);
            if (model == null || string.IsNullOrWhiteSpace(name)) return;
            model.name = name.Trim();
            ProjectStore.WriteSafely(JsonPath(id), JsonConvert.SerializeObject(model, ProjectStore.Settings));
            Register(model);   // its colored pieces get the new name too
            Changed?.Invoke();
        }

        /// <summary>Removes a model from the inventory (its file goes to models/trash; pieces already placed still work).</summary>
        public void Delete(string id)
        {
            Directory.CreateDirectory(TrashFolder);
            string trashed = Path.Combine(TrashFolder, id + ".json");
            if (File.Exists(JsonPath(id)))
            {
                if (File.Exists(trashed)) File.Delete(trashed);   // an older deleted copy of the same model
                File.Move(JsonPath(id), trashed);
                File.SetLastWriteTimeUtc(trashed, DateTime.UtcNow);   // a change (cloud sync passes it on)
            }
            ShapeRegistry.UnlistModel(ModelShape.Prefix + id);
            Changed?.Invoke();
        }

        /// <summary>Centers the triangles on (0, 0, 0) and measures their size.</summary>
        public static ImportedModel FromTriangles(string name, List<Vector3> vertices, List<int> triangles)
        {
            var box = new Bounds(vertices[0], Vector3.zero);
            foreach (var v in vertices) box.Encapsulate(v);
            var flat = new float[vertices.Count * 3];
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i] - box.center;
                flat[i * 3] = v.x;
                flat[i * 3 + 1] = v.y;
                flat[i * 3 + 2] = v.z;
            }
            var size = Vector3.Max(box.size, Vector3.one * 0.001f);
            return new ImportedModel
            {
                id = Ids.New(), name = name, size = new Vec3(size.x, size.y, size.z),
                vertices = flat, triangles = triangles.ToArray(),
            };
        }

        /// <summary>
        /// Loads a glTF file with glTFast and collects all its triangles (in the model's own space) with their colors
        /// (material color x texture x vertex color).
        /// </summary>
        static async Task<(List<Vector3>, List<int>, List<Color32>)> ReadGltf(string path)
        {
            var gltf = new GltfImport();
            var root = new GameObject("Model Import");
            try
            {
                // The file's full address lets glTFast find textures kept in separate files (e.g. Kenney models).
                if (!await gltf.LoadFile(path, new Uri(Path.GetFullPath(path)))) throw new InvalidOperationException("glTFast could not read the file");
                await gltf.InstantiateMainSceneAsync(root.transform);

                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var colors = new List<Color32>();
                var pictures = new Dictionary<Texture, (Color32[] pixels, int w, int h)>();
                void Add(Mesh mesh, Transform t, Material[] materials)
                {
                    if (mesh == null) return;
                    var toRoot = root.transform.worldToLocalMatrix * t.localToWorldMatrix;
                    int start = vertices.Count;
                    foreach (var v in mesh.vertices) vertices.Add(toRoot.MultiplyPoint3x4(v));
                    bool mirrored = toRoot.determinant < 0f;   // a mirrored part: keep its faces pointing out
                    var uv = mesh.uv;
                    var vertexColors = mesh.colors32;
                    bool hasVertexColors = vertexColors.Length == mesh.vertexCount;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var material = materials.Length > 0 ? materials[Mathf.Min(s, materials.Length - 1)] : null;
                        Color tint = ModelColors.MaterialColor(material);
                        var texture = ModelColors.MaterialTexture(material);
                        (Color32[] pixels, int w, int h) picture = default;
                        if (texture != null && uv.Length == mesh.vertexCount && !pictures.TryGetValue(texture, out picture))
                        {
                            var px = ModelColors.Pixels(texture, out int w, out int h);
                            pictures[texture] = picture = (px, w, h);
                        }
                        var tris = mesh.GetTriangles(s);
                        for (int i = 0; i + 2 < tris.Length; i += 3)
                        {
                            triangles.Add(start + tris[i]);
                            triangles.Add(start + (mirrored ? tris[i + 2] : tris[i + 1]));
                            triangles.Add(start + (mirrored ? tris[i + 1] : tris[i + 2]));
                            Color c = tint;
                            if (picture.pixels != null)
                            {
                                // The texture's color in the middle of the triangle and at its corners, averaged.
                                Vector2 a = uv[tris[i]], b = uv[tris[i + 1]], d = uv[tris[i + 2]];
                                Color sum = ModelColors.Sample(picture.pixels, picture.w, picture.h, (a + b + d) / 3f)
                                          + ModelColors.Sample(picture.pixels, picture.w, picture.h, a)
                                          + ModelColors.Sample(picture.pixels, picture.w, picture.h, b)
                                          + ModelColors.Sample(picture.pixels, picture.w, picture.h, d);
                                c *= sum / 4f;
                            }
                            if (hasVertexColors)
                                c *= ((Color)vertexColors[tris[i]] + vertexColors[tris[i + 1]] + vertexColors[tris[i + 2]]) / 3f;
                            c.a = 1f;
                            colors.Add(c);
                        }
                    }
                }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    Add(mf.sharedMesh, mf.transform, mf.GetComponent<Renderer>()?.sharedMaterials ?? new Material[0]);
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    Add(smr.sharedMesh, smr.transform, smr.sharedMaterials);
                return (vertices, triangles, colors);
            }
            finally
            {
                Object.Destroy(root);
                gltf.Dispose();
            }
        }
    }
}
