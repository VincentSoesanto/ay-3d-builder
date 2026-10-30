using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Builder.Core;
using Builder.Shapes;
using Builder.View;
using UnityEngine;

namespace Builder.IO
{
    /// <summary>
    /// Exports the model as a Wavefront .obj (+ .mtl with the colors), and a .zip with both - the format Mixamo
    /// (mixamo.com) and most 3D tools accept. One object per group ("Ungrouped" for the rest), one material per color;
    /// metal and glow are written as shine / emission. Helpers (points, the human, bone tubes) are left out.
    /// Bones are NOT in an .obj (the format has no skeleton) - use the .glb for that.
    /// The .obj world is right-handed (like Blender), so X is flipped and the faces are turned to match.
    /// </summary>
    public static class ObjExporter
    {
        static readonly CultureInfo Num = CultureInfo.InvariantCulture;

        /// <summary>Writes &lt;name&gt;.obj, &lt;name&gt;.mtl and &lt;name&gt;.zip into <paramref name="folder"/>. Returns the .zip path.</summary>
        public static string Export(ProjectDocument doc, string folder, string baseName)
        {
            Directory.CreateDirectory(folder);
            string objPath = Path.Combine(folder, baseName + ".obj");
            string mtlPath = Path.Combine(folder, baseName + ".mtl");
            string zipPath = Path.Combine(folder, baseName + ".zip");

            var content = doc.content;
            var pieces = content.pieces.Where(p => !ShapeRegistry.IsHelper(p.shape)).ToList();
            var colors = new List<string>();
            var obj = new StringBuilder();
            obj.AppendLine("# AY's 3D Builder - " + doc.name);
            obj.AppendLine("mtllib " + baseName + ".mtl");
            int vBase = 1, nBase = 1;

            foreach (var part in pieces.GroupBy(p => content.FindGroup(p.groupId)?.name ?? "Ungrouped"))
            {
                obj.AppendLine("o " + Clean(part.Key));
                foreach (var p in part)
                {
                    var mesh = PaintedMesh.Get(p, out var pieceColors);
                    var m = Matrix4x4.TRS(p.position.ToUnity(), p.rotation.ToUnity(), Vector3.one);
                    var verts = mesh.vertices;
                    var normals = mesh.normals;
                    foreach (var v in verts)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        obj.Append("v ").Append(F(-w.x)).Append(' ').Append(F(w.y)).Append(' ').Append(F(w.z)).Append('\n');
                    }
                    foreach (var n in normals)
                    {
                        var w = m.MultiplyVector(n).normalized;
                        obj.Append("vn ").Append(F(-w.x)).Append(' ').Append(F(w.y)).Append(' ').Append(F(w.z)).Append('\n');
                    }
                    for (int s = 0; s < mesh.subMeshCount && s < pieceColors.Length; s++)
                    {
                        var tris = mesh.GetTriangles(s);
                        if (tris.Length == 0) continue;
                        string color = pieceColors[s].ToUpperInvariant();
                        if (!colors.Contains(color)) colors.Add(color);
                        obj.AppendLine("usemtl " + MaterialName(color));
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            // Flipping X turns the faces inside out, so list the corners the other way round.
                            int a = tris[t], b = tris[t + 2], c = tris[t + 1];
                            bool hasNormals = normals.Length == verts.Length;
                            obj.Append("f ").Append(Corner(a, vBase, nBase, hasNormals)).Append(' ')
                               .Append(Corner(b, vBase, nBase, hasNormals)).Append(' ')
                               .Append(Corner(c, vBase, nBase, hasNormals)).Append('\n');
                        }
                    }
                    vBase += verts.Length;
                    nBase += normals.Length;
                }
            }

            var mtl = new StringBuilder("# AY's 3D Builder colors\n");
            foreach (var color in colors)
            {
                var c = Conversions.HexToColor(color);
                mtl.AppendLine("newmtl " + MaterialName(color));
                mtl.AppendLine($"Kd {F(c.r)} {F(c.g)} {F(c.b)}");
                mtl.AppendLine($"Ka {F(c.r * 0.2f)} {F(c.g * 0.2f)} {F(c.b * 0.2f)}");
                switch (ColorFinish.Of(color))
                {
                    case ColorFinish.Finish.Metal:
                        mtl.AppendLine("Ks 0.8 0.8 0.8");
                        mtl.AppendLine("Ns 200");
                        break;
                    case ColorFinish.Finish.Glow:
                        mtl.AppendLine($"Ke {F(c.r)} {F(c.g)} {F(c.b)}");
                        mtl.AppendLine("Ks 0.1 0.1 0.1");
                        mtl.AppendLine("Ns 20");
                        break;
                    default:
                        mtl.AppendLine("Ks 0.1 0.1 0.1");
                        mtl.AppendLine("Ns 20");
                        break;
                }
                mtl.AppendLine("illum 2");
            }

            File.WriteAllText(objPath, obj.ToString());
            File.WriteAllText(mtlPath, mtl.ToString());
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var zip = new ZipArchive(File.Create(zipPath), ZipArchiveMode.Create))
            {
                foreach (var file in new[] { objPath, mtlPath })
                {
                    var entry = zip.CreateEntry(Path.GetFileName(file));
                    using (var stream = entry.Open())
                    {
                        var bytes = File.ReadAllBytes(file);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
            }
            return zipPath;
        }

        static string Corner(int index, int vBase, int nBase, bool normals) =>
            normals ? $"{index + vBase}//{index + nBase}" : (index + vBase).ToString(Num);

        static string MaterialName(string color) => "color_" + color.TrimStart('#').Replace('/', '_');

        static string F(float v) => v.ToString("0.#####", Num);

        static string Clean(string name) => string.Concat((name ?? "part").Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' ? ch : '_'));
    }
}
