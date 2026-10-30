using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Math for precision mode: turns mouse movement into exact steps along the world axes.
    /// Pure math (no scene objects), so it can be tested.
    /// </summary>
    public static class PrecisionMath
    {
        /// <summary>
        /// The world axis on the ground (+X, -X, +Z or -Z) that points most like <paramref name="direction"/>.
        /// So "mouse right" moves along whichever ground axis is most to the camera's right.
        /// </summary>
        public static Vector3 GroundAxis(Vector3 direction)
        {
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.z))
                return new Vector3(direction.x >= 0f ? 1f : -1f, 0f, 0f);
            return new Vector3(0f, 0f, direction.z >= 0f ? 1f : -1f);
        }

        /// <summary>
        /// Takes whole steps out of an amount of mouse movement (in pixels). Returns how many steps were made
        /// (can be negative) and keeps the leftover pixels in <paramref name="pixels"/> for next time.
        /// </summary>
        public static int TakeSteps(ref float pixels, float pixelsPerStep)
        {
            int steps = (int)(pixels / pixelsPerStep);   // rounds toward zero, so small wobbles don't move anything
            pixels -= steps * pixelsPerStep;
            return steps;
        }
    }
}
