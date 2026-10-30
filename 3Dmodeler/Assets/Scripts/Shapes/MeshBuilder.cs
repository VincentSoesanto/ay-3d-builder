using System.Collections.Generic;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// Collects triangles and turns them into a Unity Mesh.
    /// Every face gets its own vertices with the face's normal, which gives the hard-edged "flat shaded" low-poly look.
    /// </summary>
    public class MeshBuilder
    {
        // Faces smaller than this are skipped (they happen on purpose, e.g. the tip of a cone).
        const float MinArea2 = 1e-12f;

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<int> triangles = new List<int>();

        /// <summary>Adds a triangle. Corners go clockwise when seen from the outside (Unity's front side).</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < MinArea2) return;   // zero-size triangle: nothing to draw
            var n = cross.normalized;
            int i = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
        }

        /// <summary>Adds a flat four-corner face (two triangles). Corners go clockwise seen from the outside.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        /// <summary>
        /// Adds a triangle and makes sure its front side faces <paramref name="outward"/> (corner order doesn't matter).
        /// </summary>
        public void AddTriangleFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
            AddTriangle(a, b, c);
        }

        /// <summary>
        /// Adds a four-corner face (corners in order around the edge) whose front side faces <paramref name="outward"/>.
        /// </summary>
        public void AddQuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            // The cross product of the two diagonals gives the face direction, even if two corners are the same point.
            if (Vector3.Dot(Vector3.Cross(c - a, d - b), outward) < 0f) (b, d) = (d, b);
            AddQuad(a, b, c, d);
        }

        /// <param name="mirrored">Builds the left-handed copy (flipped on X), used by mirror building.</param>
        public Mesh ToMesh(string name, bool mirrored)
        {
            var verts = new List<Vector3>(vertices);
            var norms = new List<Vector3>(normals);
            var tris = new List<int>(triangles);
            if (mirrored)
            {
                for (int i = 0; i < verts.Count; i++)
                {
                    verts[i] = new Vector3(-verts[i].x, verts[i].y, verts[i].z);
                    norms[i] = new Vector3(-norms[i].x, norms[i].y, norms[i].z);
                }
                // Flipping one axis turns faces inside out, so reverse each triangle's corner order.
                for (int i = 0; i < tris.Count; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
