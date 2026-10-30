using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// Slope (wedge): full height at the back (+Z), sloping down to the front (-Z) bottom edge.
    /// </summary>
    public class WedgeShape : IShape
    {
        public string Id => "wedge";
        public string DisplayName => "Slope";
        public ShapeFeatures Features => ShapeFeatures.None;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 1f, length = 1f };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            float x = p.width / 2f, y = p.height / 2f, z = p.length / 2f;
            // l/r = left/right (X), b/t = bottom/top (Y), f/k = front/back (-Z/+Z)
            var lbf = new Vector3(-x, -y, -z); var rbf = new Vector3(x, -y, -z);
            var lbk = new Vector3(-x, -y, z);  var rbk = new Vector3(x, -y, z);
            var ltk = new Vector3(-x, y, z);   var rtk = new Vector3(x, y, z);
            var mid = (lbf + rbf + lbk + rbk + ltk + rtk) / 6f;   // a point inside, to know which way is "out"

            mb.AddQuadFacing(lbf, rbf, rbk, lbk, Vector3.down);                           // bottom
            mb.AddQuadFacing(lbk, rbk, rtk, ltk, Vector3.forward);                        // back
            mb.AddQuadFacing(lbf, rbf, rtk, ltk, (lbf + rbf + rtk + ltk) / 4f - mid);     // slope
            mb.AddTriangleFacing(lbf, lbk, ltk, Vector3.left);                            // left side
            mb.AddTriangleFacing(rbf, rbk, rtk, Vector3.right);                           // right side
        }
    }

    /// <summary>
    /// Corner slope: a square base whose top comes to a point above the back-right corner (+X, +Z).
    /// Used to close the corners where two slopes meet.
    /// </summary>
    public class CornerWedgeShape : IShape
    {
        public string Id => "cornerWedge";
        public string DisplayName => "Corner slope";
        public ShapeFeatures Features => ShapeFeatures.None;

        public ShapeParams CreateDefaults() => new ShapeParams { width = 1f, height = 1f, length = 1f };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            float x = p.width / 2f, y = p.height / 2f, z = p.length / 2f;
            var lbf = new Vector3(-x, -y, -z); var rbf = new Vector3(x, -y, -z);
            var lbk = new Vector3(-x, -y, z);  var rbk = new Vector3(x, -y, z);
            var top = new Vector3(x, y, z);
            var mid = (lbf + rbf + lbk + rbk + top) / 5f;

            mb.AddQuadFacing(lbf, rbf, rbk, lbk, Vector3.down);                           // bottom
            mb.AddTriangleFacing(lbk, rbk, top, Vector3.forward);                         // back
            mb.AddTriangleFacing(rbf, rbk, top, Vector3.right);                           // right side
            mb.AddTriangleFacing(lbf, lbk, top, (lbf + lbk + top) / 3f - mid);            // slope (left half)
            mb.AddTriangleFacing(lbf, rbf, top, (lbf + rbf + top) / 3f - mid);            // slope (front half)
        }
    }
}
