namespace Builder.Core
{
    /// <summary>
    /// Phase 2: a saved, reusable build. Lives in the blueprint LIBRARY (its own folder), usable in every project.
    /// The thumbnail is a .png file next to the JSON with the same id (not inside the JSON).
    /// </summary>
    public class Blueprint
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string id;
        public string name;
        public string category;
        public int version;                  // +1 on every edit; linked copies notice it and rebuild
        public Vec3 pivot;                   // default: bottom center
        public BuildContent content = new BuildContent();  // pieces, nested instances, bones
        public long modifiedUtc;
    }

    /// <summary>
    /// Which linked blueprint copy a piece belongs to. Every piece of the copy carries the same link (except
    /// <see cref="part"/>), so undo / redo of piece edits also covers the links.
    /// The copy's pieces = the blueprint's pieces, each optionally flipped (mirrored across the blueprint's own X = 0),
    /// scaled, then turned by <see cref="rotation"/> and moved to <see cref="position"/>.
    /// Inside a blueprint, position / rotation are measured from that blueprint's pivot (nested copies).
    /// </summary>
    public class PieceLink
    {
        public string instanceId;            // the same for all pieces of one copy
        public string blueprintId;
        public int version;                  // the blueprint version this copy was built from
        public int part;                     // index of this piece in the blueprint's pieces
        public Vec3 position;
        public Quat rotation = Quat.Identity;
        public float scale = 1f;
        public bool flipped;
        public string partnerId;             // the copy on the other side of the mirror (pieces get paired by part)
        public System.Collections.Generic.List<int> skip;   // parts left out (mirror copy: parts on the mirror line)

        public PieceLink Clone()
        {
            var c = (PieceLink)MemberwiseClone();
            c.skip = skip != null ? new System.Collections.Generic.List<int>(skip) : null;
            return c;
        }
    }

    /// <summary>
    /// Not used: linked copies are stored on their pieces (<see cref="Piece.link"/>), so all piece tools,
    /// undo and export work on them unchanged. Kept so older files still load.
    /// </summary>
    public class BlueprintInstance
    {
        public string id;
        public string blueprintId;
        public Vec3 position;
        public Quat rotation = Quat.Identity;
        public bool mirrored;
        public string groupId;
        public string boneId;
        public string mirrorPartnerId;

        public BlueprintInstance Clone() => (BlueprintInstance)MemberwiseClone();
    }
}
