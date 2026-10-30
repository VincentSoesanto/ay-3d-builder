using System;
using System.IO;
using Builder.Core;
using Builder.IO;
using NUnit.Framework;

namespace Builder.Tests
{
    /// <summary>The blueprint library files (in a temp folder, never the real library).</summary>
    public class BlueprintTests
    {
        string temp;

        [SetUp]
        public void UseTempFolder()
        {
            temp = Path.Combine(Path.GetTempPath(), "AY3DBuilderBpTests_" + Guid.NewGuid().ToString("N"));
            ProjectStore.RootOverride = temp;
        }

        [TearDown]
        public void Cleanup()
        {
            ProjectStore.RootOverride = null;
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }

        static Blueprint Hand(string name, string category)
        {
            var bp = new Blueprint { id = Ids.New(), name = name, category = category, version = 1 };
            bp.content.pieces.Add(new Piece { id = "f1", shape = "box", p = new ShapeParams { width = 0.2f }, position = new Vec3(0f, 0.5f, 0f), color = "#FF0000" });
            bp.content.pieces.Add(new Piece { id = "f2", shape = "wedge", p = new ShapeParams(), position = new Vec3(0.3f, 0.5f, 0f), mirrored = true });
            return bp;
        }

        [Test]
        public void Save_LoadAll_SortedByCategoryThenName()
        {
            BlueprintStore.Save(Hand("Zeta hand", "Arms"));
            BlueprintStore.Save(Hand("Alpha hand", "Arms"), new byte[] { 1, 2, 3 });
            BlueprintStore.Save(Hand("Cannon", "Weapons"));

            var all = BlueprintStore.LoadAll();
            Assert.AreEqual(3, all.Count);
            Assert.AreEqual("Alpha hand", all[0].name);
            Assert.AreEqual("Zeta hand", all[1].name);
            Assert.AreEqual("Cannon", all[2].name);
            Assert.AreEqual(2, all[0].content.pieces.Count);
            Assert.IsTrue(all[0].content.pieces[1].mirrored);
            Assert.IsTrue(File.Exists(BlueprintStore.PicturePath(all[0].id)), "picture saved next to it");
        }

        [Test]
        public void Delete_MovesToTrash()
        {
            var bp = Hand("Hand", "Arms");
            BlueprintStore.Save(bp, new byte[] { 1 });
            BlueprintStore.Delete(bp.id);
            Assert.AreEqual(0, BlueprintStore.LoadAll().Count);
            Assert.AreEqual(2, Directory.GetFiles(Path.Combine(BlueprintStore.Folder, "trash")).Length, "json + picture kept in the trash");
        }
    }
}
