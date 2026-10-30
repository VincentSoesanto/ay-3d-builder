using Builder.Core;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Builder.Tests
{
    /// <summary>Checks that a project survives being written to JSON and read back.</summary>
    public class DocumentJsonTests
    {
        [Test]
        public void Project_RoundTripsThroughJson()
        {
            var doc = ProjectDocument.CreateNew("Test mech", 1000);
            doc.content.pieces.Add(new Piece
            {
                id = Ids.New(),
                shape = "box",
                position = new Vec3(1, 2, 3),
                color = "#FF0000",
            });

            string json = JsonConvert.SerializeObject(doc, Formatting.Indented);
            var back = JsonConvert.DeserializeObject<ProjectDocument>(json);

            Assert.AreEqual(ProjectDocument.CurrentSchemaVersion, back.schemaVersion);
            Assert.AreEqual("Test mech", back.name);
            Assert.AreEqual(1, back.content.pieces.Count);
            Assert.AreEqual(2f, back.content.pieces[0].position.y);
            Assert.AreEqual("#FF0000", back.content.pieces[0].color);
        }
    }
}
