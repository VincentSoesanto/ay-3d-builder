using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.Shapes;
using Builder.Tools;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>Eraser Cut mode: the cutting math and remembered cuts (also on mirrored pieces).</summary>
    public class CutTests
    {
        static readonly ShapeParams Unit = new ShapeParams();

        /// <summary>Volume of a closed mesh (sum of little pyramids from the origin). Negative = inside out.</summary>
        static float Volume(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            float sum = 0f;
            for (int i = 0; i < t.Length; i += 3)
                sum += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            return sum / 6f;
        }

        static Mesh ToMesh(List<Csg.Polygon> polygons)
        {
            var mb = new MeshBuilder();
            Csg.AddTo(mb, polygons);
            return mb.ToMesh("test", false);
        }

        [Test]
        public void BoxMinusHalfOverlappingBox_IsHalfABox()
        {
            var box = Csg.FromMesh(ShapeRegistry.GetMesh("box", Unit, false), Matrix4x4.identity);
            var cutter = Csg.FromMesh(ShapeRegistry.GetMesh("box", Unit, false), Matrix4x4.Translate(new Vector3(0.5f, 0f, 0f)));
            var mesh = ToMesh(Csg.Subtract(box, cutter));

            Assert.That(Volume(mesh), Is.EqualTo(0.5f).Within(1e-3f), "half the volume is left (and the surface is closed)");
            Assert.That(mesh.bounds.min.x, Is.EqualTo(-0.5f).Within(1e-4f));
            Assert.That(mesh.bounds.max.x, Is.EqualTo(0f).Within(1e-4f), "cut exactly at the cutter's face");
        }

        [Test]
        public void CornerCutBySphere_RemovesSomeVolume()
        {
            var piece = new Piece { id = "a", shape = "box", p = new ShapeParams(),
                cuts = new List<Cut> { new Cut { shape = "sphere", p = new ShapeParams { segments = 12 }, position = new Vec3(0.5f, 0.5f, 0.5f) } } };
            var mesh = ShapeRegistry.GetPieceMesh(piece);
            float v = Volume(mesh);
            Assert.That(v, Is.LessThan(0.99f), "a corner is gone");
            Assert.That(v, Is.GreaterThan(0.8f), "only a corner is gone (about 1/8 of a ball)");
        }

        static List<Vector3> WorldPoints(Piece p)
        {
            var mesh = ShapeRegistry.GetPieceMesh(p);
            return mesh.vertices.Select(v => p.position.ToUnity() + p.rotation.ToUnity() * v).ToList();
        }

        static void AssertMirrorImages(Piece a, Piece b)
        {
            var expected = WorldPoints(a).Select(c => new Vector3(-c.x, c.y, c.z)).ToList();
            var actual = WorldPoints(b);
            foreach (var e in expected.Distinct())
                Assert.That(actual.Min(x => Vector3.Distance(x, e)), Is.LessThan(1e-3f), $"no mirrored point near {e}");
            Assert.That(Mathf.Abs(Volume(ShapeRegistry.GetPieceMesh(a))), Is.EqualTo(Mathf.Abs(Volume(ShapeRegistry.GetPieceMesh(b)))).Within(1e-3f));
        }

        [Test]
        public void MirrorCopy_OfCutPiece_IsExactMirrorImage()
        {
            var a = new Piece { id = "a", shape = "wedge", p = new ShapeParams(), position = new Vec3(2f, 0.5f, 1f),
                rotation = Quaternion.Euler(0f, 30f, 10f).ToDoc(),
                cuts = new List<Cut> { new Cut { shape = "cylinder", p = new ShapeParams { width = 0.6f, length = 0.6f, height = 2f, segments = 8 },
                                                 position = new Vec3(0.3f, 0f, -0.2f), rotation = Quaternion.Euler(0f, 0f, 90f).ToDoc() } } };
            AssertMirrorImages(a, MirrorEdits.MirrorOf(a, "b"));
        }

        [Test]
        public void CuttingAMirroredPiece_MatchesCuttingTheOriginal()
        {
            // Cut a piece with a turned cutter, and cut its mirror copy with the mirrored cutter (what mirror
            // building does): the two results must be exact mirror images.
            var original = new Piece { id = "a", shape = "box", p = new ShapeParams(), position = new Vec3(2f, 0.5f, 0f),
                                       rotation = Quaternion.Euler(0f, 40f, 0f).ToDoc() };
            var mirror = MirrorEdits.MirrorOf(original, "b");

            var cutter = new EraseTool.Cutter { shape = "wedge", p = new ShapeParams(), mirrored = false,
                position = new Vector3(2.4f, 0.9f, 0.3f), rotation = Quaternion.Euler(0f, 20f, 15f) };
            var mirrorCutter = new EraseTool.Cutter { shape = "wedge", p = new ShapeParams(), mirrored = true,
                position = new Vector3(-2.4f, 0.9f, 0.3f), rotation = MirrorEdits.Rotation(cutter.rotation.ToDoc()).ToUnity() };

            original.cuts = new List<Cut> { BuildTool.ToCut(original, cutter) };
            mirror.cuts = new List<Cut> { BuildTool.ToCut(mirror, mirrorCutter) };
            Assert.Less(Volume(ShapeRegistry.GetPieceMesh(original)), 0.999f, "something was cut");
            AssertMirrorImages(original, mirror);
        }

        [Test]
        public void Cuts_SurviveSaveAndLoad()
        {
            var doc = ProjectDocument.CreateNew("Cut", 0);
            doc.content.pieces.Add(new Piece { id = "a", shape = "box", p = new ShapeParams(),
                cuts = new List<Cut> { new Cut { shape = "sphere", p = new ShapeParams(), position = new Vec3(0.5f, 0f, 0f), mirrored = true } } });
            var back = ProjectStore.FromJson(ProjectStore.ToJson(doc));
            Assert.AreEqual(1, back.content.pieces[0].cuts.Count);
            Assert.AreEqual("sphere", back.content.pieces[0].cuts[0].shape);
            Assert.IsTrue(back.content.pieces[0].cuts[0].mirrored);
            StringAssert.DoesNotContain("HasCuts", ProjectStore.ToJson(doc));
        }
    }
}
