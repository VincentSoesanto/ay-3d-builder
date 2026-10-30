using System.IO;
using System.Linq;
using Builder.Core;
using Builder.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>Groups (with undo) and the editor import of an exported .glb (like dragging it into a Unity project).</summary>
    public class Chunk6Tests
    {
        static Piece Box(string id, float x, string group = null) =>
            new Piece { id = id, shape = "box", p = new ShapeParams(), position = new Vec3(x, 0.5f, 0f), groupId = group };

        [Test]
        public void Group_CreateRenameDelete_AllUndo()
        {
            var c = new BuildContent();
            c.pieces.Add(Box("a", 0f));
            c.pieces.Add(Box("b", 1f));
            var stack = new UndoStack(c);

            var arm = new Group { id = "g", name = "LeftArm" };
            stack.Execute(new GroupCommand(null, arm, "New", new[] { ("a", (string)null, "g"), ("b", (string)null, "g") }));
            Assert.AreEqual(1, c.groups.Count);
            Assert.AreEqual("g", c.FindPiece("a").groupId);

            var renamed = arm.Clone();
            renamed.name = "RightArm";
            stack.Execute(new GroupCommand(arm, renamed, "Rename"));
            Assert.AreEqual("RightArm", c.groups[0].name);

            stack.Execute(new GroupCommand(renamed, null, "Delete", new[] { ("a", "g", (string)null), ("b", "g", (string)null) }));
            Assert.AreEqual(0, c.groups.Count);
            Assert.IsNull(c.FindPiece("a").groupId, "pieces stay, ungrouped");

            stack.Undo();   // delete
            Assert.AreEqual("RightArm", c.groups[0].name);
            Assert.AreEqual("g", c.FindPiece("b").groupId);
            stack.Undo();   // rename
            Assert.AreEqual("LeftArm", c.groups[0].name);
            stack.Undo();   // create
            Assert.AreEqual(0, c.groups.Count);
            Assert.IsNull(c.FindPiece("a").groupId);
        }

        [Test]
        public void Ungroup_MovesPiecesOutWithoutAGroup()
        {
            var c = new BuildContent();
            c.groups.Add(new Group { id = "g", name = "Body" });
            c.pieces.Add(Box("a", 0f, "g"));
            var stack = new UndoStack(c);
            stack.Execute(new GroupCommand(null, null, "Ungroup", new[] { ("a", "g", (string)null) }));
            Assert.IsNull(c.FindPiece("a").groupId);
            stack.Undo();
            Assert.AreEqual("g", c.FindPiece("a").groupId);
        }

        [Test]
        public void ExportedGlb_ImportsInTheEditor_WithNamedParts()
        {
            const string folder = "Assets/_ExportCheck";
            string path = folder + "/check.glb";
            try
            {
                Directory.CreateDirectory(folder);
                var doc = ProjectDocument.CreateNew("Check", 0);
                doc.content.groups.Add(new Group { id = "h", name = "Head", pivot = new Vec3(0f, 3f, 0f) });
                doc.content.pieces.Add(Box("a", 0f, "h"));
                doc.content.pieces.Add(Box("b", 2f));

                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                var task = GlbExporter.ExportAsync(doc, material, path, forceSync: true);
                Assert.IsTrue(task.IsCompleted && task.Result, "export (in one go) should succeed");

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, "the editor's glTF importer turned the file into a model");
                var names = model.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
                CollectionAssert.Contains(names, "Head");
                CollectionAssert.Contains(names, "Ungrouped");
                var head = model.GetComponentsInChildren<Transform>(true).First(t => t.name == "Head");
                Assert.That(Vector3.Distance(head.position, new Vector3(0f, 3f, 0f)), Is.LessThan(1e-3f), "pivot kept");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
