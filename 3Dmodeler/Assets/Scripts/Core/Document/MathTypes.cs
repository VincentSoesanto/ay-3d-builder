// Small position/rotation types used in the save file.
// We use our own instead of Unity's Vector3/Quaternion so the document (and its JSON) does not depend on Unity.
// The View code converts them to Unity types with one-line helpers.
namespace Builder.Core
{
    public struct Vec3
    {
        public float x, y, z;

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public override string ToString() => $"({x}, {y}, {z})";
    }

    public struct Quat
    {
        public float x, y, z, w;

        public Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        /// <summary>"No rotation".</summary>
        public static readonly Quat Identity = new Quat(0, 0, 0, 1);
    }
}
