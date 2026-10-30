# AY's 3D Builder: technical design

The goal is in `SPEC.md`. This file says **how** we build it. Status: approved on 2026-10-15 (the original plan; the code grew from here).

---

## 1. Folder structure

The Unity project is `3D\3Dmodeler`. Everything we make lives in `Assets/`:

```
Assets/
  Scenes/
    Builder.unity              the one main scene
  Scripts/
    Core/                      document, commands, undo. Pure C#, no GameObjects (see "Assemblies")
      Document/                ProjectDocument, BuildContent, Piece, Group, Bone, Blueprint...
      Commands/                ICommand, UndoStack, AddPieces / RemovePieces / ModifyPieces...
      Migration/               old-save upgraders (schemaVersion 1 -> 2 -> ...)
    Shapes/                    procedural mesh generators (BoxShape, WedgeShape, ...) + ShapeRegistry
    View/                      builds GameObjects FROM the document (DocumentView, PieceView)   <- added
    Camera/                    fly camera, orbit camera                                          <- added
    Tools/                     PlaceTool, DeleteTool, SelectTool, PaintTool, ...
    Input/                     input action wrapper, rebinding save/load
    UI/                        HUD, hotbar, inventory, edit panel, menus
    IO/                        save/load JSON, autosave, project list, GLB export
    Sync/                      Firebase (Phase 4, empty until then)
  Input/
    BuilderControls.inputactions   all key/mouse/gamepad bindings (replaces the template's file)
  Materials/  Shaders/  Prefabs/  Textures/
  Settings/                    URP settings (from the template, keep)
  Tests/
    EditMode/                  automatic tests for Core (undo/redo, save/load, migration)
```

I added two folders to your list: **View** (the code that turns the document into GameObjects) and **Camera**. That keeps the
"document vs. display" split clear.

**Assemblies.** In Unity, an *assembly definition* (`.asmdef`) groups scripts into a separate compiled DLL. I'd use three:
- `Builder.Core`: only `Scripts/Core`. It **cannot** reference GameObjects or scene code, so the compiler itself stops
  anyone from mixing data and display. It's also easy to test.
- `Builder.Runtime`: everything else (it uses Core).
- `Builder.Tests`: the tests.
Bonus: Unity recompiles faster.

---

## 2. Data model (the "document")

Rules:
- The document is plain C# classes that get saved to JSON. GameObjects are only a *picture* of it, and are rebuilt from it.
- Everything has a stable string **id** (short GUID), never a list index. Undo, mirror pairs, groups, bones,
  blueprints and cloud sync all point at things by id.
- Positions are in meters (1 unit = 1 m). +Y is up.
- JSON uses small types of our own (`Vec3`, `Quat`) instead of Unity's `Vector3`, so the save file stays clean and
  doesn't depend on Unity. Converting is one line.

```csharp
// ---------- top level: one project = one file ----------
public class ProjectDocument
{
    public int schemaVersion = 1;        // bump when the format changes; Migration/ upgrades old files
    public string id;                    // never changes, also used as the cloud doc id (Phase 4)
    public string name;
    public long createdUtc;              // unix milliseconds
    public long modifiedUtc;
    public int revision;                 // +1 on every save; sync uses it to see "who changed what"
    public string lastDeviceId;          // which device saved last (Phase 4)

    public BuildContent content = new(); // the actual model
    public List<string> palette = new(); // paint colors, "#RRGGBB"
    public EditorState editor = new();   // grid size, snap, mirror, camera spot: so a project reopens as you left it
}

// ---------- the model itself. Shared by projects AND blueprints ----------
// A blueprint is just a BuildContent with a name, so the same tools/commands/export code works on both
// (this makes "Edit blueprint" mode and nesting cheap in Phase 2).
public class BuildContent
{
    public List<Piece> pieces = new();
    public List<Group> groups = new();
    public List<BlueprintInstance> instances = new(); // Phase 2 (empty until then)
    public List<Bone> bones = new();                   // Phase 3 (empty until then)
}

public class Piece
{
    public string id;
    public string shape;                 // "box", "wedge", "cornerWedge", "cylinder", "halfCylinder", "cone", "sphere", "beam", "plate"
    public ShapeParams p = new();
    public Vec3 position;
    public Quat rotation = Quat.Identity;
    public bool mirrored;                // mesh is built mirrored (left-handed copy), needed so mirror build is exact for every shape
    public string color = "#B0B0B0";
    public string groupId;               // null = Ungrouped
    public string boneId;                // Phase 3: bound to this bone (null = follows its group's bone, or none)
    public string mirrorPartnerId;       // the piece on the other side of the mirror; edits/deletes go to both
}

public class ShapeParams                 // each shape uses only the fields it needs
{
    public float width = 1;              // X
    public float height = 1;             // Y (thickness for plate)
    public float length = 1;             // Z
    public float bevel = 0;              // meters cut off the edges
    public float taper = 0;              // 0 = straight, 1 = top shrinks to a point
    public bool hollow = false;
    public float wall = 0.1f;            // wall thickness when hollow
    public int segments = 8;             // sides for round shapes (low poly)
}

public class Group                       // Head, Body, LeftArm, ...
{
    public string id;
    public string name;
    public Vec3 pivot;                   // export puts the group's GameObject origin here
    public bool visible = true;
    public string boneId;                // Phase 3: whole group bound to a bone
}

// ---------- Phase 2: blueprints ----------
public class Blueprint                   // stored in the blueprint LIBRARY (its own folder), not inside projects
{
    public int schemaVersion = 1;
    public string id;
    public string name;
    public string category;
    public int version;                  // +1 every time it's edited; linked copies see it and rebuild
    public Vec3 pivot;                   // default bottom-center
    public BuildContent content = new(); // pieces, child instances (nesting), bones
    public long modifiedUtc;
    // thumbnail = a .png next to the JSON file with the same id (not inside the JSON)
}

public class BlueprintInstance           // a LINKED copy placed in a project or inside another blueprint
{
    public string id;
    public string blueprintId;
    public Vec3 position;
    public Quat rotation = Quat.Identity;
    public bool mirrored;
    public string groupId;
    public string boneId;
    public string mirrorPartnerId;
}

// ---------- Phase 3: rigger tubes ----------
public class Bone
{
    public string id;
    public string name;                  // "Hips", "UpperArm.L", ...
    public string parentId;              // null = root
    public Vec3 head;                    // start joint (the bone's pivot)
    public Vec3 tail;                    // end joint
    public float roll;
    public AxisLimits limits;            // null = free
}

public class AxisLimits { public Vec3 min; public Vec3 max; }   // degrees, per axis

// ---------- small helpers ----------
public struct Vec3 { public float x, y, z; }
public struct Quat { public float x, y, z, w; public static readonly Quat Identity = new() { w = 1 }; }

public class EditorState
{
    public float gridSize = 0.25f;
    public SnapMode snapMode = SnapMode.Grid;   // Grid / Snap (Valheim points) / Free
    public bool mirrorX = false;
    public string[] hotbar = new string[9];   // shape names or blueprint ids
}
```

