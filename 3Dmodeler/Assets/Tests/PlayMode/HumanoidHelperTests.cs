using System.Collections;
using System.IO;
using System.Linq;
using AY3DBuilder;
using Builder.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Builder.Tests
{
    /// <summary>The Unity helper for games: a rigged mech exported as .glb becomes a Humanoid that animations can move.</summary>
    public class HumanoidHelperTests
    {
        static ProjectDocument Mech(bool oldNames)
        {
            var doc = ProjectDocument.CreateNew("RigTest", 0);
            Piece Box(string id, float x, float y, float w, float h) =>
                new Piece { id = id, shape = "box", p = new ShapeParams { width = w, height = h, length = 0.6f }, position = new Vec3(x, y, 0f) };
            doc.content.pieces.AddRange(new[]
            {
                Box("body", 0f, 4.3f, 1.4f, 1.6f), Box("head", 0f, 5.6f, 0.6f, 0.6f),
                Box("armL", -0.8f, 4.1f, 0.3f, 1.4f), Box("armR", 0.8f, 4.1f, 0.3f, 1.4f),
                Box("legL", -0.4f, 1.8f, 0.4f, 3.4f), Box("legR", 0.4f, 1.8f, 0.4f, 3.4f),
            });
            var bounds = new Bounds(new Vector3(0f, 3f, 0f), new Vector3(2f, 6f, 1f));
            doc.content.bones = RigTools.Fitted("Biped", bounds);
            if (oldNames) foreach (var b in doc.content.bones) b.name = BoneNames.Mirror(b.name);   // .L on +X, like before the fix
            foreach (var (piece, bone) in RigTools.AutoBind(doc.content, onlyUnbound: false))
                doc.content.FindPiece(piece).boneId = bone;
            return doc;
        }

        static IEnumerator Load(ProjectDocument doc, System.Action<GameObject> done)
        {
            string path = Path.Combine(Path.GetTempPath(), "AY3DHumanoid_" + System.Guid.NewGuid().ToString("N") + ".glb");
            var export = Builder.IO.GlbExporter.ExportAsync(doc, new Material(Shader.Find("Universal Render Pipeline/Lit")), path);
            while (!export.IsCompleted) yield return null;
            var gltf = new GLTFast.GltfImport();
            var load = gltf.LoadFile(path);
            while (!load.IsCompleted) yield return null;
            var holder = new GameObject("Mech");
            var make = gltf.InstantiateMainSceneAsync(holder.transform);
            while (!make.IsCompleted) yield return null;
            File.Delete(path);
            done(holder);
        }

        [UnityTest]
        public IEnumerator ExportedMech_BecomesAHumanoid_AnimationsMoveItsParts([Values(false, true)] bool oldNames)
        {
            GameObject mech = null;
            yield return Load(Mech(oldNames), m => mech = m);
            var helper = mech.AddComponent<AYMechHumanoid>();   // Awake builds the avatar
            Assert.AreEqual("", helper.Problem, helper.Problem);
            Assert.IsTrue(helper.Avatar.isHuman && helper.Avatar.isValid);

            var animator = mech.GetComponent<Animator>();
            var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Assert.That(leftArm.position.x, Is.LessThan(hips.position.x), "Unity's LEFT arm is the one at -X (facing +Z)");

            // Move the left arm the way an animation would (Unity's "muscles"): its part must move along.
            var armPart = leftArm.GetComponentsInChildren<MeshRenderer>().First();
            var before = armPart.bounds.center;
            var rightPart = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).GetComponentsInChildren<MeshRenderer>().First();
            var rightBefore = rightPart.bounds.center;
            var handler = new HumanPoseHandler(helper.Avatar, mech.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            int muscle = System.Array.IndexOf(HumanTrait.MuscleName, "Left Arm Down-Up");
            pose.muscles[muscle] = 1f;   // arm up
            handler.SetHumanPose(ref pose);
            float left = Vector3.Distance(before, armPart.bounds.center);
            float right = Vector3.Distance(rightBefore, rightPart.bounds.center);
            Assert.That(left, Is.GreaterThan(0.2f), "the left arm's part moved with the bone");
            Assert.That(right, Is.LessThan(left * 0.4f), "the right arm hardly moved (Unity's joint limits nudge it a little)");
            handler.Dispose();
            Object.Destroy(mech);
        }
    }
}
