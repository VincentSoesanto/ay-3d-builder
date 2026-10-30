using System.Collections.Generic;
using System.Linq;
using System.Text;
using Builder.Core;
using Builder.Shapes;
using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// The mesh of a piece WITH its spray paint (see <see cref="Piece.paint"/>): every face is cut along the paint grid
    /// where it touches painted cells, and the cut parts go into one sub-mesh per paint color.
    ///   - Sub-mesh 0 = the piece's own color, then one per paint color (<c>colors</c> lists them in that order).
    ///   - A piece without spray paint just gets its normal mesh (one sub-mesh).
    ///   - The normal mesh (<see cref="ShapeRegistry.GetPieceMesh(Piece)"/>) stays in use for aiming / colliders / ghosts.
    /// A face lying exactly on a grid line belongs to the cell just INSIDE the piece (the spray picks cells the same way).
    /// </summary>
    public static class PaintedMesh
    {
        const float Eps = 1e-5f;
        const int MaxCache = 400;

        static readonly Dictionary<string, (Mesh mesh, string[] colors)> cache = new Dictionary<string, (Mesh, string[])>();

        /// <summary>The cell a point of the surface belongs to (nudged a little into the piece, against the normal).</summary>
        public static Vector3Int CellOf(Vector3 localPoint, Vector3 localNormal, float cell)
        {
            var p = localPoint - localNormal.normalized * (cell * 0.01f);
            return new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
        }

        public static Mesh Get(Piece piece, out string[] colors)
        {
            var baseMesh = ShapeRegistry.GetPieceMesh(piece);
            if (!piece.HasPaint() || piece.paintCell <= 0f)
            {
                colors = new[] { piece.color };
                return baseMesh;
            }

            var key = new StringBuilder();
            key.Append(baseMesh.GetInstanceID()).Append('|').Append(piece.color).Append('|').Append(piece.paintCell);
            foreach (var d in piece.paint) key.Append('|').Append(d.x).Append(',').Append(d.y).Append(',').Append(d.z).Append(d.c);
            string k = key.ToString();
            if (cache.TryGetValue(k, out var hit) && hit.mesh != null)
            {
                colors = hit.colors;
                return hit.mesh;
            }
            if (cache.Count > MaxCache)
            {
                // Forget them, but don't destroy them: placed pieces may still show some (they would vanish).
                cache.Clear();
                ShapeRegistry.FreeUnusedMeshes();
            }

            var mesh = Build(baseMesh, piece, out colors);
            cache[k] = (mesh, colors);
            return mesh;
        }

        static Mesh Build(Mesh baseMesh, Piece piece, out string[] colors)
        {
            float c = piece.paintCell;
            var cellColor = new Dictionary<Vector3Int, string>();
            foreach (var d in piece.paint) cellColor[new Vector3Int(d.x, d.y, d.z)] = d.c;   // the last dot on a cell wins
            var paintColors = cellColor.Values.Distinct().OrderBy(x => x, System.StringComparer.Ordinal).ToList();
            colors = new[] { piece.color }.Concat(paintColors).ToArray();
            var subIndex = paintColors.Select((col, i) => (col, i)).ToDictionary(x => x.col, x => x.i + 1);

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var subs = new List<int>[colors.Length];
            for (int i = 0; i < subs.Length; i++) subs[i] = new List<int>();

            var bv = baseMesh.vertices;
            var bn = baseMesh.normals;
            var bt = baseMesh.triangles;
            for (int t = 0; t < bt.Length; t += 3)
            {
                var tri = new List<Vector3> { bv[bt[t]], bv[bt[t + 1]], bv[bt[t + 2]] };
                var n = bn.Length > bt[t] ? bn[bt[t]] : Vector3.Cross(tri[1] - tri[0], tri[2] - tri[0]).normalized;

                // The painted cells this triangle's box touches.
                var min = Vector3.Min(Vector3.Min(tri[0], tri[1]), tri[2]);
                var max = Vector3.Max(Vector3.Max(tri[0], tri[1]), tri[2]);
                var lo = Vector3Int.FloorToInt((min - Vector3.one * Eps) / c);
                var hi = Vector3Int.FloorToInt((max + Vector3.one * Eps) / c);
                var pending = new List<List<Vector3>> { tri };
                foreach (var pair in cellColor)
                {
                    var cell = pair.Key;
                    if (cell.x < lo.x || cell.y < lo.y || cell.z < lo.z || cell.x > hi.x || cell.y > hi.y || cell.z > hi.z) continue;
                    var cellMin = (Vector3)cell * c;
                    var cellMax = cellMin + Vector3.one * c;
                    var rest = new List<List<Vector3>>();
                    foreach (var poly in pending)
                    {
                        var inside = ClipToBox(poly, cellMin, cellMax, n, c, rest);
                        if (inside != null) Add(inside, n, subs[subIndex[pair.Value]], verts, normals);
                    }
                    pending = rest;
                    if (pending.Count == 0) break;
                }
                foreach (var poly in pending) Add(poly, n, subs[0], verts, normals);
            }

            var mesh = new Mesh { name = baseMesh.name + " painted" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Cuts a flat convex polygon by the 6 sides of a cell: returns the part inside (or null) and adds the parts
        /// outside to <paramref name="outside"/>.
        /// </summary>
        static List<Vector3> ClipToBox(List<Vector3> poly, Vector3 min, Vector3 max, Vector3 n, float cell, List<List<Vector3>> outside)
        {
            var current = poly;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    // Keep "p[axis] >= min" on the first side, "p[axis] <= max" on the second.
                    float plane = side == 0 ? min[axis] : max[axis];
                    float sign = side == 0 ? 1f : -1f;
                    Split(current, axis, plane, sign, n, cell, out var keep, out var drop);
                    if (drop != null) outside.Add(drop);
                    if (keep == null) return null;
                    current = keep;
                }
            }
            return current;
        }

        /// <summary>Splits a convex polygon by the plane p[axis] = plane; "keep" is the side where sign * (p - plane) >= 0.</summary>
        static void Split(List<Vector3> poly, int axis, float plane, float sign, Vector3 n, float cell,
                          out List<Vector3> keep, out List<Vector3> drop)
        {
            int count = poly.Count;
            var d = new float[count];
            bool anyPos = false, anyNeg = false;
            for (int i = 0; i < count; i++)
            {
                d[i] = sign * (poly[i][axis] - plane);
                if (d[i] > Eps) anyPos = true;
                else if (d[i] < -Eps) anyNeg = true;
            }
            if (!anyPos && !anyNeg)
            {
                // The polygon lies ON the plane: it belongs to the side just inside the piece (against the normal).
                var middle = Vector3.zero;
                foreach (var p in poly) middle += p;
                middle = middle / count - n * (cell * 0.01f);
                bool inKeep = sign * (middle[axis] - plane) >= 0f;
                keep = inKeep ? poly : null;
                drop = inKeep ? null : poly;
                return;
            }
            if (!anyNeg) { keep = poly; drop = null; return; }
            if (!anyPos) { keep = null; drop = poly; return; }

            keep = new List<Vector3>(count + 2);
            drop = new List<Vector3>(count + 2);
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                var a = poly[i];
                var b = poly[j];
                if (d[i] >= -Eps) keep.Add(a);
                if (d[i] <= Eps) drop.Add(a);
                if ((d[i] > Eps && d[j] < -Eps) || (d[i] < -Eps && d[j] > Eps))
                {
                    var hit = Vector3.Lerp(a, b, d[i] / (d[i] - d[j]));
                    keep.Add(hit);
                    drop.Add(hit);
                }
            }
            if (keep.Count < 3) keep = null;
            if (drop.Count < 3) drop = null;
        }

        static void Add(List<Vector3> poly, Vector3 n, List<int> tris, List<Vector3> verts, List<Vector3> normals)
        {
            int start = verts.Count;
            foreach (var p in poly)
            {
                verts.Add(p);
                normals.Add(n);
            }
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                tris.Add(start);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
        }
    }

    /// <summary>
    /// Piece colors with a finish: "#RRGGBB" = matte paint, "#RRGGBB/metal" = shiny metal, "#RRGGBB/glow" = glowing
    /// (lights, visors, thruster fire). The finish also goes into the .glb export (metallic / emissive).
    /// </summary>
    public static class ColorFinish
    {
        public enum Finish { Matte, Metal, Glow }

        public static Finish Of(string color)
        {
            if (color == null) return Finish.Matte;
            if (color.EndsWith("/metal", System.StringComparison.OrdinalIgnoreCase)) return Finish.Metal;
            if (color.EndsWith("/glow", System.StringComparison.OrdinalIgnoreCase)) return Finish.Glow;
            return Finish.Matte;
        }

        /// <summary>Just the "#RRGGBB" part.</summary>
        public static string Hex(string color)
        {
            if (color == null) return "#B0B0B0";
            int slash = color.IndexOf('/');
            return (slash >= 0 ? color.Substring(0, slash) : color).ToUpperInvariant();
        }

        public static string Make(string hex, Finish finish) =>
            Hex(hex) + (finish == Finish.Metal ? "/metal" : finish == Finish.Glow ? "/glow" : "");

        /// <summary>A copy of the URP Lit base material in this color and finish.</summary>
        public static Material MaterialFor(Material baseMaterial, string color)
        {
            var m = new Material(baseMaterial) { name = "Color " + color };
            var c = Conversions.HexToColor(color);
            m.SetColor("_BaseColor", c);
            switch (Of(color))
            {
                case Finish.Metal:
                    m.SetFloat("_Metallic", 0.85f);
                    m.SetFloat("_Smoothness", 0.68f);
                    break;
                case Finish.Glow:
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * 1.6f);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    break;
                default:
                    m.SetFloat("_Metallic", 0f);
                    m.SetFloat("_Smoothness", 0.25f);
                    break;
            }
            return m;
        }
    }
}
