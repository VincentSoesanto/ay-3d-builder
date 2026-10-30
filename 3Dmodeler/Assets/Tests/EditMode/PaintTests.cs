using System.Collections.Generic;
using Builder.Core;
using Builder.Shapes;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class PaintTests
    {
        static float Area(Mesh m, int sub)
        {
            var v = m.vertices;
            var t = m.GetTriangles(sub);
            float a = 0f;
            for (int i = 0; i < t.Length; i += 3) a += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * 0.5f;
            return a;
        }

        static Piece Box() => new Piece { id = "a", shape = "box", p = new ShapeParams(), color = "#808080" };

        [Test]
        public void Spray_OneSquareOnTop_IsExactlyThatSquare()
        {
            var piece = Box();   // 1 x 1 x 1, from -0.5 to 0.5
            piece.paintCell = 0.25f;
            // The top face is at y = 0.5: its cell (just inside the box) is y = 1.
            var cell = PaintedMesh.CellOf(new Vector3(0.1f, 0.5f, 0.1f), Vector3.up, 0.25f);
            Assert.AreEqual(new Vector3Int(0, 1, 0), cell);
            piece.paint = new List<PaintDot> { new PaintDot(cell.x, cell.y, cell.z, "#FF0000") };

            var mesh = PaintedMesh.Get(piece, out var colors);
            CollectionAssert.AreEqual(new[] { "#808080", "#FF0000" }, colors);
            Assert.AreEqual(2, mesh.subMeshCount);
            Assert.AreEqual(0.0625f, Area(mesh, 1), 1e-4f, "one 0.25 x 0.25 square is red");
            Assert.AreEqual(6f - 0.0625f, Area(mesh, 0), 1e-4f, "the rest keeps the piece color");
        }

        [Test]
        public void Spray_OnACorner_PaintsAllThreeFacesOfThatCell()
        {
            var piece = Box();
            piece.paintCell = 0.25f;
            piece.paint = new List<PaintDot> { new PaintDot(1, 1, 1, "#00FF00") };   // the cube cell at the +x +y +z corner
            var mesh = PaintedMesh.Get(piece, out _);
            Assert.AreEqual(3 * 0.0625f, Area(mesh, 1), 1e-4f);
        }

        [Test]
        public void Mirror_MirrorsTheSprayPaint()
        {
            var piece = Box();
            piece.position = new Vec3(2f, 0f, 0f);
            piece.paintCell = 0.25f;
            piece.paint = new List<PaintDot> { new PaintDot(1, 1, 0, "#FF0000") };
            var mirror = MirrorEdits.MirrorOf(piece, "b");
            Assert.AreEqual(-2, mirror.paint[0].x, "cell x -> -x - 1");
            var a = PaintedMesh.Get(piece, out _);
            var b = PaintedMesh.Get(mirror, out _);
            Assert.AreEqual(Area(a, 1), Area(b, 1), 1e-4f);
            // The red part of the mirror sits at the mirrored spot (in world space).
            Vector3 Middle(Mesh m, Piece p)
            {
                var v = m.vertices; var t = m.GetTriangles(1); var sum = Vector3.zero;
                foreach (var i in t) sum += v[i];
                return p.rotation.ToUnity() * (sum / t.Length) + p.position.ToUnity();
            }
            var ma = Middle(a, piece);
            var mb = Middle(b, mirror);
            Assert.That(Vector3.Distance(new Vector3(-ma.x, ma.y, ma.z), mb), Is.LessThan(1e-3f));
        }

        [Test]
        public void Finish_ParseMakeAndMaterials()
        {
            Assert.AreEqual(ColorFinish.Finish.Metal, ColorFinish.Of("#aabbcc/metal"));
            Assert.AreEqual(ColorFinish.Finish.Glow, ColorFinish.Of("#AABBCC/GLOW"));
            Assert.AreEqual(ColorFinish.Finish.Matte, ColorFinish.Of("#AABBCC"));
            Assert.AreEqual("#AABBCC/glow", ColorFinish.Make("#aabbcc/metal", ColorFinish.Finish.Glow));
            Assert.AreEqual(Conversions.HexToColor("#AABBCC"), Conversions.HexToColor("#AABBCC/metal"), "the finish does not change the color");

            var baseMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var metal = ColorFinish.MaterialFor(baseMat, "#9AA3AD/metal");
            Assert.That(metal.GetFloat("_Metallic"), Is.GreaterThan(0.5f));
            var glow = ColorFinish.MaterialFor(baseMat, "#4FE3FF/glow");
            Assert.IsTrue(glow.IsKeywordEnabled("_EMISSION"));
            Assert.That(glow.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(0.5f));
            Object.DestroyImmediate(metal);
            Object.DestroyImmediate(glow);
            Object.DestroyImmediate(baseMat);
        }

        [Test]
        public void LinkedCopy_SprayPaintingIt_Unlinks()
        {
            var bp = new Blueprint { id = "bp", version = 1, content = new BuildContent { pieces = { Box() } } };
            var link = new PieceLink { instanceId = "i", blueprintId = "bp", version = 1 };
            var content = new BuildContent();
            var p = LinkedCopies.MakePart(bp.content.pieces[0], link, 0);
            p.id = "c0";
            content.pieces.Add(p);
            var after = p.Clone();
            after.paintCell = 0.25f;
            after.paint = new List<PaintDot> { new PaintDot(0, 1, 0, "#FF0000") };
            Assert.AreEqual(1, LinkedCopies.ApplyEditRules(content, new List<Piece> { p.Clone() }, new List<Piece> { after }));
        }
    }
}
