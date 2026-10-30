using System.IO;
using System.IO.Compression;
using System.Linq;
using Builder.Core;
using Builder.IO;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class ObjExportTests
    {
        [Test]
        public void Obj_HasEveryPieceAndColor_ReadsBackTheSameSize_ZipForMixamo()
        {
            var doc = ProjectDocument.CreateNew("Mech", 0);
            doc.content.groups.Add(new Group { id = "g", name = "Body" });
            doc.content.pieces.Add(new Piece { id = "a", shape = "box", p = new ShapeParams { width = 2f, height = 1f, length = 1f },
                                               position = new Vec3(3f, 0.5f, 0f), color = "#FF0000", groupId = "g" });
            doc.content.pieces.Add(new Piece { id = "b", shape = "box", p = new ShapeParams(), position = new Vec3(-2f, 0.5f, 0f), color = "#4FE3FF/glow" });
            doc.content.pieces.Add(new Piece { id = "pt", shape = "point", p = new ShapeParams() });   // helper: left out

            string folder = Path.Combine(Path.GetTempPath(), "AY3DObjTest_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string zip = ObjExporter.Export(doc, folder, "mech");
                string obj = File.ReadAllText(Path.Combine(folder, "mech.obj"));
                string mtl = File.ReadAllText(Path.Combine(folder, "mech.mtl"));

                StringAssert.Contains("mtllib mech.mtl", obj);
                StringAssert.Contains("o Body", obj);
                StringAssert.Contains("o Ungrouped", obj);
                StringAssert.Contains("newmtl color_FF0000", mtl);
                StringAssert.Contains("Ke ", mtl);   // the glow
                using (var archive = new ZipArchive(File.OpenRead(zip)))
                    CollectionAssert.AreEquivalent(new[] { "mech.obj", "mech.mtl" }, archive.Entries.Select(e => e.Name).ToArray());

                // Read it back with the app's own .obj reader: two boxes, 12 triangles each, same overall size.
                var (vertices, triangles) = ObjReader.Read(obj);
                Assert.AreEqual(24, triangles.Count / 3);
                var min = vertices.Aggregate(Vector3.Min);
                var max = vertices.Aggregate(Vector3.Max);
                Assert.That(Vector3.Distance(min, new Vector3(-2.5f, 0f, -0.5f)), Is.LessThan(1e-3f));
                Assert.That(Vector3.Distance(max, new Vector3(4f, 1f, 0.5f)), Is.LessThan(1e-3f));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
