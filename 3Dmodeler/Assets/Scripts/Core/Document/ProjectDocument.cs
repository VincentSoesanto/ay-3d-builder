using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>One project = one save file. This is the "document"; GameObjects are only a picture of it.</summary>
    public class ProjectDocument
    {
        /// <summary>Bump this when the save format changes, and add an upgrader in Core/Migration.</summary>
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string id;                    // never changes; also the cloud doc id in Phase 4
        public string name;
        public long createdUtc;              // unix milliseconds
        public long modifiedUtc;
        public int revision;                 // +1 on every save; sync uses it to see who changed what
        public string lastDeviceId;          // which device saved last (Phase 4)

        public BuildContent content = new BuildContent();
        public List<string> palette = new List<string>();   // paint colors, "#RRGGBB"
        public EditorState editor = new EditorState();     // so a project reopens the way you left it

        public static ProjectDocument CreateNew(string name, long nowUtc) => new ProjectDocument
        {
            id = Ids.New(),
            name = name,
            createdUtc = nowUtc,
            modifiedUtc = nowUtc,
        };
    }

    /// <summary>How new pieces line up: GRID lines, SNAP points (Valheim style), or FREE (no snapping at all).</summary>
    public enum SnapMode { Grid = 0, Snap = 1, Free = 2 }

    /// <summary>Builder settings saved with the project (grid size, snap, mirror, hotbar).</summary>
    public class EditorState
    {
        public float gridSize = 0.25f;
        public SnapMode snapMode = SnapMode.Grid;
        public bool mirrorX = false;
        public string[] hotbar = new string[9];  // shape names or blueprint ids (empty slots are filled with defaults)
        public int hotbarSlot = 0;               // selected slot, 0-8
    }
}