**One change from your spec:** in your notes, an instance is "blueprintId + transform + linked flag". Here, an
**unlinked** placement turns straight into normal pieces (that's what "unlinked" means in practice), so only linked
copies are stored as instances and the flag isn't needed. "Unlink" = a command that swaps the instance for a copy
of its pieces. It works the same for you, with one less thing that could go out of sync.

**Undo/redo (commands):**
```csharp
public interface ICommand
{
    string Label { get; }                 // "Place box", shown in the undo history
    void Do(BuildContent c);
    void Undo(BuildContent c);
}
```
- There are only a few command types, and they save **before/after copies** of what changed:
  `AddItems`, `RemoveItems`, `ModifyItems`, plus group/bone commands. A `CompositeCommand` bundles several, so
  "place + its mirror copy" is one undo step.
- `UndoStack` runs commands, keeps history (limit around 200), and raises a `Changed(ids)` event.
- `View/DocumentView` listens and rebuilds **only the changed** GameObjects (fast even with many pieces).

**Saving:**
- JSON via **Newtonsoft Json** (free, made by Unity). Unity's built-in `JsonUtility` can't do `null`, dictionaries,
  or the "open old file as raw JSON and upgrade it" step that migration needs.
- Load = read raw JSON, check `schemaVersion`, run the upgraders in `Migration/`, then turn it into classes.
- Files: `persistentDataPath/projects/{id}.json`, `.../blueprints/{id}.json` + `{id}.png`, `.../settings/bindings.json`.
- Phase 4 sync status (last synced revision, cloud time) goes in a **separate** small file `sync/state.json`, not
  inside the project, so the project file stays clean.

---

## 3. Packages

| Package | Why | Free? | Status |
|---|---|---|---|
| Input System `com.unity.inputsystem` 1.13.0 | keyboard/mouse/gamepad/touch, rebinding | free (Unity) | already in |
| URP `com.unity.render-pipelines.universal` 17.0.3 | graphics | free (Unity) | already in |
| uGUI `com.unity.ugui` 2.0.0 (includes TextMeshPro) | all UI | free (Unity) | already in |
| Test Framework `com.unity.test-framework` | automatic tests for Core | free (Unity) | already in |
| **glTFast** `com.unity.cloud.gltfast` 6.20.0 | GLB export at runtime | free (Unity, Apache 2.0) | added |
| **Newtonsoft Json** `com.unity.nuget.newtonsoft-json` 3.2.2 | save files | free (Unity, MIT) | added |
| Firebase Auth + Firestore | Phase 4 only | free plan (Spark): limits on reads/writes per day | later |

**Remove** (from the template, not needed; makes builds smaller and less cluttered): Visual Scripting, Multiplayer Center,
Unity Version Control (`collab-proxy`, we use git), AI Navigation. You can add them back any time.

**UI system: uGUI** (not UI Toolkit). Gamepad navigation, the Input System's rebinding UI and its on-screen joystick
(Phase 5) are all built for uGUI and are the most proven there.

**Gizmos and handles:** our own code (no paid assets). **Grid floor:** a simple shader on a big plane. **Sky:** URP's
built-in procedural sky.

---

## 4. Unity Editor settings

I can set most of these myself by editing the project files while Unity is **closed**:
1. **Active Input Handling → "Input System Package (New)"** (right now it's "Both"). Spec: new Input System for everything.
2. **Color space: Linear.** Already set.
3. **Scripting backend:** Windows = Mono (no extra install needed); Android = IL2CPP + ARM64 (Google Play requires it,
   and it's faster on phones).
4. Company name / product name / app id (`com.<you>.mechbuilder`?), and the app icon later.
5. Delete the template's tutorial files (`TutorialInfo`, `Readme.asset`) and the sample input file.

**Done 2026-10-15 (Unity 6000.0.84f1 + these modules installed):**
- Unity Hub → **Installs** → on 6000.0.38f1 click the **gear** → **Add modules** → tick **Android Build Support**
  (with **OpenJDK** and **Android SDK & NDK Tools**) → Install. It's about 3–5 GB and free.
  (Right now only Windows and Web build support are installed.)

**Version:** Unity **6000.0.84f1** (Unity 6 LTS). Later 6000.0.x patches only fix bugs; update any time.
