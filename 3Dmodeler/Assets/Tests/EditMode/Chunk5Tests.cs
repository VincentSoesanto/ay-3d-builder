using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using Builder.Tools;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>Mirror building and the clipboard.</summary>
    public class Chunk5Tests
    {
        static Piece Wedge(string id, Vector3 pos, Quaternion rot) => new Piece
        {
            id = id, shape = "wedge", p = new ShapeParams(), position = pos.ToDoc(), rotation = rot.ToDoc(),
        };

        static List<Vector3> WorldCorners(Piece p)
        {
            var mesh = ShapeRegistry.GetMesh(p.shape, p.p, p.mirrored);
            return mesh.vertices.Distinct().Select(v => p.position.ToUnity() + p.rotation.ToUnity() * v).ToList();
        }

        [Test]
        public void MirrorCopy_IsExactMirrorImage()
        {
            // A turned, tilted slope (not symmetric) must mirror exactly: every corner reflected across X = 0.
            var original = Wedge("a", new Vector3(1.3f, 0.5f, 2f), Quaternion.Euler(10f, 70f, 30f));
            var copy = MirrorEdits.MirrorOf(original, "b");

            var expected = WorldCorners(original).Select(c => new Vector3(-c.x, c.y, c.z)).ToList();
            var actual = WorldCorners(copy);
            Assert.AreEqual(expected.Count, actual.Count);
            foreach (var e in expected)
                Assert.That(actual.Min(a => Vector3.Distance(a, e)), Is.LessThan(1e-4f), $"no mirrored corner near {e}");
        }

        [Test]
        public void Placing_WithMirror_AddsLinkedCopies_ButNotOnTheLine()
        {
            var left = Wedge("a", new Vector3(2f, 0.5f, 0f), Quaternion.identity);
            var middle = Wedge("m", new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            var all = MirrorEdits.WithMirrorCopies(new[] { left, middle });

            Assert.AreEqual(3, all.Count, "the piece on the mirror line gets no copy");
            var copy = all.Single(p => p.id != "a" && p.id != "m");
            Assert.AreEqual(copy.id, left.mirrorPartnerId);
            Assert.AreEqual("a", copy.mirrorPartnerId);
            Assert.AreEqual(-2f, copy.position.x);
        }

        [Test]
        public void Deleting_WithMirror_TakesPartnerToo()
        {
            var c = new BuildContent();
            c.pieces.AddRange(MirrorEdits.WithMirrorCopies(new[] { Wedge("a", new Vector3(2f, 0.5f, 0f), Quaternion.identity) }));
            var ids = MirrorEdits.WithPartners(new[] { "a" }, c);
            Assert.AreEqual(2, ids.Count);
        }

        [Test]
        public void Editing_WithMirror_MovesPartnerTheMirroredWay()
        {
            var c = new BuildContent();
            c.pieces.AddRange(MirrorEdits.WithMirrorCopies(new[] { Wedge("a", new Vector3(2f, 0.5f, 0f), Quaternion.identity) }));
            var before = new List<Piece> { c.FindPiece("a").Clone() };
            var moved = before[0].Clone();
            moved.position = new Vec3(3f, 1f, 4f);   // move right, up and back
            var after = new List<Piece> { moved };

            MirrorEdits.AddPartnerEdits(before, after, c);
            Assert.AreEqual(2, after.Count);
            var partner = after[1];
            Assert.AreEqual(-3f, partner.position.x, "partner moves left");
            Assert.AreEqual(1f, partner.position.y);
            Assert.AreEqual(4f, partner.position.z);
        }

        [Test]
        public void Clipboard_StoresPositionsFromGroupBottomMiddle()
        {
            var go = new GameObject("clipboard test");
            try
            {
                var clipboard = go.AddComponent<Clipboard>();
                var a = Wedge("a", new Vector3(1f, 0.5f, 1f), Quaternion.identity);
                var b = Wedge("b", new Vector3(3f, 0.5f, 1f), Quaternion.identity);
                var bounds = new Bounds(new Vector3(2f, 0.5f, 1f), new Vector3(3f, 1f, 1f));   // around both
                clipboard.Copy(new[] { a, b }, bounds);

                var parts = clipboard.Parts;
                Assert.AreEqual(2, parts.Count);
                Assert.AreEqual(new Vector3(-1f, 0.5f, 0f), parts[0].position.ToUnity());
                Assert.AreEqual(new Vector3(1f, 0.5f, 0f), parts[1].position.ToUnity());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
