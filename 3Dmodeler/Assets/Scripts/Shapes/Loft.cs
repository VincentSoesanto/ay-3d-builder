using System;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// Builds a shape by stacking copies ("rings") of a flat 2D outline along a line and joining them,
    /// like a cookie cutter pushed through dough. Boxes, cylinders, cones and half-cylinders are all made this way.
    ///
    /// Outlines are lists of 2D points going around the shape. The <c>place</c> function turns
    /// (2D point, position along the line) into a 3D point, which decides which way the shape stands.
    /// </summary>
    public static class Loft
    {
        public delegate Vector3 Placer(Vector2 point, float along);

        /// <summary>Standing up: outline in X/Z, stacked along Y (box, cylinder, cone).</summary>
        public static readonly Placer AlongY = (p, s) => new Vector3(p.x, s, p.y);

        /// <summary>Lying down: outline in X/Y, stacked along Z (half-cylinder).</summary>
        public static readonly Placer AlongZ = (p, s) => new Vector3(p.x, p.y, s);

        public struct Ring
        {
            public Vector2[] points;
            public float along;
            public Ring(Vector2[] points, float along) { this.points = points; this.along = along; }
        }

        /// <summary>
        /// A solid shape from rings that all have the same number of points. The outline must be convex
        /// (no dents) and contain the point (0, 0). The first and last rings are closed with flat caps.
        /// </summary>
        public static void Solid(MeshBuilder mb, Ring[] rings, Placer place)
        {
            Vector3 axis = place(Vector2.zero, 1f) - place(Vector2.zero, 0f);

            for (int r = 0; r + 1 < rings.Length; r++)
            {
                var a = rings[r];
                var b = rings[r + 1];
                Vector3 center = place(Vector2.zero, (a.along + b.along) * 0.5f);
                int n = a.points.Length;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 p0 = place(a.points[i], a.along), p1 = place(a.points[j], a.along);
                    Vector3 p2 = place(b.points[j], b.along), p3 = place(b.points[i], b.along);
                    mb.AddQuadFacing(p0, p1, p2, p3, (p0 + p1 + p2 + p3) * 0.25f - center);
                }
            }

            Cap(mb, rings[0], place, -axis);
            Cap(mb, rings[rings.Length - 1], place, axis);
        }

        static void Cap(MeshBuilder mb, Ring ring, Placer place, Vector3 outward)
        {
            // A fan of triangles from the first point. Works because the outline is convex.
            var pts = ring.points;
            Vector3 p0 = place(pts[0], ring.along);
            for (int i = 1; i + 1 < pts.Length; i++)
                mb.AddTriangleFacing(p0, place(pts[i], ring.along), place(pts[i + 1], ring.along), outward);
        }

        /// <summary>
        /// A hollow shape (pipe / half-pipe): an outer and an inner outline with the same number of points.
        /// <paramref name="closed"/> = the outline goes all the way around (pipe); false = an open arc (half-pipe),
        /// whose two ends get flat strips. <paramref name="endScale"/> shrinks the far end (taper).
        /// </summary>
        public static void Hollow(MeshBuilder mb, Vector2[] outer, Vector2[] inner, bool closed,
                                  float start, float end, float endScale, Placer place)
        {
            int n = outer.Length;
            Vector3 axis = place(Vector2.zero, 1f) - place(Vector2.zero, 0f);
            Vector3 center = place(Vector2.zero, (start + end) * 0.5f);
            Vector3 P(Vector2[] outline, int i, bool far) => place(outline[i] * (far ? endScale : 1f), far ? end : start);

            int edges = closed ? n : n - 1;
            for (int i = 0; i < edges; i++)
            {
                int j = (i + 1) % n;
                // Outer wall faces away from the middle line, inner wall faces toward it.
                Vector3 o0 = P(outer, i, false), o1 = P(outer, j, false), o2 = P(outer, j, true), o3 = P(outer, i, true);
                mb.AddQuadFacing(o0, o1, o2, o3, (o0 + o1 + o2 + o3) * 0.25f - center);
                Vector3 i0 = P(inner, i, false), i1 = P(inner, j, false), i2 = P(inner, j, true), i3 = P(inner, i, true);
                mb.AddQuadFacing(i0, i1, i2, i3, center - (i0 + i1 + i2 + i3) * 0.25f);
                // End caps: the flat ring between outer and inner outline.
                mb.AddQuadFacing(o0, o1, i1, i0, -axis);
                mb.AddQuadFacing(o3, o2, i2, i3, axis);
            }

            if (!closed)
            {
                // The two flat strips where the arc ends (the "cut" edges of a half-pipe).
                foreach (int k in new[] { 0, n - 1 })
                {
                    Vector3 a = P(outer, k, false), b = P(inner, k, false), c = P(inner, k, true), d = P(outer, k, true);
                    mb.AddQuadFacing(a, b, c, d, (a + b + c + d) * 0.25f - center);
                }
            }
        }

        /// <summary>Scales every point of an outline (used for taper and bevel insets).</summary>
        public static Vector2[] Scaled(Vector2[] points, float sx, float sy)
        {
            var result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = new Vector2(points[i].x * sx, points[i].y * sy);
            return result;
        }

        /// <summary>
        /// Rings for a solid standing shape of the given height, with optional bevel (cut edges at top and bottom)
        /// and taper (top smaller). <paramref name="outline"/> makes the outline for a given size and corner cut.
        /// </summary>
        public static Ring[] StandingRings(Func<float, float, float, Vector2[]> outline,
                                           float width, float depth, float height, float bevel, float taper)
        {
            float h = height / 2f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(width, depth, height) / 2f - 0.001f);
            Ring[] rings = b > 0f
                ? new[]
                {
                    new Ring(outline(width - 2 * b, depth - 2 * b, 0f), -h),   // bottom cap, pulled in
                    new Ring(outline(width, depth, b), -h + b),
                    new Ring(outline(width, depth, b), h - b),
                    new Ring(outline(width - 2 * b, depth - 2 * b, 0f), h),    // top cap, pulled in
                }
                : new[]
                {
                    new Ring(outline(width, depth, 0f), -h),
                    new Ring(outline(width, depth, 0f), h),
                };

            if (taper > 0f)
            {
                for (int i = 0; i < rings.Length; i++)
                {
                    // 1 at the bottom, (1 - taper) at the top.
                    float k = 1f - Mathf.Clamp01(taper) * (rings[i].along + h) / height;
                    rings[i].points = Scaled(rings[i].points, k, k);
                }
            }
            return rings;
        }
    }

    /// <summary>Common 2D outlines. All go counter-clockwise and are centered on (0, 0).</summary>
    public static class Outlines
    {
        /// <summary>Rectangle with its 4 corners cut by <paramref name="cut"/> (always 8 points, so rings match).</summary>
        public static Vector2[] Rect8(float width, float depth, float cut)
        {
            float x = width / 2f, y = depth / 2f, c = Mathf.Min(cut, x, y);
            return new[]
            {
                new Vector2(-x + c, -y), new Vector2(x - c, -y),
                new Vector2(x, -y + c),  new Vector2(x, y - c),
                new Vector2(x - c, y),   new Vector2(-x + c, y),
                new Vector2(-x, y - c),  new Vector2(-x, -y + c),
            };
        }

        public static Vector2[] Rect(float width, float depth)
        {
            float x = width / 2f, y = depth / 2f;
            return new[] { new Vector2(-x, -y), new Vector2(x, -y), new Vector2(x, y), new Vector2(-x, y) };
        }

        /// <summary>An oval (circle when both sizes match) with <paramref name="sides"/> straight sides.</summary>
        public static Vector2[] Ellipse(float width, float depth, int sides)
        {
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                pts[i] = new Vector2(Mathf.Cos(a) * width / 2f, Mathf.Sin(a) * depth / 2f);
            }
            return pts;
        }

        /// <summary>
        /// Half an oval standing on a flat bottom: from the right end, over the top, to the left end.
        /// The outline's box is centered on (0, 0), so the flat bottom is at -height/2.
        /// </summary>
        public static Vector2[] HalfEllipse(float width, float height, float totalHeight, int sides)
        {
            var pts = new Vector2[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI / sides;
                pts[i] = new Vector2(Mathf.Cos(a) * width / 2f, -totalHeight / 2f + Mathf.Sin(a) * height);
            }
            return pts;
        }
    }
}
