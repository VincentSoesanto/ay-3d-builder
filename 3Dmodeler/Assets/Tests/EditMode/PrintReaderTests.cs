using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Builder.Core;
using Builder.IO;
using Builder.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>3D-print files (.stl, .3mf from MakerWorld / Printables ...) become models.</summary>
    public class PrintReaderTests
    {
        /// <summary>A box in print space (Z up), faces counter-clockwise seen from outside (the STL / 3MF rule).</summary>
        static List<Vector3[]> Box(Vector3 size)
        {
            var h = size * 0.5f;
            Vector3 P(int x, int y, int z) => new Vector3(x * h.x, y * h.y, z * h.z);
            var quads = new[]
            {
                new[] { P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) },      // top (+Z)
                new[] { P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1) },  // bottom
                new[] { P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1) },      // +X
                new[] { P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1) },  // -X
                new[] { P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1) },      // +Y
                new[] { P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1) },  // -Y
            };
            var tris = new List<Vector3[]>();
            foreach (var q in quads)
            {
                tris.Add(new[] { q[0], q[1], q[2] });
                tris.Add(new[] { q[0], q[2], q[3] });
            }
            return tris;
        }

        static byte[] BinaryStl(List<Vector3[]> tris)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new byte[80]);
                w.Write((uint)tris.Count);
                foreach (var t in tris)
                {
                    w.Write(0f); w.Write(0f); w.Write(0f);
                    foreach (var v in t) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
                    w.Write((ushort)0);
                }
                return ms.ToArray();
            }
        }

        static string TextStl(List<Vector3[]> tris)
        {
            var sb = new StringBuilder("solid box\n");
            foreach (var t in tris)
            {
                sb.Append("facet normal 0 0 0\nouter loop\n");
                foreach (var v in t) sb.Append(System.FormattableString.Invariant($"vertex {v.x} {v.y} {v.z}\n"));
                sb.Append("endloop\nendfacet\n");
            }
            return sb.Append("endsolid box\n").ToString();
        }

        static Mesh MeshOf(List<Vector3> vertices, List<int> triangles)
        {
            var model = ModelLibrary.FromTriangles("t", vertices, triangles);
            model.id = "print-test-" + System.Guid.NewGuid().ToString("N");
            var shape = new ModelShape(model);
            ShapeRegistry.RegisterModel(shape, listed: false);
            return ShapeRegistry.GetMesh(shape.Id, shape.CreateDefaults(), false);
        }

        /// <summary>Same rule as ShapeTests: every face of the built mesh points away from the middle.</summary>
        static void Outward(Mesh mesh, string what)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            var center = mesh.bounds.center;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Assert.That(Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center), Is.GreaterThan(-1e-6f), $"{what}: triangle {i / 3} faces inward");
            }
        }

        [Test]
        public void Stl_BinaryAndText_ZUpBecomesYUp_FacesOutward()
        {
            var box = Box(new Vector3(10f, 20f, 30f));   // 30 mm tall (Z)
            foreach (var (name, read) in new (string, (List<Vector3>, List<int>))[]
                     {
                         ("binary", PrintReader.ReadStl(BinaryStl(box))),
                         ("text", PrintReader.ReadStl(Encoding.ASCII.GetBytes(TextStl(box)))),
                     })
            {
                var (vertices, triangles) = read;
                Assert.AreEqual(12, triangles.Count / 3, name);
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (var p in vertices) bounds.Encapsulate(p);
                Assert.AreEqual(30f, bounds.size.y, 1e-4f, name + ": the print's height (Z) is the app's height (Y)");
                Assert.AreEqual(20f, bounds.size.z, 1e-4f, name);
                Outward(MeshOf(vertices, triangles), name);
            }
        }

        [Test]
        public void ThreeMf_ColorsAndPlacement()
        {
            // Two boxes: one red, one blue (basematerials), the blue one moved 50 mm along X by the build item.
            string Mesh(int id, string pindex)
            {
                var sb = new StringBuilder($"<object id=\"{id}\" type=\"model\" pid=\"9\" pindex=\"{pindex}\"><mesh><vertices>");
                var tris = Box(new Vector3(10f, 10f, 10f));
                foreach (var t in tris) foreach (var v in t) sb.Append(System.FormattableString.Invariant($"<vertex x=\"{v.x}\" y=\"{v.y}\" z=\"{v.z}\"/>"));
                sb.Append("</vertices><triangles>");
                for (int i = 0; i < tris.Count; i++) sb.Append($"<triangle v1=\"{i * 3}\" v2=\"{i * 3 + 1}\" v3=\"{i * 3 + 2}\"/>");
                return sb.Append("</triangles></mesh></object>").ToString();
            }
            string xml = "<?xml version=\"1.0\"?><model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">" +
                         "<resources><basematerials id=\"9\"><base name=\"Red\" displaycolor=\"#E02020FF\"/><base name=\"Blue\" displaycolor=\"#2040E0FF\"/></basematerials>" +
                         Mesh(1, "0") + Mesh(2, "1") + "</resources><build><item objectid=\"1\"/>" +
                         "<item objectid=\"2\" transform=\"1 0 0 0 1 0 0 0 1 50 0 0\"/></build></model>";
            byte[] file;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                using (var w = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open()))
                    w.Write(xml);
                file = ms.ToArray();
            }

            var (vertices, triangles, colors) = PrintReader.Read3mf(file);
            Assert.AreEqual(24, triangles.Count / 3);
            Assert.IsNotNull(colors);
            Assert.AreEqual(new Color32(0xE0, 0x20, 0x20, 255), colors[0]);
            Assert.AreEqual(new Color32(0x20, 0x40, 0xE0, 255), colors[12]);
            float maxX = vertices.Max(v => v.x);
            Assert.AreEqual(0.055f, maxX, 1e-4f, "millimeters -> meters, the second box moved 50 mm");
            // Each box on its own (seen from the middle of both, the faces between them would point "inward").
            for (int b = 0; b < 2; b++)
            {
                var tris = triangles.Skip(b * 36).Take(36).ToList();
                var used = tris.Distinct().OrderBy(i => i).ToList();
                var verts = used.Select(i => vertices[i]).ToList();
                Outward(MeshOf(verts, tris.Select(i => used.IndexOf(i)).ToList()), "3mf box " + b);
            }
            var (merged, _) = ModelColors.Merge(colors);
            Assert.AreEqual(2, merged.Length);
        }
    }
}
