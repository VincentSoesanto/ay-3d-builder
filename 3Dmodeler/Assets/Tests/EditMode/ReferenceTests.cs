using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class ReferenceTests
    {
        [Test]
        public void Human_Is1Point8MetersTall()
        {
            var human = ShapeRegistry.Get(HumanShape.ShapeId);
            var mesh = ShapeRegistry.GetMesh(human.Id, human.CreateDefaults(), false);
            Assert.That(mesh.bounds.size.y, Is.EqualTo(1.8f).Within(1e-3f));
            Assert.That(mesh.bounds.min.y, Is.EqualTo(-0.9f).Within(1e-3f), "feet at the bottom of its box");
        }

        [Test]
        public void Helpers_AreNotExported()
        {
            var doc = ProjectDocument.CreateNew("Ref", 0);
            doc.content.pieces.Add(new Piece { id = "box", shape = "box", p = new ShapeParams() });
            doc.content.pieces.Add(new Piece { id = "human", shape = HumanShape.ShapeId, p = ShapeRegistry.Get("human").CreateDefaults(), position = new Vec3(3f, 0.9f, 0f) });
            doc.content.pieces.Add(new Piece { id = "point", shape = PointShape.ShapeId, p = ShapeRegistry.Get("point").CreateDefaults(), position = new Vec3(-3f, 0f, 0f) });

            var made = new List<Object>();
            var root = GlbExporter.BuildModel(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), made);
            try
            {
                var mesh = root.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
                Assert.That(mesh.bounds.size.x, Is.EqualTo(1f).Within(1e-3f), "only the box is in the export");
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
