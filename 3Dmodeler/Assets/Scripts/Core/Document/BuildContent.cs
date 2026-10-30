using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>
    /// The actual model. Shared by projects AND blueprints, so the same tools, commands and export work on both.
    /// </summary>
    public class BuildContent
    {
        public List<Piece> pieces = new List<Piece>();
        public List<Group> groups = new List<Group>();
        public List<BlueprintInstance> instances = new List<BlueprintInstance>();  // Phase 2
        public List<Bone> bones = new List<Bone>();                                // Phase 3

        public Piece FindPiece(string id) => id == null ? null : pieces.Find(x => x.id == id);
        public Group FindGroup(string id) => groups.Find(x => x.id == id);
        public Bone FindBone(string id) => id == null ? null : bones.Find(x => x.id == id);

        /// <summary>The bone a piece moves with: its own bone, else its group's bone (null = none).</summary>
        public string BoneOf(Piece p) => FindBone(p.boneId)?.id ?? FindBone(FindGroup(p.groupId)?.boneId)?.id;
    }
}
