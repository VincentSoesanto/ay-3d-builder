using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// Box: width (X) x height (Y) x length (Z).
    /// Bevel cuts all edges, taper shrinks the top, hollow makes a square tube (open top and bottom).
    /// </summary>
    public class BoxShape : IShape
    {
        public virtual string Id => "box";
        public virtual string DisplayName => "Box";
        public ShapeFeatures Features => ShapeFeatures.Bevel | ShapeFeatures.Taper | ShapeFeatures.Hollow;

        public virtual ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 1f, length = 1f };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            if (p.hollow)
            {
                float wall = Mathf.Clamp(p.wall, 0.01f, Mathf.Min(p.width, p.length) / 2f - 0.005f);
                Loft.Hollow(mb, Outlines.Rect(p.width, p.length), Outlines.Rect(p.width - 2 * wall, p.length - 2 * wall),
                            true, -p.height / 2f, p.height / 2f, 1f - Mathf.Clamp01(p.taper), Loft.AlongY);
            }
            else
            {
                var rings = Loft.StandingRings(Outlines.Rect8, p.width, p.length, p.height, p.bevel, p.taper);
                Loft.Solid(mb, rings, Loft.AlongY);
            }
        }
    }

    /// <summary>A thin box (armor plates, floors, panels). Same options as Box.</summary>
    public class PlateShape : BoxShape
    {
        public override string Id => "plate";
        public override string DisplayName => "Plate";
        public override ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 0.1f, length = 1f };
    }

    /// <summary>
    /// I-beam lying along Z: a top and bottom flange joined by a thin web. "Wall" is the flange/web thickness.
    /// </summary>
    public class BeamShape : IShape
    {
        public string Id => "beam";
        public string DisplayName => "Beam";
        public ShapeFeatures Features => ShapeFeatures.Wall;

        public ShapeParams CreateDefaults() =>
            new ShapeParams { width = 0.4f, height = 0.5f, length = 2f, wall = 0.08f };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            float t = Mathf.Clamp(p.wall, 0.01f, Mathf.Min(p.height / 2f, p.width) - 0.001f);
            float flangeY = p.height / 2f - t / 2f;
            AddBox(mb, new Vector3(0f, flangeY, 0f), new Vector3(p.width, t, p.length));          // top flange
            AddBox(mb, new Vector3(0f, -flangeY, 0f), new Vector3(p.width, t, p.length));         // bottom flange
            AddBox(mb, Vector3.zero, new Vector3(t, p.height - 2f * t, p.length));                // web
        }

        static void AddBox(MeshBuilder mb, Vector3 center, Vector3 size)
        {
            var outline = Outlines.Rect(size.x, size.z);
            var rings = new[] { new Loft.Ring(outline, -size.y / 2f), new Loft.Ring(outline, size.y / 2f) };
            Loft.Solid(mb, rings, (pt, s) => center + new Vector3(pt.x, s, pt.y));
        }
    }
}
