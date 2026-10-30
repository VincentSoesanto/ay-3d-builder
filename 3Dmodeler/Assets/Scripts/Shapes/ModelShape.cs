using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// An imported 3D model, saved in the app's own simple form: corner points (centered, in meters) + triangles.
    /// </summary>
    public class ImportedModel
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string id;
        public string name;
        public string sourceFile;     // the file it was imported from (just for information)
        public Vec3 size;             // the model's size in meters
        public float[] vertices;      // x, y, z, x, y, z ... (centered on 0, 0, 0)
        public int[] triangles;       // three corner numbers per triangle
        public string[] colors;       // the model's own colors ("#RRGGBB"), or null (one color, painted in the app)
        public byte[] triColor;       // per triangle: which of the colors it has
    }

    /// <summary>
    /// An imported model used as a shape (shape id "model:" + its id). Width / height / length stretch it,
    /// starting at the model's own size. Flat shaded like everything else.
    /// </summary>
    public class ModelShape : IShape
    {
        public const string Prefix = "model:";

        readonly ImportedModel model;

        public ModelShape(ImportedModel model) => this.model = model;

        public string Id => Prefix + model.id;
        public string DisplayName => model.name;
        public ShapeFeatures Features => ShapeFeatures.None;
        public ImportedModel Model => model;

        public ShapeParams CreateDefaults() => new ShapeParams { width = model.size.x, height = model.size.y, length = model.size.z };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            var v = model.vertices;
            var t = model.triangles;
            var stretch = new Vector3(p.width / Mathf.Max(model.size.x, 1e-4f), p.height / Mathf.Max(model.size.y, 1e-4f),
                                      p.length / Mathf.Max(model.size.z, 1e-4f));
            Vector3 Corner(int i) => Vector3.Scale(new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]), stretch);
            for (int i = 0; i + 2 < t.Length; i += 3)
                mb.AddTriangle(Corner(t[i]), Corner(t[i + 1]), Corner(t[i + 2]));
        }
    }
}
