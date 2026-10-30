using System.Linq;
using Builder.IO;
using Builder.Shapes;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class PartLibraryTests
    {
        [Test]
        public void Library_Loads_EveryItemIsUsable()
        {
            PartLibrary.Load();
            var items = PartLibrary.Items;
            Assert.That(items.Count, Is.GreaterThan(250), "the built-in parts are there");
            Assert.AreEqual(items.Count, items.Select(i => i.id).Distinct().Count(), "ids are unique");
            foreach (var tab in PartLibrary.Tabs)
                Assert.That(items.Count(i => i.tab == tab), Is.GreaterThan(5), "tab " + tab + " has parts");
            Assert.IsTrue(items.All(i => PartLibrary.Tabs.Contains(i.tab)), "every item is in a known tab");

            foreach (var it in items)
            {
                Assert.That(it.blueprint.content.pieces.Count, Is.InRange(1, 8), it.name);
                foreach (var p in it.blueprint.content.pieces)
                {
                    var shape = ShapeRegistry.Get(p.shape);
                    Assert.IsNotNull(shape, $"{it.name}: its piece shape is known");
                    Assert.IsFalse(ShapeRegistry.Models.Contains(shape), "library pieces are not listed as imported models");
                }
            }
        }

        [Test]
        public void Library_Gear_IsAClosedSolidStandingOnTheFloor()
        {
            var gear = PartLibrary.Get("ay.gear");
            Assert.IsNotNull(gear);
            Assert.AreEqual("Gears", gear.category);
            float bottom = float.MaxValue;
            foreach (var p in gear.blueprint.content.pieces)
            {
                var mesh = ShapeRegistry.GetPieceMesh(p);
                bottom = Mathf.Min(bottom, p.position.y + mesh.bounds.min.y);
                // Faces point outward: the signed volume is the same sign as the app's own box.
                var box = ShapeRegistry.GetPieceMesh(new Builder.Core.Piece { shape = "box", p = new Builder.Core.ShapeParams() });
                Assert.AreEqual(Mathf.Sign(Volume(box)), Mathf.Sign(Volume(mesh)), "faces point outward");
            }
            Assert.AreEqual(0f, bottom, 1e-3f, "stands on y = 0, like a blueprint");
            Assert.AreEqual(gear.blueprint, PartLibrary.FromItem(PartLibrary.ItemOf(gear)).blueprint);
        }

        static float Volume(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            float sum = 0f;
            for (int i = 0; i < t.Length; i += 3) sum += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            return sum / 6f;
        }
    }
}
