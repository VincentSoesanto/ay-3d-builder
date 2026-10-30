using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.Shapes;
using Builder.Tools;
using Builder.View;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class ModelAndFlipTests
    {
        [Test]
        public void ModelColors_CloseShadesMerge_FarColorsStay()
        {
            var tris = new List<Color32>();
            for (int i = 0; i < 30; i++) tris.Add(new Color32((byte)(200 + i % 5), 20, 20, 255));   // reds (a texture's shades)
            for (int i = 0; i < 10; i++) tris.Add(new Color32(20, 30, 210, 255));                  // blue
            var (colors, index) = Builder.IO.ModelColors.Merge(tris);
            Assert.AreEqual(2, colors.Length, string.Join(" ", colors));
            Assert.That(colors[0], Does.StartWith("#C"), "the biggest color (red) first: " + colors[0]);
            Assert.AreEqual(0, index[0]);
            Assert.AreEqual(1, index[35]);

            var many = new List<Color32>();
            for (int i = 0; i < 40; i++) many.Add(new Color32((byte)(i * 6), (byte)(255 - i * 6), (byte)(i * 37 % 256), 255));
            Assert.That(Builder.IO.ModelColors.Merge(many).colors.Length, Is.LessThanOrEqualTo(Builder.IO.ModelColors.MaxColors));
        }

        [Test]
        public void ObjReader_ReadsColorsFromTheMtl()
        {
            const string obj = "mtllib two.mtl\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nusemtl Red\nf 1 2 3\nusemtl Blue\nf 1 3 4\n";
            const string mtl = "newmtl Red\nKd 1 0 0\nnewmtl Blue\nKd 0 0 1\n";
            var (_, triangles, colors) = Builder.IO.ObjReader.ReadColored(obj, name => name == "two.mtl" ? mtl : throw new System.IO.FileNotFoundException());
            Assert.AreEqual(2, triangles.Count / 3);
            Assert.AreEqual(new Color32(255, 0, 0, 255), colors[0]);
            Assert.AreEqual(new Color32(0, 0, 255, 255), colors[1]);
            Assert.IsNull(Builder.IO.ObjReader.ReadColored("v 0 0 0\nv 1 0 0\nv 1 1 0\nf 1 2 3\n", null).colors, "no materials: no colors");
        }

        // A 2 x 1 x 1 box as Blender writes it (right-handed, faces counter-clockwise from outside, quads).
        public const string ObjBox = @"# test box
v -1 -0.5 -0.5
v 1 -0.5 -0.5
v 1 0.5 -0.5
v -1 0.5 -0.5
v -1 -0.5 0.5
v 1 -0.5 0.5
v 1 0.5 0.5
v -1 0.5 0.5
f 1 4 3 2
f 5 6 7 8
f 1 2 6 5
f 4 8 7 3
f 1 5 8 4
f 2 3 7 6
";

        static float Volume(Mesh m)
        {
            var v = m.vertices;
            var t = m.triangles;
            float sum = 0f;
            for (int i = 0; i < t.Length; i += 3) sum += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            return sum / 6f;
        }

        [Test]
        public void Obj_ReadsQuadsAsTriangles_FacingOutward()
        {
            var (vertices, triangles) = ObjReader.Read(ObjBox);
            Assert.AreEqual(8, vertices.Count);
            Assert.AreEqual(12, triangles.Count / 3);

            var model = ModelLibrary.FromTriangles("box", vertices, triangles);
            var shape = new ModelShape(model);
            var mb = new MeshBuilder();
            shape.Build(shape.CreateDefaults(), mb);
            var mesh = mb.ToMesh("obj", false);
            // Same sign as the app's own box = faces point outward the same way.
            float boxSign = Mathf.Sign(Volume(ShapeRegistry.GetPieceMesh(new Piece { id = "b", shape = "box", p = new ShapeParams() })));
            Assert.That(Volume(mesh) * boxSign, Is.EqualTo(2f).Within(1e-3f), "closed, faces pointing out");
            Assert.That(Vector3.Distance(mesh.bounds.size, new Vector3(2f, 1f, 1f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void ModelShape_StretchesWithSize()
        {
            var (v, t) = ObjReader.Read(ObjBox);
            var shape = new ModelShape(ModelLibrary.FromTriangles("box", v, t));
            var mb = new MeshBuilder();
            shape.Build(new ShapeParams { width = 4f, height = 1f, length = 1f }, mb);
            Assert.That(Mathf.Abs(Volume(mb.ToMesh("m", false))), Is.EqualTo(4f).Within(1e-3f));
        }

        [Test]
        public void Flip_UpDown_IsExactMirrorImage()
        {
            // A turned slope, flipped up/down (across Y) the way J does it: rotation + the piece's own mirror.
            var rotation = Quaternion.Euler(0f, 30f, 0f);
            var piece = new Piece { id = "a", shape = "wedge", p = new ShapeParams() };
            var flippedRotation = BuildTool.FlipRotation(rotation, Vector3.up);
            var flipped = MirrorEdits.MirrorOf(piece, "b");   // what the stamp does when Flipped

            var original = ShapeRegistry.GetPieceMesh(piece).vertices.Select(c => rotation * c).ToList();
            var mirrored = ShapeRegistry.GetPieceMesh(flipped).vertices
                .Select(c => flippedRotation * (flipped.rotation.ToUnity() * c + flipped.position.ToUnity())).ToList();
            foreach (var o in original.Distinct())
            {
                var expected = new Vector3(o.x, -o.y, o.z);   // mirrored up/down
                Assert.That(mirrored.Min(m => Vector3.Distance(m, expected)), Is.LessThan(1e-4f), $"no flipped corner near {expected}");
            }
        }
    }
}
