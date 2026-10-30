using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.Shapes;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class RigToolsTests
    {
        [Test]
        public void Templates_HaveNamedConnectedBones_AndFitTheModel()
        {
            foreach (var name in RigTools.Templates)
            {
                var bones = RigTools.Fitted(name, new Bounds(new Vector3(2f, 4f, 0f), new Vector3(3f, 8f, 3f)));
                Assert.That(bones.Count, Is.GreaterThanOrEqualTo(3), name);
                Assert.AreEqual(bones.Count, bones.Select(b => b.name).Distinct().Count(), name + ": names differ (for the game)");
                Assert.AreEqual(1, bones.Count(b => b.parentId == null), name + ": one root bone");
                Assert.IsTrue(bones.All(b => b.parentId == null || bones.Any(p => p.id == b.parentId)), name + ": parents exist");
                var ys = bones.SelectMany(b => new[] { b.head.y, b.tail.y }).ToList();
                Assert.AreEqual(0f, ys.Min(), 1e-3f, name + ": stands on the model's floor");
                Assert.That(ys.Max(), Is.InRange(4f, 8.001f), name + ": about as tall as the model");
            }
            var biped = RigTools.Template("Biped");
            CollectionAssert.IsSubsetOf(new[] { "Hips", "Head", "Hand.L", "Hand.R", "Foot.L", "Foot.R" }, biped.Select(b => b.name).ToList());
        }

        [Test]
        public void MechFrame_PutsSizedPartsOnEveryBone_BoundToIt([Values("Biped", "Walker", "Quad", "Tank")] string template,
                                                                  [ValueSource(nameof(FramePresets))] string preset)
        {
            var content = new BuildContent { bones = RigTools.Fitted(template, new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 1.2f))) };
            var frame = MechFrame.Build(content, preset);
            Assert.IsTrue(frame.All(p => p.p.width > 0f && p.p.height > 0f && p.p.length > 0f && ShapeRegistry.Get(p.shape) != null), "real shapes with a size");
            Assert.That(frame.Count, Is.GreaterThanOrEqualTo(content.bones.Count), "parts on every bone");
            Assert.IsTrue(frame.All(p => p.frame && p.link == null && p.id != null));
            Assert.AreEqual(frame.Count, frame.Select(p => p.id).Distinct().Count());
            foreach (var bone in content.bones)
                Assert.IsTrue(frame.Any(p => p.boneId == bone.id), template + ": " + bone.name + " has a part");
            foreach (var p in frame)
            {
                var bone = content.FindBone(p.boneId);
                Assert.IsNotNull(bone);
                float d = RigTools.Distance(bone, p.position.ToUnity());
                Assert.That(d, Is.LessThan(0.5f), $"{template}: a part of {bone.name} is {d:0.00} m from its bone");
                Assert.That(Mathf.Max(p.p.width, Mathf.Max(p.p.height, p.p.length)), Is.LessThan(1.8f), "sized to the 2 m skeleton (a Quad body is 1.4 m long)");
            }
        }

        static IEnumerable<string> FramePresets() => MechFrame.Presets;

        [Test]
        public void MechFrame_DetailedFrame_HasJointsTrussAndPistons()
        {
            var content = new BuildContent { bones = RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 1.2f))) };
            var shin = content.bones.First(b => b.name == "LowerLeg.L");
            var detailed = MechFrame.Build(content, "Detailed (PG style)").Where(p => p.boneId == shin.id).ToList();
            var slim = MechFrame.Build(content, "Slim (RG style)").Where(p => p.boneId == shin.id).ToList();
            Assert.That(detailed.Count, Is.GreaterThan(slim.Count), "the PG style has more parts (truss, fins, hydraulics)");
            Assert.That(detailed.Count(p => p.shape == "cylinder"), Is.GreaterThanOrEqualTo(4), "double knee pins + hydraulic barrel and rod");
            var hand = content.bones.First(b => b.name == "Hand.L");
            Assert.That(MechFrame.Build(content, "Detailed (PG style)").Count(p => p.boneId == hand.id), Is.GreaterThanOrEqualTo(10), "jointed fingers");
        }

        [Test]
        public void MechFrame_PistonsRunAlongTheirBone()
        {
            var content = new BuildContent { bones = RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 1.2f))) };
            var shin = content.bones.First(b => b.name == "LowerLeg.L");
            var dir = (shin.tail.ToUnity() - shin.head.ToUnity()).normalized;
            var parts = MechFrame.BuildGears(content).Where(p => p.boneId == shin.id && p.shape.Contains("ay.piston")).ToList();
            Assert.That(parts.Count, Is.GreaterThan(0));
            foreach (var p in parts)
                Assert.That(Mathf.Abs(Vector3.Dot(p.rotation.ToUnity() * Vector3.up, dir)), Is.GreaterThan(0.99f), "the piston's long side follows the shin");
            Assert.AreEqual("UpperLeg", MechFrame.Role("FrontUpperLeg.R"));
            Assert.AreEqual("Gun", MechFrame.Role("Gun.L"));
            Assert.AreEqual("Body", MechFrame.Role("Body"));
        }

        [Test]
        public void AutoBind_PutsEachPieceOnItsNearestBone()
        {
            var content = new BuildContent { bones = RigTools.Fitted("Biped", new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f))) };
            // A small box at the left hand, one at the head.
            content.pieces.Add(new Piece { id = "hand", shape = "box", p = new ShapeParams { width = 0.1f, height = 0.1f, length = 0.1f }, position = new Vec3(-0.17f * 2f, 0.47f * 2f, 0.03f) });
            content.pieces.Add(new Piece { id = "head", shape = "box", p = new ShapeParams { width = 0.2f, height = 0.2f, length = 0.2f }, position = new Vec3(0f, 1.9f, 0f) });
            content.pieces.Add(new Piece { id = "pt", shape = "point", p = new ShapeParams() });
            var result = RigTools.AutoBind(content, onlyUnbound: false).ToDictionary(x => x.piece, x => x.bone);
            Assert.AreEqual("Hand.L", content.FindBone(result["hand"]).name);
            Assert.AreEqual("Head", content.FindBone(result["head"]).name);
            Assert.IsFalse(result.ContainsKey("pt"), "helpers (points) are not bound");
        }

        [Test]
        public void FromGroups_BonesFollowTheGroupParents()
        {
            var content = new BuildContent();
            content.groups.Add(new Group { id = "body", name = "Body", pivot = new Vec3(0f, 1f, 0f) });
            content.groups.Add(new Group { id = "arm", name = "LeftArm", pivot = new Vec3(0.5f, 1.5f, 0f), parentId = "body" });
            content.pieces.Add(new Piece { id = "a", shape = "box", p = new ShapeParams(), groupId = "arm", position = new Vec3(1f, 1.5f, 0f) });
            var bones = RigTools.FromGroups(content, out var changes);
            var body = bones.Single(b => b.name == "Body");
            var arm = bones.Single(b => b.name == "LeftArm");
            Assert.AreEqual(body.id, arm.parentId);
            Assert.That(arm.tail.x, Is.GreaterThan(arm.head.x), "the arm bone points toward its pieces");
            Assert.AreEqual(arm.id, changes.Single(c => c.before.id == "arm").after.boneId, "the group moves with its bone");
        }

        [Test]
        public void MoveJoint_KeepsTheChainJoined()
        {
            var a = new Bone { id = "a", name = "A", head = new Vec3(0, 0, 0), tail = new Vec3(0, 1, 0) };
            var b = new Bone { id = "b", name = "B", parentId = "a", head = new Vec3(0, 1, 0), tail = new Vec3(0, 2, 0) };
            var moved = RigTools.MoveJoint(new[] { a, b }, new Vector3(0, 1, 0), new Vector3(0.5f, 1, 0));
            Assert.AreEqual(2, moved.Count);
            Assert.AreEqual(0.5f, moved.Single(x => x.id == "a").tail.x, 1e-5f);
            Assert.AreEqual(0.5f, moved.Single(x => x.id == "b").head.x, 1e-5f);
        }

        [Test]
        public void BonesCommand_DragMergesIntoOneUndoStep()
        {
            var content = new BuildContent();
            var stack = new UndoStack(content);
            var bone = new Bone { id = "a", name = "A", head = new Vec3(0, 0, 0), tail = new Vec3(0, 1, 0) };
            stack.Execute(new BonesCommand(null, new[] { bone }, "add"));
            stack.SealMerge();
            for (int i = 1; i <= 5; i++)
            {
                var c = content.bones[0].Clone();
                c.tail = new Vec3(i * 0.1f, 1, 0);
                stack.Execute(new BonesCommand(new[] { content.bones[0] }, new[] { c }, "Move joint", "joint"));
            }
            stack.SealMerge();
            Assert.AreEqual(0.5f, content.bones[0].tail.x, 1e-5f);
            stack.Undo();
            Assert.AreEqual(0f, content.bones[0].tail.x, 1e-5f, "one undo = the whole drag");
        }

        [Test]
        public void Export_GroupHangingOnAGroup_IsNestedInIt()
        {
            var doc = ProjectDocument.CreateNew("nest", 0);
            doc.content.groups.Add(new Group { id = "body", name = "Body", pivot = new Vec3(0f, 1f, 0f) });
            doc.content.groups.Add(new Group { id = "arm", name = "LeftArm", pivot = new Vec3(0.5f, 1.5f, 0f), parentId = "body" });
            doc.content.pieces.Add(new Piece { id = "a", shape = "box", p = new ShapeParams(), groupId = "arm", position = new Vec3(1f, 1.5f, 0f) });
            doc.content.pieces.Add(new Piece { id = "b", shape = "box", p = new ShapeParams(), groupId = "body", position = new Vec3(0f, 1f, 0f) });
            var made = new List<Object>();
            var root = GlbExporter.BuildModel(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), made);
            try
            {
                var arm = root.transform.Find("Body/LeftArm");
                Assert.IsNotNull(arm, "LeftArm is inside Body");
                Assert.That(Vector3.Distance(arm.position, new Vector3(0.5f, 1.5f, 0f)), Is.LessThan(1e-4f), "and still at its pivot");
            }
            finally
            {
                made.Add(root);
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
