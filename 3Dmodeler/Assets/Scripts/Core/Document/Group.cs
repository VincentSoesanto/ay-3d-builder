namespace Builder.Core
{
    /// <summary>A named set of pieces (Head, Body, LeftArm...). Becomes one GameObject in the GLB export.</summary>
    public class Group
    {
        public string id;
        public string name;
        public Vec3 pivot;                   // export puts the group's origin here
        public bool visible = true;
        public string boneId;                // Phase 3: whole group bound to a bone
        public string parentId;              // the group this one hangs on (an arm on the body): nested in the export

        public Group Clone() => (Group)MemberwiseClone();
    }
}
