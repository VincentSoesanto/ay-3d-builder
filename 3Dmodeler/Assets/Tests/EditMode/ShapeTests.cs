using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>Builds every shape and checks size, normals and that faces point outward.</summary>
    public class ShapeTests
    {
        static IEnumerable<string> AllShapeIds => ShapeRegistry.All.Select(s => s.Id);

        // Shapes without dents, where every face must point away from the middle.
        static readonly string[] Convex = { "box", "plate", "wedge", "cornerWedge", "cylinder", "halfCylinder", "cone", "sphere" };

        static Mesh Build(string id, ShapeParams p, bool mirrored = false)
        {
            var mb = new MeshBuilder();
            ShapeRegistry.Get(id).Build(p, mb);
            return mb.ToMesh(id, mirrored);
        }

        [Test]
        public void Registry_HasNineShapes() => Assert.AreEqual(9, ShapeRegistry.All.Count);

        [TestCaseSource(nameof(AllShapeIds))]
        public void Shape_HasExpectedSize_AndValidNormals(string id)
        {
            var p = ShapeRegistry.Get(id).CreateDefaults();
            var mesh = Build(id, p);

            Assert.Greater(mesh.vertexCount, 0);
            foreach (var n in mesh.normals)
                Assert.That(Mathf.Abs(n.magnitude - 1f), Is.LessThan(1e-3f), "normal not length 1");

            var size = mesh.bounds.size;
            Assert.That(Vector3.Distance(size, new Vector3(p.width, p.height, p.length)), Is.LessThan(1e-3f),
                        $"{id} size {size} != params");
        }

        [TestCaseSource(nameof(Convex))]
        public void ConvexShape_FacesPointOutward(string id) =>
            AssertOutward(Build(id, ShapeRegistry.Get(id).CreateDefaults()), id);

        [TestCaseSource(nameof(Convex))]
        public void MirroredShape_FacesPointOutward(string id) =>
            AssertOutward(Build(id, ShapeRegistry.Get(id).CreateDefaults(), mirrored: true), id + " mirrored");

        [Test]
        public void Bevel_And_Taper_KeepFacesOutward()
        {
            var p = new ShapeParams { width = 1f, height = 1f, length = 2f, bevel = 0.15f, taper = 0.4f, segments = 10 };
            AssertOutward(Build("box", p), "beveled tapered box");
            AssertOutward(Build("cylinder", p), "beveled tapered cylinder");
        }

        [TestCase("box")]
        [TestCase("cylinder")]
        [TestCase("halfCylinder")]
        public void Hollow_BuildsAndKeepsOuterSize(string id)
        {
            var p = ShapeRegistry.Get(id).CreateDefaults();
            p.hollow = true;
            p.wall = 0.1f;
            var mesh = Build(id, p);
            Assert.Greater(mesh.vertexCount, 0);
            Assert.That(Vector3.Distance(mesh.bounds.size, new Vector3(p.width, p.height, p.length)), Is.LessThan(1e-3f));
        }

        static void AssertOutward(Mesh mesh, string what)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            var center = mesh.bounds.center;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                var outward = (a + b + c) / 3f - center;
                Assert.That(Vector3.Dot(normal, outward), Is.GreaterThan(-1e-6f), $"{what}: triangle {i / 3} faces inward");
            }
        }
    
        [Test]
        public void MeshCache_StaysSmall_AndKeepsMeshesInUse()
        {
            int old = ShapeRegistry.MaxCachedMeshes;
            ShapeRegistry.MaxCachedMeshes = 50;
            try
            {
                var shown = ShapeRegistry.GetMesh("box", new ShapeParams { width = 0.123f, height = 1f, length = 1f }, false);
                for (int i = 0; i < 200; i++)   // like turning the wheel to resize: a new size every step
                    ShapeRegistry.GetMesh("box", new ShapeParams { width = 1f + i * 0.01f, height = 1f, length = 1f }, false);
                Assert.That(ShapeRegistry.CachedMeshCount, Is.LessThanOrEqualTo(50));
                Assert.IsTrue(shown != null, "a mesh a piece still shows is never destroyed");
            }
            finally { ShapeRegistry.MaxCachedMeshes = old; }
        }
    }
}
