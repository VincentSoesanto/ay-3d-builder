using System.Collections.Generic;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// Cutting one closed shape out of another ("boolean subtract", CSG), for the eraser's Cut mode.
    ///
    /// How it works (the classic BSP method, as in the free csg.js library): every face of a shape is a flat
    /// polygon on a plane. A BSP tree sorts polygons into "in front of" / "behind" planes, splitting the ones that
    /// cross. With a tree for each shape we can keep exactly the parts of shape A that are outside shape B, plus
    /// the parts of B's surface that are inside A (turned around, they become the walls of the cut).
    /// Both shapes must be closed (no holes in their surface); all our shapes are.
    /// </summary>
    public static class Csg
    {
        const float Epsilon = 1e-5f;

        /// <summary>A flat face with its corners in order (front side = clockwise, like Unity's triangles).</summary>
        public class Polygon
        {
            public List<Vector3> points;
            public Plane plane;

            public Polygon(List<Vector3> points, Plane plane)
            {
                this.points = points;
                this.plane = plane;
            }

            public Polygon Clone() => new Polygon(new List<Vector3>(points), plane.Clone());

            public void Flip()
            {
                points.Reverse();
                plane.Flip();
            }
        }

        public class Plane
        {
            public Vector3 normal;
            public float w;   // distance from the origin along the normal

            public Plane(Vector3 normal, float w) { this.normal = normal; this.w = w; }

            public static Plane FromPoints(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;   // same "outside" direction as MeshBuilder
                return new Plane(n, Vector3.Dot(n, a));
            }

            public Plane Clone() => new Plane(normal, w);

            public void Flip()
            {
                normal = -normal;
                w = -w;
            }

            const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

            /// <summary>Sorts a polygon by this plane, cutting it in two if it crosses the plane.</summary>
            public void Split(Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack,
                              List<Polygon> front, List<Polygon> back)
            {
                int whole = 0;
                var types = new int[polygon.points.Count];
                for (int i = 0; i < polygon.points.Count; i++)
                {
                    float t = Vector3.Dot(normal, polygon.points[i]) - w;
                    int type = t < -Epsilon ? Back : t > Epsilon ? Front : Coplanar;
                    whole |= type;
                    types[i] = type;
                }

                switch (whole)
                {
                    case Coplanar:
                        (Vector3.Dot(normal, polygon.plane.normal) > 0f ? coplanarFront : coplanarBack).Add(polygon);
                        break;
                    case Front: front.Add(polygon); break;
                    case Back: back.Add(polygon); break;
                    default:
                        var f = new List<Vector3>();
                        var b = new List<Vector3>();
                        int n = polygon.points.Count;
                        for (int i = 0; i < n; i++)
                        {
                            int j = (i + 1) % n;
                            int ti = types[i], tj = types[j];
                            Vector3 vi = polygon.points[i], vj = polygon.points[j];
                            if (ti != Back) f.Add(vi);
                            if (ti != Front) b.Add(vi);
                            if ((ti | tj) == Spanning)
                            {
                                // Where the edge crosses the plane.
                                float t = (w - Vector3.Dot(normal, vi)) / Vector3.Dot(normal, vj - vi);
                                var cross = Vector3.Lerp(vi, vj, t);
                                f.Add(cross);
                                b.Add(cross);
                            }
                        }
                        if (f.Count >= 3) front.Add(new Polygon(f, polygon.plane.Clone()));
                        if (b.Count >= 3) back.Add(new Polygon(b, polygon.plane.Clone()));
                        break;
                }
            }
        }

        /// <summary>A node of the BSP tree: a plane, the polygons lying on it, and the trees in front / behind.</summary>
        class Node
        {
            Plane plane;
            Node front, back;
            List<Polygon> polygons = new List<Polygon>();

            public Node(List<Polygon> list = null)
            {
                if (list != null) Build(list);
            }

            /// <summary>Swaps inside and outside (the solid becomes the empty space around it).</summary>
            public void Invert()
            {
                foreach (var p in polygons) p.Flip();
                plane?.Flip();
                front?.Invert();
                back?.Invert();
                (front, back) = (back, front);
            }

            /// <summary>Removes the parts of <paramref name="list"/> that are inside this tree's solid.</summary>
            public List<Polygon> ClipPolygons(List<Polygon> list)
            {
                if (plane == null) return new List<Polygon>(list);
                var f = new List<Polygon>();
                var b = new List<Polygon>();
                foreach (var p in list) plane.Split(p, f, b, f, b);
                if (front != null) f = front.ClipPolygons(f);
                b = back != null ? back.ClipPolygons(b) : new List<Polygon>();
                f.AddRange(b);
                return f;
            }

            /// <summary>Removes the parts of this tree's polygons that are inside <paramref name="other"/>.</summary>
            public void ClipTo(Node other)
            {
                polygons = other.ClipPolygons(polygons);
                front?.ClipTo(other);
                back?.ClipTo(other);
            }

            public List<Polygon> AllPolygons()
            {
                var all = new List<Polygon>(polygons);
                if (front != null) all.AddRange(front.AllPolygons());
                if (back != null) all.AddRange(back.AllPolygons());
                return all;
            }

            public void Build(List<Polygon> list)
            {
                if (list.Count == 0) return;
                if (plane == null) plane = list[0].plane.Clone();
                var f = new List<Polygon>();
                var b = new List<Polygon>();
                foreach (var p in list) plane.Split(p, polygons, polygons, f, b);
                if (f.Count > 0) (front ??= new Node()).Build(f);
                if (b.Count > 0) (back ??= new Node()).Build(b);
            }
        }

        /// <summary>Shape <paramref name="a"/> with shape <paramref name="b"/> cut out of it.</summary>
        public static List<Polygon> Subtract(List<Polygon> a, List<Polygon> b)
        {
            var na = new Node(a.ConvertAll(p => p.Clone()));
            var nb = new Node(b.ConvertAll(p => p.Clone()));
            na.Invert();
            na.ClipTo(nb);
            nb.ClipTo(na);
            nb.Invert();
            nb.ClipTo(na);
            nb.Invert();
            na.Build(nb.AllPolygons());
            na.Invert();
            return na.AllPolygons();
        }

        /// <summary>Triangles of a mesh as polygons, moved by <paramref name="transform"/>.</summary>
        public static List<Polygon> FromMesh(Mesh mesh, Matrix4x4 transform)
        {
            var list = new List<Polygon>();
            var v = mesh.vertices;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = transform.MultiplyPoint3x4(v[t[i]]);
                Vector3 b = transform.MultiplyPoint3x4(v[t[i + 1]]);
                Vector3 c = transform.MultiplyPoint3x4(v[t[i + 2]]);
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                list.Add(new Polygon(new List<Vector3> { a, b, c }, Plane.FromPoints(a, b, c)));
            }
            return list;
        }

        /// <summary>Adds the polygons to a MeshBuilder as flat triangles (each polygon is a fan of triangles).</summary>
        public static void AddTo(MeshBuilder mb, List<Polygon> polygons)
        {
            foreach (var p in polygons)
                for (int i = 1; i + 1 < p.points.Count; i++)
                    mb.AddTriangle(p.points[0], p.points[i], p.points[i + 1]);
        }
    }
}
