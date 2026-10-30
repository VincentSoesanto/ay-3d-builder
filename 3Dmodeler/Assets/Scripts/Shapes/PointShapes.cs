using System.Collections.Generic;
using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// A point: a small orange ball that marks a corner. Place several, then press B to build the solid that wraps
    /// around them (see <see cref="HullShape"/>). Points are helpers: they are not exported.
    /// </summary>
    public class PointShape : IShape
    {
        public const string ShapeId = "point";
        public const string Color = "#FF9A26";

        public string Id => ShapeId;
        public string DisplayName => "Point";
        public ShapeFeatures Features => ShapeFeatures.None;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 0.12f, height = 0.12f, length = 0.12f, segments = 8 };

        public void Build(ShapeParams p, MeshBuilder mb) => new SphereShape().Build(p, mb);
    }

    /// <summary>
    /// The "Rigger tube" item: not a real piece. With it on the hotbar the build tool places bones
    /// (click = start joint, click again = end joint). This shape is only its joint-ball ghost and icon.
    /// </summary>
    public class BoneJointShape : IShape
    {
        public const string ShapeId = "bone";
        public const string Color = "#3FE0FF";

        public string Id => ShapeId;
        public string DisplayName => "Rigger tube";
        public ShapeFeatures Features => ShapeFeatures.None;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 0.1f, height = 0.1f, length = 0.1f, segments = 8 };

        public void Build(ShapeParams p, MeshBuilder mb) => new SphereShape().Build(p, mb);
    }

    /// <summary>
    /// The solid wrapped around a set of points (their "convex hull": like shrink-wrap around nails).
    /// The points are stored inside a 1 x 1 x 1 box and stretched to width x height x length.
    /// </summary>
    public class HullShape : IShape
    {
        public const string ShapeId = "hull";

        public string Id => ShapeId;
        public string DisplayName => "Solid";
        public ShapeFeatures Features => ShapeFeatures.None;

        public ShapeParams CreateDefaults() => new ShapeParams();

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            if (p.points == null || p.points.Count < 4) return;
            var size = new Vector3(p.width, p.height, p.length);
            var points = new List<Vector3>();
            foreach (var v in p.points) points.Add(Vector3.Scale(new Vector3(v.x, v.y, v.z), size));
            ConvexHull.Build(points, mb);
        }
    }

    /// <summary>
    /// Builds the convex hull of a few points: finds every flat side (a plane with all points on one side of it),
    /// puts that side's points in order around it, and adds the side as flat triangles facing outward.
    /// Simple and exact for the small numbers of points used here (up to a few dozen).
    /// </summary>
    public static class ConvexHull
    {
        const float Eps = 1e-4f;

        /// <summary>False if the points are all flat (or fewer than 4), so no solid can be made.</summary>
        public static bool IsSolid(IList<Vector3> points)
        {
            var p = Unique(points);
            if (p.Count < 4) return false;
            for (int i = 0; i < p.Count; i++)
            for (int j = i + 1; j < p.Count; j++)
            for (int k = j + 1; k < p.Count; k++)
            {
                var n = Vector3.Cross(p[j] - p[i], p[k] - p[i]);
                if (n.sqrMagnitude < 1e-10f) continue;
                n.Normalize();
                foreach (var q in p)
                    if (Mathf.Abs(Vector3.Dot(n, q - p[i])) > Eps) return true;   // a point off this plane: it's 3D
                return false;   // everything lies on one plane
            }
            return false;
        }

        /// <summary>
        /// Points that can make a solid: 3D points stay as they are; points lying flat get a thin
        /// <paramref name="thickness"/> (a plate, e.g. 3 points = a flat triangle); points on one line become a thin
        /// square stick between the two ends. Null for fewer than 2 different points.
        /// <paramref name="kind"/> says which ("solid", "plate" or "stick").
        /// </summary>
        public static List<Vector3> MakeSolid(IList<Vector3> input, float thickness, out string kind)
        {
            kind = null;
            var p = Unique(input);
            if (p.Count < 2) return null;
            if (IsSolid(p))
            {
                kind = "solid";
                return p;
            }

            // The two points furthest apart give the main direction.
            Vector3 a = p[0], b = p[1];
            for (int i = 0; i < p.Count; i++)
            for (int j = i + 1; j < p.Count; j++)
                if ((p[j] - p[i]).sqrMagnitude > (b - a).sqrMagnitude) { a = p[i]; b = p[j]; }
            var dir = (b - a).normalized;
            float h = thickness / 2f;

            int offLine = p.FindIndex(q => Vector3.Cross(dir, q - a).magnitude > Eps);   // a point not on the a-b line
            if (offLine < 0)
            {
                // All on one line: a thin square stick from end to end.
                kind = "stick";
                var u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(dir, u);
                var stick = new List<Vector3>();
                foreach (var end in new[] { a, b })
                foreach (var su in new[] { -h, h })
                foreach (var sv in new[] { -h, h })
                    stick.Add(end + u * su + v * sv);
                return stick;
            }

            // All on one flat plane: copy them a little to both sides of it.
            kind = "plate";
            var n = Vector3.Cross(dir, p[offLine] - a).normalized;
            var plate = new List<Vector3>();
            foreach (var q in p)
            {
                plate.Add(q + n * h);
                plate.Add(q - n * h);
            }
            return plate;
        }

        public static void Build(IList<Vector3> input, MeshBuilder mb)
        {
            var p = Unique(input);
            var middle = Vector3.zero;
            foreach (var q in p) middle += q;
            middle /= Mathf.Max(1, p.Count);

            var planes = new List<(Vector3 n, float d)>();
            for (int i = 0; i < p.Count; i++)
            for (int j = i + 1; j < p.Count; j++)
            for (int k = j + 1; k < p.Count; k++)
            {
                var n = Vector3.Cross(p[j] - p[i], p[k] - p[i]);
                if (n.sqrMagnitude < 1e-10f) continue;
                n.Normalize();
                float d = Vector3.Dot(n, p[i]);
                bool anyFront = false, anyBack = false;
                foreach (var q in p)
                {
                    float t = Vector3.Dot(n, q) - d;
                    if (t > Eps) anyFront = true;
                    else if (t < -Eps) anyBack = true;
                }
                if (anyFront && anyBack) continue;   // points on both sides: not a side of the hull
                if (anyFront) { n = -n; d = -d; }     // make the normal point outward (all points behind it)
                if (planes.Exists(x => Vector3.Dot(x.n, n) > 1f - 1e-5f && Mathf.Abs(x.d - d) < Eps)) continue;
                planes.Add((n, d));
            }

            foreach (var (n, d) in planes)
            {
                // The points on this side, in order around their middle.
                var side = p.FindAll(q => Mathf.Abs(Vector3.Dot(n, q) - d) < Eps);
                var c = Vector3.zero;
                foreach (var q in side) c += q;
                c /= side.Count;
                var u = (side[0] - c).sqrMagnitude > 1e-10f ? (side[0] - c).normalized : Vector3.Cross(n, Vector3.up).normalized;
                var v = Vector3.Cross(n, u);
                side.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a - c, v), Vector3.Dot(a - c, u))
                                 .CompareTo(Mathf.Atan2(Vector3.Dot(b - c, v), Vector3.Dot(b - c, u))));
                for (int i = 1; i + 1 < side.Count; i++)
                    mb.AddTriangleFacing(side[0], side[i], side[i + 1], n);
            }
        }

        static List<Vector3> Unique(IList<Vector3> points)
        {
            var list = new List<Vector3>();
            foreach (var q in points)
                if (!list.Exists(x => (x - q).sqrMagnitude < Eps * Eps)) list.Add(q);
            return list;
        }
    }
}
