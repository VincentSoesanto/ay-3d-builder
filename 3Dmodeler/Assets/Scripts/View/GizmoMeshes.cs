using Builder.Shapes;
using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// Thin tube meshes for on-screen helpers: straight arrows (move), rings (rotate) and curved arrows
    /// (rotation indicator). All are built once and shared.
    /// </summary>
    public static class GizmoMeshes
    {
        const int Sides = 6;

        static Mesh straightArrow, ring;

        /// <summary>An arrow from (0,0,0) to (0,1,0): thin shaft with a cone tip.</summary>
        public static Mesh StraightArrow => straightArrow != null ? straightArrow : straightArrow = BuildStraightArrow();

        /// <summary>A 1 x 1 x 1 cube around (0, 0, 0) (Unity's built-in one).</summary>
        public static Mesh Cube
        {
            get
            {
                if (cube != null) return cube;
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = temp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(temp);
                return cube;
            }
        }
        static Mesh cube;

        /// <summary>A full circle of radius 1 around +Y.</summary>
        public static Mesh Ring => ring != null ? ring : ring = Arc(0f, 360f, 0.03f, 0f, 0f, "Ring");

        /// <summary>
        /// A thin tube bent along a circle (radius 1, around +Y) from angle <paramref name="start"/> to
        /// <paramref name="end"/> (degrees), with an optional cone tip at the end (headLength = 0 for none).
        /// Going from start to end = a positive turn around +Y, the same way Quaternion.AngleAxis(+angle, up) turns.
        /// </summary>
        public static Mesh Arc(float start, float end, float tubeRadius, float headRadius, float headLength, string name)
        {
            int steps = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(end - start) / 7f));
            var mb = new MeshBuilder();

            var prev = TubeRing(start, tubeRadius);
            for (int s = 1; s <= steps; s++)
            {
                float t = Mathf.Lerp(start, end, s / (float)steps);
                var next = TubeRing(t, tubeRadius);
                var middle = OnCircle(Mathf.Lerp(start, end, (s - 0.5f) / steps));
                for (int i = 0; i < Sides; i++)
                {
                    int j = (i + 1) % Sides;
                    mb.AddQuadFacing(prev[i], prev[j], next[j], next[i], (prev[i] + next[j]) * 0.5f - middle);
                }
                prev = next;
            }

            if (headLength > 0f)
            {
                var baseCenter = OnCircle(end);
                Vector3 forward = Tangent(end);
                AddCone(mb, baseCenter, forward, TubeRing(end, headRadius), headLength);
            }
            return mb.ToMesh(name, false);
        }

        static Mesh BuildStraightArrow()
        {
            const float shaftRadius = 0.018f, headRadius = 0.06f, headLength = 0.2f;
            var mb = new MeshBuilder();
            Vector3[] Circle(float y, float r)
            {
                var pts = new Vector3[Sides];
                for (int i = 0; i < Sides; i++)
                {
                    float a = i * Mathf.PI * 2f / Sides;
                    pts[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }
                return pts;
            }
            float shaftEnd = 1f - headLength;
            var bottom = Circle(0f, shaftRadius);
            var top = Circle(shaftEnd, shaftRadius);
            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                var mid = new Vector3(0f, shaftEnd * 0.5f, 0f);
                mb.AddQuadFacing(bottom[i], bottom[j], top[j], top[i], (bottom[i] + top[j]) * 0.5f - mid);
            }
            AddCone(mb, new Vector3(0f, shaftEnd, 0f), Vector3.up, Circle(shaftEnd, headRadius), headLength);
            return mb.ToMesh("Straight Arrow", false);
        }

        /// <summary>A cone with its base ring at <paramref name="baseCenter"/>, pointing along <paramref name="forward"/>.</summary>
        static void AddCone(MeshBuilder mb, Vector3 baseCenter, Vector3 forward, Vector3[] baseRing, float length)
        {
            var tip = baseCenter + forward * length;
            var inside = baseCenter + forward * (length * 0.3f);
            for (int i = 0; i < baseRing.Length; i++)
            {
                int j = (i + 1) % baseRing.Length;
                mb.AddTriangleFacing(baseRing[i], baseRing[j], tip, (baseRing[i] + baseRing[j] + tip) / 3f - inside);
                mb.AddTriangleFacing(baseRing[i], baseRing[j], baseCenter, -forward);
            }
        }

        // A point on the circle at angle t (degrees): AngleAxis(t, up) * forward = (sin t, 0, cos t).
        static Vector3 OnCircle(float t) => new Vector3(Mathf.Sin(t * Mathf.Deg2Rad), 0f, Mathf.Cos(t * Mathf.Deg2Rad));
        static Vector3 Tangent(float t) => new Vector3(Mathf.Cos(t * Mathf.Deg2Rad), 0f, -Mathf.Sin(t * Mathf.Deg2Rad));

        // Ring of points around the tube at angle t.
        static Vector3[] TubeRing(float t, float r)
        {
            var c = OnCircle(t);
            var pts = new Vector3[Sides];
            for (int i = 0; i < Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides;
                pts[i] = c + (c * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * r;
            }
            return pts;
        }
    }
}
