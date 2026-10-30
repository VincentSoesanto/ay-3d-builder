using UnityEngine;

namespace Builder.Tools
{
    /// <summary>Math for dragging gizmo arrows, rings and handles with the mouse. No scene objects, so it can be tested.</summary>
    public static class GizmoMath
    {
        /// <summary>
        /// The point on the line (origin + axis * t) closest to the mouse ray, as t (meters along the axis).
        /// Null when the ray runs (almost) along the axis, so the position would jump wildly.
        /// </summary>
        public static float? AxisParam(Ray ray, Vector3 origin, Vector3 axis)
        {
            Vector3 a = axis.normalized, d = ray.direction.normalized;
            float b = Vector3.Dot(a, d);
            float denom = 1f - b * b;
            if (denom < 1e-4f) return null;
            Vector3 w = origin - ray.origin;
            return (b * Vector3.Dot(d, w) - Vector3.Dot(a, w)) / denom;
        }

        /// <summary>
        /// Where the mouse ray hits the flat plane through <paramref name="center"/> that faces along
        /// <paramref name="axis"/>, as an angle (degrees) around the axis, measured from <paramref name="reference"/>.
        /// Null when the plane is seen edge-on.
        /// </summary>
        public static float? PlaneAngle(Ray ray, Vector3 center, Vector3 axis, Vector3 reference)
        {
            Vector3 n = axis.normalized;
            float facing = Vector3.Dot(ray.direction, n);
            if (Mathf.Abs(facing) < 1e-4f) return null;
            float s = Vector3.Dot(center - ray.origin, n) / facing;
            if (s < 0f) return null;   // plane is behind the camera
            Vector3 onPlane = ray.origin + ray.direction * s - center;
            return Vector3.SignedAngle(reference, onPlane, n);
        }

        /// <summary>Any direction at right angles to <paramref name="axis"/> (used as the "zero" for ring angles).</summary>
        public static Vector3 Perpendicular(Vector3 axis)
        {
            var p = Vector3.Cross(axis, Vector3.up);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.Cross(axis, Vector3.right);
            return p.normalized;
        }

        /// <summary>Rounds to the nearest multiple of <paramref name="step"/> (no rounding when step is 0).</summary>
        public static float Snap(float value, float step) => step > 0f ? Mathf.Round(value / step) * step : value;

        /// <summary>Distance in pixels from point p to the screen line segment a-b.</summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
