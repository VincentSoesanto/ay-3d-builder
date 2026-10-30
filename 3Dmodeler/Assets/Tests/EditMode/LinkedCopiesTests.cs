using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class LinkedCopiesTests
    {
        static Blueprint TwoBoxes(float width) => new Blueprint
        {
            id = "bp", name = "Two", version = 1,
            content = new BuildContent
            {
                pieces =
                {
                    new Piece { id = "s0", shape = "box", p = new ShapeParams { width = width }, position = new Vec3(0f, 0.5f, 0f) },
                    new Piece { id = "s1", shape = "wedge", p = new ShapeParams(), position = new Vec3(1f, 0.5f, 0f) },
                },
            },
        };

        static BuildContent Placed(Blueprint bp, PieceLink link)
        {
            var content = new BuildContent();
            for (int i = 0; i < bp.content.pieces.Count; i++)
            {
                var p = LinkedCopies.MakePart(bp.content.pieces[i], link, i);
                p.id = "c" + i;
                content.pieces.Add(p);
            }
            return content;
        }

        static PieceLink Link(Blueprint bp) => new PieceLink
        {
            instanceId = "i1", blueprintId = bp.id, version = bp.version,
            position = new Vec3(3f, 0f, 2f), rotation = Quaternion.Euler(0f, 90f, 0f).ToDoc(), scale = 2f,
        };

        static Blueprint Rigged(int version, bool withB, float tailY)
        {
            var bp = TwoBoxes(1f);
            bp.version = version;
            bp.content.bones.Add(new Bone { id = "a", name = "Arm", head = new Vec3(0f, 0f, 0f), tail = new Vec3(0f, tailY, 0f) });
            bp.content.pieces[0].boneId = "a";
            if (withB)
            {
                bp.content.bones.Add(new Bone { id = "b", name = "Hand", parentId = "a", head = new Vec3(0f, tailY, 0f), tail = new Vec3(0f, tailY + 0.5f, 0f) });
                bp.content.pieces[1].boneId = "b";
            }
            return bp;
        }

        [Test]
        public void Rebuild_CopyBonesFollowTheBlueprint()
        {
            var v1 = Rigged(1, false, 1f);
            var link = Link(v1);
            var content = Placed(v1, link);
            var arm = Rig.Placed(v1.content.bones[0], link.flipped, link.scale, link.rotation.ToUnity(), link.position.ToUnity());
            arm.id = "d1";
            arm.name = "MyArm";   // renamed by the user: stays
            arm.copyId = link.instanceId;
            arm.copyBone = "a";
            content.bones.Add(arm);
            content.pieces[0].boneId = "d1";

            // v2: the arm is longer and has a new hand bone, the second piece is bound to it.
            var v2 = Rigged(2, true, 2f);
            Assert.AreEqual(1, LinkedCopies.RebuildInPlace(content, id => v2));
            Assert.AreEqual(2, content.bones.Count);
            var newArm = content.FindBone("d1");
            Assert.AreEqual("MyArm", newArm.name);
            Assert.That(Vector3.Distance(new Vector3(3f, 4f, 2f), newArm.tail.ToUnity()), Is.LessThan(1e-4f), "tail at y 2, scaled x2, moved");
            var hand = content.bones.Single(b => b.copyBone == "b");
            Assert.AreEqual("d1", hand.parentId);
            Assert.AreEqual("d1", content.pieces.Single(p => p.link.part == 0).boneId);
            Assert.AreEqual(hand.id, content.pieces.Single(p => p.link.part == 1).boneId);

            // v3: the hand bone is gone again: its bone goes, its piece is not bound any more.
            var v3 = Rigged(3, false, 2f);
            LinkedCopies.RebuildInPlace(content, id => v3);
            Assert.AreEqual(1, content.bones.Count);
            Assert.IsNull(content.pieces.Single(p => p.link.part == 1).boneId);
        }

        [Test]
        public void Rebuild_CopyWithoutItsOwnBones_LeavesBonesAlone()
        {
            var v1 = TwoBoxes(1f);
            var content = Placed(v1, Link(v1));
            content.bones.Add(new Bone { id = "x", name = "Other", head = new Vec3(0f, 0f, 0f), tail = new Vec3(0f, 1f, 0f) });
            content.pieces[0].boneId = "x";
            var v2 = Rigged(2, true, 2f);
            LinkedCopies.RebuildInPlace(content, id => v2);
            Assert.AreEqual(1, content.bones.Count, "an old copy (bones not remembered): no bones added");
            Assert.AreEqual("x", content.pieces.Single(p => p.link.part == 0).boneId, "kept its binding");
        }

        [Test]
        public void Rebuild_UsesNewVersion_KeepsIdsAndPlacement()
        {
            var bp = TwoBoxes(1f);
            var content = Placed(bp, Link(bp));
            Assert.That(Vector3.Distance(new Vector3(3f, 1f, 0f), content.pieces[1].position.ToUnity()), Is.LessThan(1e-4f), "turned 90, scaled x2, moved");

            var v2 = TwoBoxes(0.5f);
            v2.version = 2;
            Assert.AreEqual(1, LinkedCopies.RebuildInPlace(content, id => v2));
            Assert.AreEqual("c0", content.pieces[0].id, "same piece ids");
            Assert.AreEqual(1f, content.pieces[0].p.width, 1e-5f, "new width 0.5, scaled x2");
            Assert.AreEqual(2, content.pieces[0].link.version);
            Assert.AreEqual(0, LinkedCopies.RebuildInPlace(content, id => v2), "up to date now");
        }

        [Test]
        public void WholeMove_StaysLinked_OtherEditsUnlink()
        {
            var bp = TwoBoxes(1f);
            var content = Placed(bp, Link(bp));

            var before = content.pieces.Select(p => p.Clone()).ToList();
            var after = content.pieces.Select(p =>
            {
                var c = p.Clone();
                c.position = (Quaternion.Euler(0f, 90f, 0f) * c.position.ToUnity() + Vector3.up).ToDoc();
                c.rotation = (Quaternion.Euler(0f, 90f, 0f) * c.rotation.ToUnity()).ToDoc();
                return c;
            }).ToList();
            Assert.AreEqual(0, LinkedCopies.ApplyEditRules(content, before, after));
            // The moved link rebuilds the moved pieces exactly.
            var rebuilt = LinkedCopies.MakePart(bp.content.pieces[1], after[1].link, 1);
            Assert.That(Vector3.Distance(rebuilt.position.ToUnity(), after[1].position.ToUnity()), Is.LessThan(1e-4f));

            // Moving only one piece unlinks the whole copy.
            before = new List<Piece> { content.pieces[0].Clone() };
            var one = content.pieces[0].Clone();
            one.position = new Vec3(9f, 0f, 0f);
            after = new List<Piece> { one };
            Assert.AreEqual(1, LinkedCopies.ApplyEditRules(content, before, after));
            Assert.AreEqual(2, after.Count, "the other piece is unlinked too");
            Assert.That(after.All(p => p.link == null));
        }

        [Test]
        public void DeletingPartOfACopy_UnlinksTheRest()
        {
            var bp = TwoBoxes(1f);
            var content = Placed(bp, Link(bp));
            var before = new List<Piece>();
            var after = new List<Piece>();
            Assert.AreEqual(1, LinkedCopies.ApplyRemoveRules(content, new[] { "c0" }, before, after));
            Assert.AreEqual("c1", after.Single().id);
            Assert.IsNull(after[0].link);
            Assert.AreEqual(0, LinkedCopies.ApplyRemoveRules(content, new[] { "c0", "c1" }, new List<Piece>(), new List<Piece>()), "whole copy deleted: nothing to unlink");
        }

        [Test]
        public void MirrorCopy_RebuildsAsExactMirror_AndStaysPaired()
        {
            var bp = TwoBoxes(1f);
            var originals = Placed(bp, Link(bp)).pieces;
            var all = MirrorEdits.WithMirrorCopies(originals);
            LinkedCopies.LinkMirrorCopies(originals, all.GetRange(originals.Count, all.Count - originals.Count));
            var content = new BuildContent { pieces = all };

            var v2 = TwoBoxes(0.5f);
            v2.version = 2;
            Assert.AreEqual(2, LinkedCopies.RebuildInPlace(content, id => v2), "original + mirror copy");
            foreach (var p in content.pieces.Where(p => !p.mirrored))
            {
                var partner = content.FindPiece(p.mirrorPartnerId);
                Assert.IsNotNull(partner, "still paired");
                var expected = MirrorEdits.MirrorOf(p, partner.id);
                Assert.That(Vector3.Distance(expected.position.ToUnity(), partner.position.ToUnity()), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(expected.rotation.ToUnity(), partner.rotation.ToUnity()), Is.LessThan(0.01f));
                Assert.AreEqual(p.p.width, partner.p.width, 1e-5f);
            }
        }

        [Test]
        public void Nested_CopyInsideBlueprint_Updates_AndLoopsAreFound()
        {
            var finger = TwoBoxes(1f);
            var hand = new Blueprint { id = "hand", version = 1, content = Placed(finger, Link(finger)) };
            var arm = new Blueprint { id = "arm", version = 1, content = Placed(hand, new PieceLink { instanceId = "h", blueprintId = "hand", version = 1 }) };
            var library = new Dictionary<string, Blueprint> { ["bp"] = finger, ["hand"] = hand, ["arm"] = arm };
            Blueprint Find(string id) => library.TryGetValue(id, out var b) ? b : null;

            Assert.IsTrue(LinkedCopies.Uses(hand, "bp", Find));
            Assert.IsTrue(LinkedCopies.Uses(arm, "bp", Find), "arm -> hand -> finger");
            Assert.IsFalse(LinkedCopies.Uses(finger, "arm", Find));

            library["bp"] = TwoBoxes(0.5f);
            library["bp"].version = 2;
            Assert.AreEqual(1, LinkedCopies.RebuildInPlace(hand.content, Find));
            Assert.AreEqual(1f, hand.content.pieces[0].p.width, 1e-5f);
        }
    }
}
