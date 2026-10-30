using System;
using Builder.Core;

namespace Builder.Shapes
{
    /// <summary>Which optional settings a shape uses (so the edit panel only shows those).</summary>
    [Flags]
    public enum ShapeFeatures
    {
        None = 0,
        Bevel = 1,      // cut-off edges
        Taper = 2,      // one end smaller
        Hollow = 4,     // tube / pipe with a wall thickness
        Segments = 8,   // number of sides for round shapes
        Wall = 16,      // wall/flange thickness without "hollow" (beam)
    }

    /// <summary>
    /// A shape type (box, wedge, ...). It builds its mesh from the piece's parameters.
    /// The mesh is centered on the piece's position; its size is width (X) x height (Y) x length (Z).
    /// </summary>
    public interface IShape
    {
        /// <summary>The name stored in the save file, e.g. "box". Never change it once used.</summary>
        string Id { get; }

        /// <summary>Name shown to the user, e.g. "Box".</summary>
        string DisplayName { get; }

        ShapeFeatures Features { get; }

        /// <summary>Settings a new piece of this shape starts with.</summary>
        ShapeParams CreateDefaults();

        void Build(ShapeParams p, MeshBuilder mb);
    }
}
