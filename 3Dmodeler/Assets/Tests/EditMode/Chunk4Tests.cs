using Builder.Core;
using Builder.Tools;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    /// <summary>Editing existing pieces (one undo step per drag) and the gizmo drag math.</summary>
    public class Chunk4Tests
    {
        static Piece Box(string id, float width = 1f) => new Piece { id = id, shape = "box", p = new ShapeParams { width = width } };

        static ModifyPiecesCommand SetWidth(BuildContent c, float width, string key = "edit:width")
        {
            var before = c.pieces[0];
            var after = before.Clone();
            after.p.width = width;
            return new ModifyPiecesCommand(new[] { before }, new[] { after }, "Edit width", key);
        }

        [Test]
        public void SliderDrag_IsOneUndoStep()
        {
            var c = new BuildContent();
            var stack = new UndoStack(c);
            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));

            // A slider drag: many small changes, then the mouse is released.
            stack.Execute(SetWidth(c, 1.2f));
            stack.Execute(SetWidth(c, 1.5f));
            stack.Execute(SetWidth(c, 2f));
            stack.SealMerge();
            Assert.AreEqual(2f, c.pieces[0].p.width);

            stack.Undo();
            Assert.AreEqual(1f, c.pieces[0].p.width, "one undo goes back to before the whole drag");
            stack.Redo();
            Assert.AreEqual(2f, c.pieces[0].p.width);
        }

        [Test]
        public void AfterSeal_NextEditIsNewStep()
        {
            var c = new BuildContent();
            var stack = new UndoStack(c);
            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));
            stack.Execute(SetWidth(c, 2f));
            stack.SealMerge();
            stack.Execute(SetWidth(c, 3f));

            stack.Undo();
            Assert.AreEqual(2f, c.pieces[0].p.width);
        }

        [Test]
        public void DifferentSetting_DoesNotMerge()
        {
            var c = new BuildContent();
            var stack = new UndoStack(c);
            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));
            stack.Execute(SetWidth(c, 2f, "edit:width"));
            stack.Execute(SetWidth(c, 3f, "edit:height"));   // other key -> own step

            stack.Undo();
            Assert.AreEqual(2f, c.pieces[0].p.width);
        }

        [Test]
        public void AxisParam_FindsPointUnderMouse()
        {
            // Camera at (0, 0, -10) looking at (2, 0, 0): the ray crosses the X axis at x = 2.
            var origin = new Vector3(0f, 0f, -10f);
            var ray = new Ray(origin, (new Vector3(2f, 0f, 0f) - origin).normalized);
            float? t = GizmoMath.AxisParam(ray, Vector3.zero, Vector3.right);
            Assert.IsTrue(t.HasValue);
            Assert.That(t.Value, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void AxisParam_LookingAlongAxis_IsNull()
        {
            var ray = new Ray(new Vector3(-10f, 0f, 0f), Vector3.right);
            Assert.IsFalse(GizmoMath.AxisParam(ray, Vector3.zero, Vector3.right).HasValue);
        }

        [Test]
        public void PlaneAngle_MeasuresTurn()
        {
            // Looking straight down at the ground; the point (0, 0, 1) is 90 degrees from (1, 0, 0) around +Y.
            var ray = new Ray(new Vector3(0f, 10f, 1f), Vector3.down);
            float? angle = GizmoMath.PlaneAngle(ray, Vector3.zero, Vector3.up, Vector3.right);
            Assert.IsTrue(angle.HasValue);
            Assert.That(Mathf.Abs(angle.Value), Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Snap_RoundsToStep()
        {
            Assert.That(GizmoMath.Snap(0.37f, 0.25f), Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(GizmoMath.Snap(0.4f, 0.25f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(GizmoMath.Snap(0.4f, 0f), Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void DistanceToSegment_Pixels()
        {
            Assert.That(GizmoMath.DistanceToSegment(new Vector2(5, 3), Vector2.zero, new Vector2(10, 0)), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(GizmoMath.DistanceToSegment(new Vector2(13, 4), Vector2.zero, new Vector2(10, 0)), Is.EqualTo(5f).Within(1e-4f));
        }
    }
}
