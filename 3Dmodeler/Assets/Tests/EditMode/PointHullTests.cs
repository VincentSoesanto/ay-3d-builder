using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>The solid wrapped around points (convex hull).</summary>
    public class PointHullTests
    {
        static float Volume(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            float sum = 0f;
            for (int i = 0; i < t.Length; i += 3) sum += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            return sum / 6f;
        }

        static Mesh Hull(IEnumerable<Vector3> points)
        {
            var mb = new MeshBuilder();
            ConvexHull.Build(points.ToList(), mb);
            return mb.ToMesh("hull", false);
        }

        static IEnumerable<Vector3> BoxCorners(Vector3 size)
        {
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                yield return Vector3.Scale(new Vector3(x, y, z) - Vector3.one * 0.5f, size);
        }

        [Test]
        public void EightCorners_MakeTheBlock()
        {
            var mesh = Hull(BoxCorners(new Vector3(3f, 1f, 1f)));   // a long block
            Assert.That(Volume(mesh), Is.EqualTo(3f).Within(1e-3f), "closed, faces outward, right size");
            Assert.AreEqual(12, mesh.triangles.Length / 3, "6 flat sides = 12 triangles (no doubled faces)");
        }

        [Test]
        public void FivePoints_MakeAPyramid()
        {
            var points = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(0, 3, 0) };
            Assert.That(Volume(Hull(points)), Is.EqualTo(4f).Within(1e-3f), "base 2 x 2, height 3: 2*2*3/3 = 4");
        }

        [Test]
        public void PointInside_IsIgnored()
        {
            var points = BoxCorners(Vector3.one).Concat(new[] { Vector3.zero, new Vector3(0.1f, 0.2f, 0f) });
            Assert.That(Volume(Hull(points)), Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void FlatPoints_AreNotASolid()
        {
            var flat = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(0.5f, 0, 0.5f) };
            Assert.IsFalse(ConvexHull.IsSolid(flat));
            Assert.IsTrue(ConvexHull.IsSolid(flat.Append(new Vector3(0, 1, 0)).ToList()));
        }

        [Test]
        public void ThreePoints_MakeAThinTriangle()
        {
            var flat = new[] { new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 0, 2) };   // triangle, area 2
            var spots = ConvexHull.MakeSolid(flat, 0.05f, out string kind);
            Assert.AreEqual("plate", kind);
            Assert.That(Volume(Hull(spots)), Is.EqualTo(2f * 0.05f).Within(1e-4f), "area x thickness");
        }

        [Test]
        public void TwoPoints_MakeAStick()
        {
            var spots = ConvexHull.MakeSolid(new[] { new Vector3(0, 0, 0), new Vector3(0, 3, 0) }, 0.05f, out string kind);
            Assert.AreEqual("stick", kind);
            var mesh = Hull(spots);
            Assert.That(Volume(mesh), Is.EqualTo(3f * 0.05f * 0.05f).Within(1e-5f));
            Assert.That(mesh.bounds.size.y, Is.EqualTo(3f).Within(1e-4f), "from end to end");
        }

        [Test]
        public void PointsOnALine_MakeAStick_OnePointIsNotEnough()
        {
            ConvexHull.MakeSolid(new[] { Vector3.zero, new Vector3(1, 1, 0), new Vector3(2, 2, 0) }, 0.05f, out string kind);
            Assert.AreEqual("stick", kind);
            Assert.IsNull(ConvexHull.MakeSolid(new[] { Vector3.one, Vector3.one }, 0.05f, out _), "two points on the same spot");
        }

        [Test]
        public void HullPiece_StretchesWithItsSize()
        {
            // Points stored in a 1 x 1 x 1 box; the piece's width/height/length stretch them (resizing works).
            var p = new ShapeParams { width = 2f, height = 1f, length = 4f, points = BoxCorners(Vector3.one).Select(v => new Vec3(v.x, v.y, v.z)).ToList() };
            var mesh = ShapeRegistry.GetMesh(HullShape.ShapeId, p, false);
            Assert.That(Volume(mesh), Is.EqualTo(8f).Within(1e-3f));
            Assert.That(Vector3.Distance(mesh.bounds.size, new Vector3(2f, 1f, 4f)), Is.LessThan(1e-4f));
        }
    }
}
