using System.Linq;
using Builder.Core;
using Builder.Shapes;
using Builder.Tools;
using NUnit.Framework;
using UnityEngine;

namespace Builder.Tests
{
    public class PlacementMathTests
    {
        static readonly Bounds UnitBox = new Bounds(Vector3.zero, Vector3.one);   // 1 x 1 x 1 m, centered
        static readonly Vector3[] UnitCorners = PlacementMath.SnapPoints(UnitBox).Where(p =>
            Mathf.Abs(p.x) > 0.4f && Mathf.Abs(p.y) > 0.4f && Mathf.Abs(p.z) > 0.4f).ToArray();

        static void AreClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), $"expected {expected}, got {actual}");

        static Vector3 PlaceOnGrid(Vector3 hit, Vector3 normal, Vector3[] corners, Bounds b, Quaternion rot, float grid)
        {
            var pos = PlacementMath.SurfacePosition(hit, normal, corners, b, rot);
            return PlacementMath.SnapToGrid(pos, normal, b, rot, grid);
        }

        [Test]
        public void OnFloor_SitsOnTop_AndSnapsToGrid()
        {
            // Aim at the floor at (0.3, 0, 0.6) with a 0.5 m grid.
            // x: corner 0.3 - 0.5 = -0.2 snaps to 0, so center x = 0.5. z: corner 0.1 snaps to 0, so center z = 0.5.
            var pos = PlaceOnGrid(new Vector3(0.3f, 0f, 0.6f), Vector3.up, UnitCorners, UnitBox, Quaternion.identity, 0.5f);
            AreClose(new Vector3(0.5f, 0.5f, 0.5f), pos);
        }

        [Test]
        public void OnSideOfBox_AttachesNextToIt()
        {
            // A box sits at (0.5, 0.5, 0.5). Aim at its +X face (x = 1).
            var pos = PlaceOnGrid(new Vector3(1f, 0.4f, 0.6f), Vector3.right, UnitCorners, UnitBox, Quaternion.identity, 0.25f);
            AreClose(new Vector3(1.5f, 0.5f, 0.5f), pos);
        }

        [Test]
        public void RotatedLongPiece_UsesRotatedSize()
        {
            // 2 m long (Z) beam turned 90 degrees now lies along X.
            var beam = new Bounds(Vector3.zero, new Vector3(0.5f, 0.5f, 2f));
            var corners = PlacementMath.SnapPoints(beam).Where(p => Mathf.Abs(p.x) > 0.2f && Mathf.Abs(p.y) > 0.2f && Mathf.Abs(p.z) > 0.9f).ToArray();
            var rot = Quaternion.Euler(0f, 90f, 0f);
            var pos = PlaceOnGrid(Vector3.zero, Vector3.up, corners, beam, rot, 0.25f);
            AreClose(new Vector3(0f, 0.25f, 0f), pos);   // corner x = -1 is on the grid, y = half height
            AreClose(new Vector3(2f, 0.5f, 0.5f), PlacementMath.RotatedExtents(beam.extents, rot) * 2f);
        }

        [Test]
        public void UpsideDownSlope_OnSlopeFace_CompletesTheBox()
        {
            // First slope sits at (0.5, 0.5, 0.5). Its slope face goes from the front-bottom edge to the back-top edge,
            // so it faces up and to the front. Aim at the middle of that face with a slope turned 180 degrees around X.
            var mesh = ShapeRegistry.GetMesh("wedge", new ShapeParams(), false);
            var corners = mesh.vertices.Distinct().ToArray();
            var normal = new Vector3(0f, 1f, -1f).normalized;
            var rot = Quaternion.Euler(180f, 0f, 0f);

            var pos = PlacementMath.SurfacePosition(new Vector3(0.5f, 0.5f, 0.5f), normal, corners, mesh.bounds, rot);
            AreClose(new Vector3(0.5f, 0.5f, 0.5f), pos);                     // fits exactly: no floating
            AreClose(pos, PlacementMath.SnapToGrid(pos, normal, mesh.bounds, rot, 0.25f));   // and stays there on the grid
        }

        [Test]
        public void FreeMode_SnapsCornerToNearbyCorner()
        {
            // Target box at origin. New box placed slightly off the top: (0.1, 1, 0.05) -> should snap to (0, 1, 0).
            var snapped = PlacementMath.SnapToPoints(new Vector3(0.1f, 1f, 0.05f), Quaternion.identity, UnitBox,
                                                     Vector3.zero, Quaternion.identity, UnitBox, 0.3f);
            Assert.IsTrue(snapped.HasValue);
            AreClose(new Vector3(0f, 1f, 0f), snapped.Value);
        }

        [Test]
        public void FreeMode_NoSnapWhenFarAway()
        {
            var snapped = PlacementMath.SnapToPoints(new Vector3(0.25f, 1f, 0.25f), Quaternion.identity, UnitBox,
                                                     Vector3.zero, Quaternion.identity, UnitBox, 0.1f);
            Assert.IsFalse(snapped.HasValue);
        }

        [Test]
        public void AttachPoints_AreCornersThenFacesThenEdges()
        {
            var points = PlacementMath.AttachPoints;
            Assert.AreEqual(26, points.Length);
            Assert.AreEqual("Top front left corner", PlacementMath.AttachPointName(points[0]));
            Assert.AreEqual("Top face", PlacementMath.AttachPointName(points[8]));
            Assert.AreEqual("Top front edge", PlacementMath.AttachPointName(points[14]));
        }

        [Test]
        public void AttachAt_PutsChosenCornerOnNearestTargetPoint()
        {
            // Ghost attaches by its bottom-front-left corner; we aim near the target's top-back-right corner.
            var pos = PlacementMath.AttachAt(Quaternion.identity, UnitBox, new Vector3Int(-1, -1, -1),
                                             Vector3.zero, Quaternion.identity, UnitBox, new Vector3(0.45f, 0.5f, 0.4f));
            AreClose(new Vector3(1f, 1f, 1f), pos);

            // Same aim, but attach by the bottom-front-RIGHT corner: the ghost moves left, still turned the same way.
            pos = PlacementMath.AttachAt(Quaternion.identity, UnitBox, new Vector3Int(1, -1, -1),
                                         Vector3.zero, Quaternion.identity, UnitBox, new Vector3(0.45f, 0.5f, 0.4f));
            AreClose(new Vector3(0f, 1f, 1f), pos);
        }

        [Test]
        public void Eraser_GoesIntoTheAimedSurface()
        {
            // A box sits at (0.5, 0.5, 0.5). Aiming at its top (y = 1) with the eraser (normal turned around)
            // must put a 1 m eraser exactly where the box is, not on top of it.
            var down = -Vector3.up;
            var pos = PlaceOnGrid(new Vector3(0.4f, 1f, 0.6f), down, UnitCorners, UnitBox, Quaternion.identity, 0.5f);
            AreClose(new Vector3(0.5f, 0.5f, 0.5f), pos);
        }

        [Test]
        public void GridSizes_Cycle()
        {
            Assert.AreEqual(0.5f, PlacementMath.NextGridSize(1f));
            Assert.AreEqual(0.1f, PlacementMath.NextGridSize(0.25f));
            Assert.AreEqual(1f, PlacementMath.NextGridSize(0.1f));
        }
    }
}
