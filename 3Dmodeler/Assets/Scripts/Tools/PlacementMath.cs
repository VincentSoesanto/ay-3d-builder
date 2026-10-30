using System.Collections.Generic;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Works out where a new piece goes when you aim at a surface. Pure math (no scene objects), so it can be tested.
    ///
    /// Steps: 1) <see cref="SurfacePosition"/> puts the piece against the aimed surface, touching it;
    ///        2a) GRID mode: <see cref="SnapToGrid"/> lines its edges up with the grid, or
    ///        2b) SNAP mode: <see cref="SnapToPoints"/> pulls a corner/edge/face point onto the same kind of point
    ///            of the aimed piece (Valheim style), if one is close.
    /// </summary>
    public static class PlacementMath
    {
        /// <summary>The grid sizes the G key / D-pad up cycles through, in meters.</summary>
        public static readonly float[] GridSizes = { 1f, 0.5f, 0.25f, 0.1f };

        /// <summary>
        /// Position for a piece so it touches the aimed surface from outside, with its middle in front of the hit point.
        /// Uses the piece's real corner points, so a turned slope fits flat against another slope.
        /// </summary>
        /// <param name="hitPoint">Where the crosshair ray hit.</param>
        /// <param name="hitNormal">The hit surface's outward direction.</param>
        /// <param name="localPoints">The piece mesh's corner points (before rotation).</param>
        /// <param name="localBounds">The piece mesh's bounds (before rotation).</param>
        /// <param name="rotation">The piece's rotation.</param>
        public static Vector3 SurfacePosition(Vector3 hitPoint, Vector3 hitNormal, IReadOnlyList<Vector3> localPoints,
                                              Bounds localBounds, Quaternion rotation)
        {
            Vector3 n = hitNormal.normalized;

            // How far the piece reaches "into" the surface from its pivot: push it out by exactly that much.
            float deepest = float.PositiveInfinity;
            for (int i = 0; i < localPoints.Count; i++)
                deepest = Mathf.Min(deepest, Vector3.Dot(rotation * localPoints[i], n));

            // Put the middle of the piece's box in front of the hit point (along the surface), then push out.
            Vector3 middle = rotation * localBounds.center;
            return hitPoint + n * (Vector3.Dot(middle, n) - deepest) - middle;
        }

        /// <summary>
        /// GRID mode: moves the piece so the edges of its box land on grid lines.
        /// On a flat (straight) surface the piece keeps touching it; on a slanted surface every direction snaps,
        /// so the piece lands in the nearest grid spot (e.g. a second slope that completes a box).
        /// </summary>
        public static Vector3 SnapToGrid(Vector3 position, Vector3 hitNormal, Bounds localBounds,
                                         Quaternion rotation, float gridSize)
        {
            if (gridSize <= 0f) return position;
            Vector3 n = hitNormal.normalized;
            Vector3 e = RotatedExtents(localBounds.extents, rotation);
            Vector3 middle = position + rotation * localBounds.center;

            int along = DominantAxis(n);
            bool straightSurface = Mathf.Abs(n[along]) > 0.99f;
            for (int axis = 0; axis < 3; axis++)
            {
                if (straightSurface && axis == along) continue;   // keep touching the surface
                float minCorner = middle[axis] - e[axis];
                // Floor(x + 0.5) rounds halves up every time (Mathf.Round would round halves to even numbers).
                middle[axis] = Mathf.Floor(minCorner / gridSize + 0.5f) * gridSize + e[axis];
            }
            return middle - rotation * localBounds.center;
        }

        /// <summary>
        /// SNAP mode, Valheim style: finds the closest pair of "snap points" (box corners, edge middles, face middles)
        /// between the new piece and the aimed piece. If they are closer than <paramref name="radius"/>, returns the
        /// moved position that puts them on top of each other; otherwise null (stay where you aimed).
        /// </summary>
        public static Vector3? SnapToPoints(Vector3 position, Quaternion rotation, Bounds bounds,
                                            Vector3 targetPosition, Quaternion targetRotation, Bounds targetBounds,
                                            float radius)
        {
            var mine = SnapPoints(bounds);
            var theirs = SnapPoints(targetBounds);
            float best = radius * radius;
            Vector3? move = null;
            foreach (var m in mine)
            {
                Vector3 mw = position + rotation * m;
                foreach (var t in theirs)
                {
                    Vector3 tw = targetPosition + targetRotation * t;
                    float d = (tw - mw).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        move = tw - mw;
                    }
                }
            }
            return move.HasValue ? position + move.Value : (Vector3?)null;
        }

        /// <summary>
        /// The attach points Q cycles through, as directions from the box middle (-1, 0 or +1 per axis):
        /// first the 8 corners, then the 6 face middles, then the 12 edge middles.
        /// </summary>
        public static readonly Vector3Int[] AttachPoints = BuildAttachPoints();

        static Vector3Int[] BuildAttachPoints()
        {
            // How many of x/y/z are not zero: 3 = corner, 1 = face middle, 2 = edge middle.
            int Kind(Vector3Int v) => (v.x != 0 ? 1 : 0) + (v.y != 0 ? 1 : 0) + (v.z != 0 ? 1 : 0);
            var sorted = new List<Vector3Int>();
            foreach (int kind in new[] { 3, 1, 2 })
                for (int y = 1; y >= -1; y--)          // top points first: usually the most useful
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    var v = new Vector3Int(x, y, z);
                    if (Kind(v) == kind) sorted.Add(v);
                }
            return sorted.ToArray();
        }

        /// <summary>Where an attach point is on a box (in the box's own space).</summary>
        public static Vector3 PointOnBox(Bounds b, Vector3Int point) =>
            b.center + Vector3.Scale(b.extents, point);

        /// <summary>A readable name, e.g. "Top front left corner", "Top face", "Bottom left edge".</summary>
        public static string AttachPointName(Vector3Int p)
        {
            var parts = new List<string>();
            if (p.y != 0) parts.Add(p.y > 0 ? "top" : "bottom");
            if (p.z != 0) parts.Add(p.z > 0 ? "back" : "front");
            if (p.x != 0) parts.Add(p.x > 0 ? "right" : "left");
            string kind = parts.Count == 3 ? "corner" : parts.Count == 1 ? "face" : "edge";
            string text = string.Join(" ", parts) + " " + kind;
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        /// <summary>
        /// SNAP mode with a chosen attach point: moves the piece so its <paramref name="attachPoint"/> sits exactly on
        /// the aimed piece's snap point that is closest to <paramref name="aimPoint"/>.
        /// </summary>
        public static Vector3 AttachAt(Quaternion rotation, Bounds bounds, Vector3Int attachPoint,
                                       Vector3 targetPosition, Quaternion targetRotation, Bounds targetBounds,
                                       Vector3 aimPoint)
        {
            Vector3 best = targetPosition;
            float bestDistance = float.PositiveInfinity;
            foreach (var t in SnapPoints(targetBounds))
            {
                Vector3 world = targetPosition + targetRotation * t;
                float d = (world - aimPoint).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = world;
                }
            }
            return best - rotation * PointOnBox(bounds, attachPoint);
        }

        /// <summary>The aimed piece's snap point (corner / edge middle / face middle) closest to <paramref name="aimPoint"/>.</summary>
        public static Vector3 NearestSnapPoint(Vector3 targetPosition, Quaternion targetRotation, Bounds targetBounds, Vector3 aimPoint)
        {
            Vector3 best = aimPoint;
            float bestDistance = float.PositiveInfinity;
            foreach (var t in SnapPoints(targetBounds))
            {
                Vector3 world = targetPosition + targetRotation * t;
                float d = (world - aimPoint).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = world; }
            }
            return best;
        }

        /// <summary>The grid crossing closest to <paramref name="point"/> (where points go in GRID mode).</summary>
        public static Vector3 NearestGridCrossing(Vector3 point, float gridSize) => gridSize <= 0f ? point : new Vector3(
            Mathf.Floor(point.x / gridSize + 0.5f) * gridSize,
            Mathf.Floor(point.y / gridSize + 0.5f) * gridSize,
            Mathf.Floor(point.z / gridSize + 0.5f) * gridSize);

        /// <summary>The 26 snap points of a box: 8 corners, 12 edge middles, 6 face middles.</summary>
        public static List<Vector3> SnapPoints(Bounds b)
        {
            var points = new List<Vector3>(26);
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && y == 0 && z == 0) continue;   // not the very middle
                points.Add(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)));
            }
            return points;
        }

        /// <summary>Half-size of the box after rotating it (the size of the box that fits around it).</summary>
        public static Vector3 RotatedExtents(Vector3 extents, Quaternion rotation)
        {
            Vector3 x = rotation * new Vector3(extents.x, 0, 0);
            Vector3 y = rotation * new Vector3(0, extents.y, 0);
            Vector3 z = rotation * new Vector3(0, 0, extents.z);
            return new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        static int DominantAxis(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return 0;
            return ay >= az ? 1 : 2;
        }

        /// <summary>The next grid size after the current one (wraps around).</summary>
        public static float NextGridSize(float current)
        {
            for (int i = 0; i < GridSizes.Length; i++)
                if (Mathf.Approximately(GridSizes[i], current))
                    return GridSizes[(i + 1) % GridSizes.Length];
            return GridSizes[0];
        }
    }
}
