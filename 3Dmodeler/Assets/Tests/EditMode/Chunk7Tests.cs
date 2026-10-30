using System;
using System.IO;
using System.Linq;
using Builder.Core;
using Builder.IO;
using NUnit.Framework;

namespace Builder.Tests
{
    /// <summary>Project files: save, list, open, delete (to trash), export/import, and old/new file formats.</summary>
    public class Chunk7Tests
    {
        string temp;

        [SetUp]
        public void UseTempFolder()
        {
            temp = Path.Combine(Path.GetTempPath(), "AY3DBuilderTests_" + Guid.NewGuid().ToString("N"));
            ProjectStore.RootOverride = temp;   // never touch the real projects
        }

        [TearDown]
        public void Cleanup()
        {
            ProjectStore.RootOverride = null;
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }

        static ProjectDocument SampleMech()
        {
            var doc = ProjectDocument.CreateNew("Test Mech", 1000);
            doc.content.groups.Add(new Group { id = "g", name = "LeftArm", pivot = new Vec3(1f, 2f, 0f), visible = false });
            doc.content.pieces.Add(new Piece { id = "a", shape = "wedge", p = new ShapeParams { width = 2f, bevel = 0.1f },
                position = new Vec3(1.5f, 0.5f, -2f), rotation = new Quat(0f, 0.7071068f, 0f, 0.7071068f),
                mirrored = true, color = "#C0392B", groupId = "g", mirrorPartnerId = "b" });
            doc.editor.snapMode = SnapMode.Free;
            doc.editor.gridSize = 0.5f;
            doc.palette.Add("#123456");
            return doc;
        }

        [Test]
        public void Save_Load_KeepsEverything()
        {
            var doc = SampleMech();
            ProjectStore.Save(doc);
            var back = ProjectStore.Load(doc.id);

            // Same document after a save + load (compare their JSON).
            Assert.AreEqual(ProjectStore.ToJson(doc), ProjectStore.ToJson(back));
            Assert.AreEqual(1, back.revision, "each save counts up");
            Assert.AreEqual(SnapMode.Free, back.editor.snapMode);
            Assert.IsTrue(back.content.pieces[0].mirrored);
            Assert.IsFalse(back.content.groups[0].visible);
        }

        [Test]
        public void Json_UsesWordsForModes_AndHasSchemaVersion()
        {
            string json = ProjectStore.ToJson(SampleMech());
            StringAssert.Contains("\"snapMode\":\"Free\"", json);
            StringAssert.Contains("\"schemaVersion\":1", json);
        }

        [Test]
        public void List_NewestFirst_Delete_GoesToTrash()
        {
            var older = SampleMech();
            ProjectStore.Save(older);
            System.Threading.Thread.Sleep(5);
            var newer = SampleMech();
            newer.id = Ids.New();
            newer.name = "Second";
            ProjectStore.Save(newer);

            var list = ProjectStore.List();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("Second", list[0].name);

            ProjectStore.Delete(older.id);
            Assert.IsFalse(ProjectStore.Exists(older.id));
            Assert.AreEqual(1, Directory.GetFiles(ProjectStore.TrashFolder).Length, "deleted file is kept in the trash");
        }

        [Test]
        public void Export_Then_Import_MakesANewProject()
        {
            var doc = SampleMech();
            ProjectStore.Save(doc);
            string file = ProjectStore.Export(doc);
            Assert.IsTrue(File.Exists(file));
            CollectionAssert.Contains(ProjectStore.ImportCandidates(), file);

            var imported = ProjectStore.Import(file);
            Assert.AreNotEqual(doc.id, imported.id, "import never overwrites the original");
            Assert.AreEqual(2, ProjectStore.List().Count);
            Assert.AreEqual(doc.content.pieces[0].color, imported.content.pieces[0].color);
        }

        [Test]
        public void FileFromNewerApp_IsRefused()
        {
            string json = ProjectStore.ToJson(SampleMech()).Replace("\"schemaVersion\":1", "\"schemaVersion\":99");
            var e = Assert.Throws<InvalidOperationException>(() => ProjectStore.FromJson(json));
            StringAssert.Contains("newer version", e.Message);
        }

        [Test]
        public void LastId_IsRemembered()
        {
            ProjectStore.LastId = "abc";
            Assert.AreEqual("abc", ProjectStore.LastId);
        }
    }
}
