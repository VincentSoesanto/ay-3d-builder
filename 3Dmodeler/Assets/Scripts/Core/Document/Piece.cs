namespace Builder.Core
{
    /// <summary>One placed shape (box, wedge, ...). Its mesh is generated from <see cref="p"/>, never stored.</summary>
    public class Piece
    {
        public string id;
        public string shape;                 // "box", "wedge", "cornerWedge", "cylinder", "halfCylinder", "cone", "sphere", "beam", "plate"
        public ShapeParams p = new ShapeParams();
        public Vec3 position;                // meters
        public Quat rotation = Quat.Identity;
        public bool mirrored;                // mesh is built mirrored (left-handed copy), so mirror build is exact for every shape
        public string color = "#B0B0B0";       // "#RRGGBB", or with a finish: "#RRGGBB/metal", "#RRGGBB/glow"
        public string groupId;               // null = Ungrouped
        public string boneId;                // Phase 3: bound to this bone (null = follow the group's bone, or none)
        public string mirrorPartnerId;       // the piece on the other side of the mirror; edits/deletes go to both
        public bool frame;                   // made by "Frame parts" (bones panel): pressing it again fits these again

        /// <summary>
        /// Set when this piece is part of a LINKED copy of a blueprint (null = a normal piece). When the blueprint is
        /// saved again, the copy is rebuilt from it. Editing the copy (not just moving it whole) unlinks it.
        /// </summary>
        public PieceLink link;

        /// <summary>
        /// Shapes cut out of this piece (eraser Cut mode), or null for none. The mesh is the shape minus these.
        /// Stored in the piece's own space before mirroring, so a mirrored copy gets exactly mirrored cuts.
        /// </summary>
        public System.Collections.Generic.List<Cut> cuts;

        public bool HasCuts() => cuts != null && cuts.Count > 0;   // a method, so it is not written into the save file

        /// <summary>
        /// Spray paint: small grid squares painted on the surface (Paint tool, spray mode). Each dot colors the part of the
        /// surface inside one cube of the paint grid (<see cref="paintCell"/> meters), measured in the piece's own space.
        /// Null = none (the whole piece is <see cref="color"/>).
        /// </summary>
        public System.Collections.Generic.List<PaintDot> paint;
        public float paintCell;               // size of the paint squares (set by the first spray on this piece)

        public bool HasPaint() => paint != null && paint.Count > 0;

        /// <summary>A full independent copy (used by undo to remember "before" and "after").</summary>
        public Piece Clone()
        {
            var c = (Piece)MemberwiseClone();
            c.p = p.Clone();
            c.cuts = cuts?.ConvertAll(x => x.Clone());
            c.link = link?.Clone();
            c.paint = paint?.ConvertAll(d => d);
            return c;
        }
    }

    /// <summary>One spray-paint square: grid cell (x, y, z) in the piece's own space, and its color.</summary>
    public struct PaintDot
    {
        public int x, y, z;
        public string c;

        public PaintDot(int x, int y, int z, string c) { this.x = x; this.y = y; this.z = z; this.c = c; }
    }

    /// <summary>One cut: a cutter shape and where it sits, measured from the piece (in the piece's own space).</summary>
    public class Cut
    {
        public string shape;
        public ShapeParams p = new ShapeParams();
        public Vec3 position;
        public Quat rotation = Quat.Identity;
        public bool mirrored;

        public Cut Clone()
        {
            var c = (Cut)MemberwiseClone();
            c.p = p.Clone();
            return c;
        }
    }

    /// <summary>Size and style settings of a shape. Each shape uses only the fields it needs.</summary>
    public class ShapeParams
    {
        public float width = 1;              // X
        public float height = 1;             // Y (thickness for plate)
        public float length = 1;             // Z
        public float bevel = 0;              // meters cut off the edges
        public float taper = 0;              // 0 = straight, 1 = top shrinks to a point
        public bool hollow = false;
        public float wall = 0.1f;            // wall thickness when hollow
        public int segments = 8;             // sides for round shapes (low poly)

        /// <summary>
        /// "hull" shape only: the corner points, inside a 1 x 1 x 1 box around (0, 0, 0). The mesh is the solid wrapped
        /// around them, stretched to width x height x length (so resizing works like for other shapes). Null otherwise.
        /// </summary>
        public System.Collections.Generic.List<Vec3> points;

        public ShapeParams Clone()
        {
            var c = (ShapeParams)MemberwiseClone();
            c.points = points != null ? new System.Collections.Generic.List<Vec3>(points) : null;
            return c;
        }
    }
}
