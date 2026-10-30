namespace Builder.Core
{
    /// <summary>Phase 3: a "rigger tube" bone, from its head (start joint) to its tail (end joint).</summary>
    public class Bone
    {
        public string id;
        public string name;                  // "Hips", "UpperArm.L", ...
        public string parentId;              // null = root bone
        public Vec3 head;                    // start joint = the bone's pivot
        public Vec3 tail;                    // end joint
        public float roll;                   // degrees around the head->tail axis
        public AxisLimits limits;            // null = free to rotate
        public string copyId;                // came with this linked blueprint copy (its instanceId), or null
        public string copyBone;              // ... and is this bone of the blueprint (rebuilt when the blueprint changes)

        public Bone Clone()
        {
            var c = (Bone)MemberwiseClone();
            c.limits = limits?.Clone();
            return c;
        }
    }

    /// <summary>Allowed rotation per axis, in degrees.</summary>
    public class AxisLimits
    {
        public Vec3 min;
        public Vec3 max;

        public AxisLimits Clone() => (AxisLimits)MemberwiseClone();
    }
}
