using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class RigTests
    {
        static Bone B(string id, string name, string parent, Vector3 head, Vector3 tail) =>
            new Bone { id = id, name = name, parentId = parent, head = head.ToDoc(), tail = tail.ToDoc() };

        [Test]
        public void Names_FollowTheParent()
        {
            var c = new BuildContent();
            Assert.AreEqual("Hips", BoneNames.Suggest(c, null, 0f));
            var hips = B("h", "Hips", null, Vector3.zero, Vector3.up);
            c.bones.Add(hips);
            Assert.AreEqual("Spine", BoneNames.Suggest(c, hips, 0f));
            c.bones.Add(B("s", "Spine", "h", Vector3.up, Vector3.up * 2));
            Assert.AreEqual("UpperLeg.L", BoneNames.Suggest(c, hips, -0.3f), "spine taken: a leg; facing +Z, -X is the left side");
            Assert.AreEqual("UpperLeg.R", BoneNames.Suggest(c, hips, 0.3f));
            var arm = B("a", "UpperArm.R", null, Vector3.zero, Vector3.left);
            Assert.AreEqual("LowerArm.R", BoneNames.Suggest(c, arm, 5f), "keeps the parent's side");
            Assert.AreEqual("Hand.L", BoneNames.Mirror("Hand.R"));
            Assert.AreEqual("Bone 3", BoneNames.Suggest(c, B("x", "Tail", null, Vector3.zero, Vector3.up), 0f));
        }

        [Test]
        public void Pose_TurnsAroundTheHead_ChildrenFollow()
        {
            var c = new BuildContent();
            c.bones.Add(B("a", "UpperArm.L", null, new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f)));
            c.bones.Add(B("b", "LowerArm.L", "a", new Vector3(1f, 1f, 0f), new Vector3(2f, 1f, 0f)));
            var turns = new Dictionary<string, Vector3> { ["a"] = new Vector3(0f, 0f, 90f) };
            var m = Rig.PoseMatrices(c, id => turns.TryGetValue(id, out var t) ? t : Vector3.zero);

            Assert.That(Vector3.Distance(m["a"].MultiplyPoint3x4(new Vector3(1f, 1f, 0f)), new Vector3(0f, 2f, 0f)), Is.LessThan(1e-4f), "arm points up now");
            Assert.That(Vector3.Distance(m["b"].MultiplyPoint3x4(new Vector3(2f, 1f, 0f)), new Vector3(0f, 3f, 0f)), Is.LessThan(1e-4f), "the lower arm went along");
        }

        [Test]
        public void Limits_ClampThePose()
        {
            var b = B("a", "Neck", null, Vector3.zero, Vector3.up);
            b.limits = new AxisLimits { min = new Vec3(-30f, -10f, 0f), max = new Vec3(45f, 10f, 0f) };
            Assert.AreEqual(45f, Rig.Clamp(b, 0, 80f));
            Assert.AreEqual(-10f, Rig.Clamp(b, 1, -50f));
            Assert.AreEqual(0f, Rig.Clamp(b, 2, 20f));
            Assert.AreEqual(170f, Rig.Clamp(B("n", "x", null, Vector3.zero, Vector3.up), 0, 170f), "no limits: free");
        }

        [Test]
        public void MirrorBones_SwapSides_AndFindTheirParents()
        {
            var c = new BuildContent();
            c.bones.Add(B("chest", "Chest", null, new Vector3(0f, 1f, 0f), new Vector3(0f, 1.5f, 0f)));
            var upper = B("u", "UpperArm.L", "chest", new Vector3(0.3f, 1.4f, 0f), new Vector3(0.7f, 1.4f, 0f));
            var lower = B("l", "LowerArm.L", "u", new Vector3(0.7f, 1.4f, 0f), new Vector3(1.1f, 1.4f, 0f));
            var ids = new Dictionary<string, string>();
            var all = Rig.WithMirrorBones(new List<Bone> { upper, lower }, c, ids);

            Assert.AreEqual(4, all.Count);
            var upperR = all.Single(b => b.name == "UpperArm.R");
            var lowerR = all.Single(b => b.name == "LowerArm.R");
            Assert.AreEqual("chest", upperR.parentId, "the chest is on the middle line: both arms hang on it");
            Assert.AreEqual(upperR.id, lowerR.parentId);
            Assert.AreEqual(-0.3f, upperR.head.x, 1e-5f);
            Assert.AreEqual(upperR.id, ids["u"]);
        }

        [Test]
        public void BonesCommand_AddChangeDelete_Undo()
        {
            var c = new BuildContent();
            var a = B("a", "Hips", null, Vector3.zero, Vector3.up);
            var add = new BonesCommand(null, new[] { a }, "add");
            Assert.IsTrue(add.Do(c).boneIds.Contains("a"));
            var renamed = a.Clone();
            renamed.name = "Root";
            var change = new BonesCommand(new[] { a }, new[] { renamed }, "rename");
            change.Do(c);
            Assert.AreEqual("Root", c.bones.Single().name);
            var delete = new BonesCommand(new[] { renamed }, null, "delete");
            delete.Do(c);
            Assert.AreEqual(0, c.bones.Count);
            delete.Undo(c);
            change.Undo(c);
            Assert.AreEqual("Hips", c.bones.Single().name);
            add.Undo(c);
            Assert.AreEqual(0, c.bones.Count);
        }

        [Test]
        public void Export_PutsBoundPartsInsideTheirBones()
        {
            var doc = ProjectDocument.CreateNew("rig", 0);
            var c = doc.content;
            c.bones.Add(B("h", "Hips", null, new Vector3(0f, 1f, 0f), new Vector3(0f, 1.3f, 0f)));
            c.bones.Add(B("s", "Spine", "h", new Vector3(0f, 1.3f, 0f), new Vector3(0f, 1.7f, 0f)));
            c.groups.Add(new Group { id = "g", name = "Body", pivot = new Vec3(0f, 1.3f, 0f), boneId = "s" });
            c.pieces.Add(new Piece { id = "body", shape = "box", p = new ShapeParams(), position = new Vec3(0f, 1.5f, 0f), groupId = "g" });
            c.pieces.Add(new Piece { id = "pelvis", shape = "box", p = new ShapeParams(), position = new Vec3(0f, 1f, 0f), boneId = "h" });
            c.pieces.Add(new Piece { id = "loose", shape = "box", p = new ShapeParams(), position = new Vec3(3f, 0f, 0f) });

            var made = new List<Object>();
            var root = GlbExporter.BuildModel(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), made);
            try
            {
                var hips = root.transform.Find("Armature/Hips");
                Assert.IsNotNull(hips, "bones are inside the Armature");
                Assert.AreEqual(new Vector3(0f, 1f, 0f), hips.position);
                var spine = hips.Find("Spine");
                Assert.IsNotNull(spine, "Spine is inside Hips");
                Assert.IsNotNull(spine.Find("Body"), "the group bound to Spine is inside Spine");
                Assert.AreEqual(new Vector3(0f, 1.3f, 0f), spine.Find("Body").position, "at its pivot");
                Assert.IsNotNull(hips.Find("Hips Mesh"), "a piece bound on its own goes into '<bone> Mesh'");
                Assert.IsNotNull(root.transform.Find("Ungrouped"), "unbound, ungrouped pieces as before");
            }
            finally
            {
                made.Add(root);
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
