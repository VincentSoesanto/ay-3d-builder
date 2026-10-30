using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// Standing cylinder (an oval when width and length differ). Segments = number of flat sides.
    /// Bevel cuts the top and bottom edges, taper shrinks the top, hollow makes a pipe.
    /// </summary>
    public class CylinderShape : IShape
    {
        public string Id => "cylinder";
        public string DisplayName => "Cylinder";
        public ShapeFeatures Features =>
            ShapeFeatures.Bevel | ShapeFeatures.Taper | ShapeFeatures.Hollow | ShapeFeatures.Segments;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 1f, length = 1f, segments = 12 };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            int sides = Mathf.Clamp(p.segments, 3, 64);
            if (p.hollow)
            {
                float wall = Mathf.Clamp(p.wall, 0.01f, Mathf.Min(p.width, p.length) / 2f - 0.005f);
                Loft.Hollow(mb, Outlines.Ellipse(p.width, p.length, sides),
                            Outlines.Ellipse(p.width - 2 * wall, p.length - 2 * wall, sides),
                            true, -p.height / 2f, p.height / 2f, 1f - Mathf.Clamp01(p.taper), Loft.AlongY);
            }
            else
            {
                var rings = Loft.StandingRings((w, d, _) => Outlines.Ellipse(w, d, sides),
                                               p.width, p.length, p.height, p.bevel, p.taper);
                Loft.Solid(mb, rings, Loft.AlongY);
            }
        }
    }

    /// <summary>Cone: a cylinder whose top shrinks to a point.</summary>
    public class ConeShape : IShape
    {
        public string Id => "cone";
        public string DisplayName => "Cone";
        public ShapeFeatures Features => ShapeFeatures.Segments;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 1f, length = 1f, segments = 12 };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            int sides = Mathf.Clamp(p.segments, 3, 64);
            var rings = Loft.StandingRings((w, d, _) => Outlines.Ellipse(w, d, sides),
                                           p.width, p.length, p.height, 0f, 1f);
            Loft.Solid(mb, rings, Loft.AlongY);
        }
    }

    /// <summary>
    /// Half-cylinder lying along Z with its flat side down (like a rounded roof or an arm guard).
    /// Height = how high the round top goes. Taper shrinks the far (+Z) end, hollow makes a half-pipe.
    /// </summary>
    public class HalfCylinderShape : IShape
    {
        public string Id => "halfCylinder";
        public string DisplayName => "Half cylinder";
        public ShapeFeatures Features => ShapeFeatures.Taper | ShapeFeatures.Hollow | ShapeFeatures.Segments;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 0.5f, length = 1f, segments = 12 };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            int sides = Mathf.Clamp(p.segments / 2, 2, 32);   // segments counts a full circle; half of it is used
            var outer = Outlines.HalfEllipse(p.width, p.height, p.height, sides);
            float endScale = 1f - Mathf.Clamp01(p.taper);

            if (p.hollow)
            {
                float wall = Mathf.Clamp(p.wall, 0.01f, Mathf.Min(p.width / 2f, p.height) - 0.005f);
                var inner = Outlines.HalfEllipse(p.width - 2 * wall, p.height - wall, p.height, sides);
                Loft.Hollow(mb, outer, inner, false, -p.length / 2f, p.length / 2f, endScale, Loft.AlongZ);
            }
            else
            {
                var rings = new[]
                {
                    new Loft.Ring(outer, -p.length / 2f),
                    new Loft.Ring(Loft.Scaled(outer, endScale, endScale), p.length / 2f),
                };
                Loft.Solid(mb, rings, Loft.AlongZ);
            }
        }
    }

    /// <summary>Sphere (an egg/oval shape when the sizes differ). Segments = sides around the middle.</summary>
    public class SphereShape : IShape
    {
        public string Id => "sphere";
        public string DisplayName => "Sphere";
        public ShapeFeatures Features => ShapeFeatures.Segments;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 1f, length = 1f, segments = 12 };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            int around = Mathf.Clamp(p.segments, 4, 64);
            int bands = Mathf.Max(2, around / 2);

            Vector3 Point(int band, int step)
            {
                float lat = -Mathf.PI / 2f + Mathf.PI * band / bands;   // bottom pole to top pole
                float lon = Mathf.PI * 2f * step / around;
                return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * p.width / 2f,
                                   Mathf.Sin(lat) * p.height / 2f,
                                   Mathf.Cos(lat) * Mathf.Sin(lon) * p.length / 2f);
            }

            for (int b = 0; b < bands; b++)
            {
                for (int s = 0; s < around; s++)
                {
                    Vector3 a = Point(b, s), c = Point(b + 1, s + 1);
                    Vector3 bb = Point(b, s + 1), d = Point(b + 1, s);
                    // At the poles two corners are the same point, so the quad becomes a triangle (handled by MeshBuilder).
                    mb.AddQuadFacing(a, bb, c, d, (a + bb + c + d) * 0.25f);
                }
            }
        }
    }
}
