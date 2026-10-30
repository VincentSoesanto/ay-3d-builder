using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Builder.Core;
using Builder.IO;
using GLTFast;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Builder.Tests
{
    /// <summary>
    /// Exports a small model to a real .glb file, then loads that file back with glTFast (the same importer a Unity
    /// game uses) and checks that groups, pivots, sizes and colors came through.
    /// </summary>
    public class ExportRoundTripTests
    {
        static IEnumerator Wait(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
        }

        static Piece Box(string id, Vector3 pos, string color, string group) => new Piece
        {
            id = id, shape = "box", p = new ShapeParams(), position = new Vec3(pos.x, pos.y, pos.z), color = color, groupId = group,
        };

        static Color BaseColor(Material m) =>
            m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor")
            : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;

        [UnityTest]
        public IEnumerator Glb_KeepsGroupsPivotsSizesAndColors()
        {
            var doc = ProjectDocument.CreateNew("Test Mech", 0);
            doc.content.groups.Add(new Group { id = "arm", name = "LeftArm", pivot = new Vec3(1f, 2f, 0f) });
            doc.content.pieces.Add(Box("upper", new Vector3(1.5f, 1.5f, 0f), "#FF0000", "arm"));
            doc.content.pieces.Add(Box("lower", new Vector3(1.5f, 0.5f, 0f), "#0000FF", "arm"));
            var slope = Box("slope", new Vector3(1.5f, 0.5f, 1f), "#FF0000", "arm");
            slope.shape = "wedge";
            slope.mirrored = true;   // a left-handed copy must survive too
            doc.content.pieces.Add(slope);
            doc.content.pieces.Add(Box("loose", new Vector3(-3f, 0.5f, 0f), "#B0B0B0", null));

            string path = Path.Combine(Application.temporaryCachePath, "roundtrip.glb");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var export = GlbExporter.ExportAsync(doc, material, path);
            yield return Wait(export);
            Assert.IsTrue(export.Result, "export should succeed");
            Assert.IsTrue(File.Exists(path));

            var gltf = new GltfImport();
            var load = gltf.LoadFile(path);
            yield return Wait(load);
            Assert.IsTrue(load.Result, "glTFast should read the file");
            var parent = new GameObject("Imported");
            var instantiate = gltf.InstantiateMainSceneAsync(parent.transform);
            yield return Wait(instantiate);

            var all = parent.GetComponentsInChildren<Transform>(true);
            var arm = all.FirstOrDefault(t => t.name == "LeftArm");
            Assert.IsNotNull(arm, "group LeftArm exported as its own object");
            Assert.IsNotNull(all.FirstOrDefault(t => t.name == "Ungrouped"), "ungrouped pieces exported");

            // The group's origin is its pivot (1, 2, 0); 1 unit = 1 meter.
            Assert.That(Vector3.Distance(arm.position, new Vector3(1f, 2f, 0f)), Is.LessThan(1e-3f), $"pivot at {arm.position}");

            // One mesh for the whole group: from the pivot, x 0..1, y -2..0, z -0.5..1.5.
            var renderer = arm.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(renderer, "group has a mesh");
            var bounds = renderer.bounds;
            Assert.That(Vector3.Distance(bounds.min, new Vector3(1f, 0f, -0.5f)), Is.LessThan(1e-3f), $"min {bounds.min}");
            Assert.That(Vector3.Distance(bounds.max, new Vector3(2f, 2f, 1.5f)), Is.LessThan(1e-3f), $"max {bounds.max}");

            // One material per color (red + blue), with the right colors.
            var colors = renderer.sharedMaterials.Select(BaseColor).ToList();
            Assert.AreEqual(2, colors.Count, "two colors -> two materials");
            Assert.IsTrue(colors.Any(c => c.r > 0.9f && c.g < 0.1f && c.b < 0.1f), "red survived");
            Assert.IsTrue(colors.Any(c => c.b > 0.9f && c.r < 0.1f && c.g < 0.1f), "blue survived");

            Object.Destroy(parent);
            gltf.Dispose();
            File.Delete(path);
        }
    }
}
