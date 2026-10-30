# AY's 3D Builder: product spec

The original product brief, written by the project planner on 2026-10-15 and kept as written.
Decisions made later during development are listed at the end. The technical design is in `DESIGN.md`.

---

You are helping me build a Unity app: a game-style "creative mode" 3D builder for making low-poly game models (mainly mechs, also structures) that I will import into my separate Unity mech game. It must feel like playing a sandbox builder (Minecraft creative / Space Engineers), not like Blender. I am new to Unity, so explain Unity-specific setup steps (packages, project settings, scene setup) clearly when I need to do them in the Editor.

PLATFORMS & TECH
- Unity 6 LTS, URP. Targets: Windows (.exe, main platform) and Android (.apk).
- New Input System for everything. Must support keyboard+mouse AND gamepad (Xbox/PS/generic) on both Windows and Android (via Bluetooth or OTG). Touch controls come in a later phase.
- glTFast (com.unity.cloud.gltfast) for GLB export at runtime.
- Firebase Unity SDK (Auth + Firestore) in a later phase only.
- Architecture: keep the build data as a plain serializable document (C# classes → JSON) separate from the GameObjects that display it. GameObjects are rebuilt from the document. All edits go through commands (for undo/redo).
- Folder structure: Scripts/Core (document, commands, undo), Scripts/Shapes (procedural mesh generators), Scripts/Tools (place, delete, select, paint, etc.), Scripts/Input, Scripts/UI, Scripts/IO (save/load/export), Scripts/Sync (later).
- Save format is versioned JSON (include schemaVersion) so older saves keep working.

========== PHASE 1 — CORE BUILDER ==========
1. Scene: grid floor, lighting, sky. Fly camera (WASD + mouse look, Space/Ctrl up/down, Shift fast) and an orbit camera toggle (Tab) that orbits the selection or the model.
2. Parametric shapes generated as procedural meshes from parameters (not fixed models): box, wedge/slope, corner wedge, cylinder, half-cylinder, cone, sphere, beam, plate. Params: length, width, thickness/height, bevel, taper, hollow (where it applies). Low poly, flat shading, correct normals.
3. Hotbar with 9 slots (keys 1–9, mouse wheel, gamepad bumpers) + a creative-style inventory screen (E / gamepad button) to assign shapes to slots.
4. Placement: ghost preview at the crosshair, snapping. R rotates before placing. Left click / RT places, right click / LT deletes.
   Snap toggle: GRID (default; grid sizes 1, 0.5, 0.25, 0.1) or FREE (attach face-to-face on any surface).
5. Select tool: select pieces (multi-select with Shift). Edit panel with sliders + number fields for the shape params. Draggable resize handles on the piece. Move/rotate gizmo. All usable with gamepad too (cycle fields, adjust values with sticks/d-pad).
6. Mirror build toggle (X axis): place/edit/delete mirrors to the other side.
7. Copy / paste / duplicate, with ghost preview when pasting.
8. Paint tool with a color palette (per-piece color, stored in the document).
9. Groups: user-named groups (Head, Body, LeftArm, RightArm, LeftLeg, RightLeg, etc.), group list panel with show/hide and select-all, editable pivot per group.
10. Undo/redo for every action.
11. Projects: create, rename, delete, save, load. Autosave locally (Application.persistentDataPath). Import/export a project as a JSON file.
12. Export to GLB with glTFast: each group = one named GameObject with its pivot at the group pivot; pieces in a group merged into one mesh; ungrouped pieces under "Ungrouped". 1 unit = 1 meter. Use colors that survive import into URP (vertex colors or simple per-color materials) — verify by importing into a separate Unity project.
13. Input: full rebinding screen for keyboard/mouse and gamepad, saved to disk. Mouse cursor lock for camera look on Windows and on Android with a physical mouse (verify; if not supported, use a fallback such as hold-right-click to look).
14. HUD: crosshair, current tool, current shape, snap mode, grid size, mirror state, hotbar.
15. Android build must run and be fully usable with a gamepad or keyboard+mouse.

Suggested gamepad layout (rebindable): left stick move, right stick look, A/Cross jump-up, B/Circle down, RT place, LT delete, LB/RB hotbar, Y/Triangle select tool, X/Square rotate, D-pad grid size / snap toggle, Start menu, Select inventory.

========== PHASE 2 — BLUEPRINTS ==========
- Select pieces → "Save as Blueprint": name, category, auto thumbnail (render the selection), pivot at the bottom center (editable).
- Blueprints appear in the inventory by category with search; can be put on the hotbar.
- Place like a single piece: ghost, snap, rotate, mirror.
- LINKED instances (editing the blueprint updates all copies, like Unity prefabs) or UNLINKED (copied as normal pieces). Any linked instance can be unlinked.
- "Edit blueprint" mode. Blueprints can contain other blueprints (nesting).
- Blueprint library is separate from projects (usable in every project). Export/import a single blueprint as a file.
- Data: Blueprint { id, name, category, thumbnail, pieces[], childInstances[], pivot, version }. Scene instance = blueprintId + transform + linked flag.

========== PHASE 3 — RIGGER TUBES (BONES) ==========
- Special piece "Rigger Tube" = a bone, with a start joint and end joint, shown as a glowing tube with joint balls. Tubes snap end-to-end to form a parent/child hierarchy.
- Each tube has a name (presets: Hips, Spine, Chest, Neck, Head, Shoulder/UpperArm/LowerArm/Hand .L/.R, UpperLeg/LowerLeg/Foot .L/.R, plus custom) and optional rotation limits per axis.
- Bind pieces or groups rigidly to a bone (100% weight, no weight painting).
- Pose test mode: rotate joints to check movement and clipping; reset pose.
- Blueprints can include bones.
- Export: the GLB includes the bone hierarchy as named transforms, with the bound meshes as children of their bones. Rigger tube visuals are NOT exported. Verify the result can be animated in Unity.

========== PHASE 4 — FIREBASE PERSONAL SYNC ==========
- Purpose: the SAME user's projects and blueprints stay in sync across their own devices (PC + phone). No sharing or collaboration between users.
- Firebase Auth: email/password on all platforms; Google sign-in on Android (and Windows only if it works reliably). The app works fully without signing in (local-only mode).
- Firestore layout: users/{uid}/projects/{id} and users/{uid}/blueprints/{id}. A metadata doc (name, updatedAt, deviceId, version, thumbnail) + piece data stored compactly and split into chunk docs under ~800KB each.
- Offline-first: local files are the source of truth while editing; background sync (debounced) when online; pull updates on app start and when the cloud has a newer version.
- Conflicts: if both the cloud and local copies changed, ask: keep this device / keep cloud / save both.
- Security rules: users can only read/write their own data.
- Sync status icon in the HUD. Stay within Firebase's free plan (Auth + Firestore only).
- Note: Firebase's Unity SDK on Windows desktop is officially for development use — test sign-in and sync on Windows early and tell me if anything is unreliable.

========== PHASE 5 — MOBILE TOUCH CONTROLS (OPTIONAL) ==========
- Minecraft-style: left virtual joystick to move, drag on the right side to look, tap to place, hold to delete, on-screen buttons for up/down/rotate/tool/undo/hotbar.
- Layout edit mode: drag and resize every on-screen button; layout saved.
- If no touch, gamepad and keyboard/mouse still work as in Phase 1.

========== HOW TO WORK WITH ME ==========
- Start by showing me: the folder structure, the document/data model classes (designed so Phases 2–4 fit without refactoring), and the list of packages + Editor settings I need to set up. Wait for my OK.
- Then build Phase 1 in small chunks, stopping after each so I can test:
  Chunk 1: scene, grid, fly/orbit camera, place/delete a box with ghost + grid snap, keyboard/mouse + gamepad.
  Chunk 2: an early GLB export of the placed boxes (so I can test import into my game project right away).
  Chunk 3: all shapes, hotbar, inventory.
  Chunk 4: select tool, sliders, resize handles, gizmo.
  Chunk 5: mirror, copy/paste, paint, undo/redo.
  Chunk 6: groups, pivots, full GLB export.
  Chunk 7: projects, save/load, autosave, rebinding screen, HUD polish.
  Chunk 8: Android build test with gamepad and keyboard/mouse.
- After each chunk, tell me exactly what to test and any Unity Editor steps I need to do.
- Keep code clean and commented; I am learning Unity through this project.

========== ADDITIONS (agreed with the user later) ==========
- 2026-10-15 — App name: "AY's 3D Builder" (Android id com.ay.builder3d).
- 2026-10-15 — SHAPE ERASER (bulk removal): pick a shape (and size) like when placing; a ghost of that shape follows
  the crosshair (shown in red); clicking REMOVES every piece that overlaps the ghost instead of placing one.
  Works with grid snap, rotate (R), mirror, and is one undo step. Planned for Chunk 5 (after undo/redo exists).
  Eraser options: remove pieces that are FULLY INSIDE the ghost, or every piece the ghost is TOUCHING.
- 2026-10-15 — PRECISE CONTROL MODE (toggle): the mouse stops turning the camera and instead moves the ghost/object
  along its X/Y/Z coordinates, for exact positioning. Works for ALL tools (place, eraser, select/move, paste, ...).
  Keyboard and gamepad equivalents too. Details decided when we build it.
- 2026-10-15 — CHUNK ORDER: Chunk 2 (early GLB export) moved later, after Chunk 5 (build the editor first).
  New order: 1, 3, 4, 5, 2, 6, 7, 8. The HUD gets redone properly in Chunk 3 (with the hotbar).
- 2026-10-15 — ROTATION + KEYS (after testing Chunk 3, Valheim style): R switches the rotation axis (Y -> X -> Z),
  mouse wheel turns the piece 90 degrees around that axis (wheel no longer changes the hotbar; 1-9 / LB RB do).
  Tab = inventory / block list (was E). O = orbit camera (was Tab), "for now".
  Gamepad: X switches axis, D-pad left/right turns.
- 2026-10-15 — FREE MODE + SNAP POINTS (after testing): FREE keeps the user's rotation (no tilting to the surface).
  Valheim-style snap points (box corners, edge middles, face middles). Q (gamepad: D-pad up in FREE mode) cycles
  which point of the ghost is the attach point ("pivot"): Auto (nearest pair) -> 8 corners -> 6 faces -> 12 edges.
  The chosen point attaches to the aimed piece's point closest to the crosshair. Orange dot shows the chosen point.
- 2026-10-15 — THREE PLACEMENT MODES (F cycles): GRID (grid lines), SNAP (Valheim snap points + attach point),
  FREE (no snapping at all; overlapping allowed; exact moving comes with precision mode later).
  Hold Q in SNAP mode: a list of attach points shows, mouse wheel scrolls it; tap Q = next.
  Ghost shows a small, faint X (red) / Y (green) / Z (blue) axis indicator; the current rotate axis is brighter.
- 2026-10-15 — WHEEL MODES: R = wheel rotates (R again = next axis), E = wheel scales the piece (x0.1 .. x10 in friendly
  steps), middle mouse = wheel picks the hotbar item. I = Select tool (was middle mouse).
  ERASER (X) uses the same controls as placing (rotate, scale, grid/snap/free, mirror) but the red ghost goes INTO the
  aimed surface and removes what it catches; C = overlap / fully inside.
- 2026-10-15 — IDEA (not built yet): eraser "Cut" mode = remove only the overlapping PART of a piece (boolean / CSG cut).
  Planned as "remembered cuts" (the piece keeps its sizes + a list of cutter shapes; mesh recalculated), after GLB
  export + groups (Chunk 6), so cut pieces can be checked in the export right away.
- 2026-10-15 — ESC = MENU: Esc opens a menu (instead of freeing the mouse). First item: Settings -> controls,
  where the user can remap the PC hotkeys (saved). Bones = Phase 3 (Rigger Tubes), unchanged.
- 2026-10-16 — BLUEPRINTS: NO "Edit blueprint" mode (planner: place it in a clear spot, edit, select, save again).
- 2026-10-16 — POINTS (inventory category "Points"; the planner's word was "chekpo", name can change): a Point item = small
  orange ball marking a corner (GRID: on grid crossings, SNAP: on the aimed piece's corners/edges/faces, FREE: anywhere).
  B builds the solid wrapped around the points (convex hull: 8 block corners = that block) and removes the points.
  Uses the selected points (Select tool) or all points. The solid is a normal piece; points are not exported.
- 2026-10-16 — Points: 2 points = stick, 3 / flat points = thin plate. Precision mode: mouse wheel = up/down.
  Controls page lists every control per mode (building, precision, eraser, select, paint ...).
- 2026-10-16 — Turn step: Shift+R = 90 / 45 / 15 / 5 degrees. Reference item: Human 1.8 m (size guide, not exported).
- 2026-10-16 — Same blueprint name = replace it. FLIP: J in rotate mode mirrors the piece by the rotate axis
  (Y = left/right, X = up/down, Z = front/back). IMPORT 3D MODEL: Esc menu page; a .glb/.gltf/.obj file becomes an item
  (inventory > Models), shape only, paint it in the app.
- 2026-10-16 — LINKED BLUEPRINTS (planner: "linked, everywhere"): a placed blueprint is a linked copy. Saving the blueprint
  again (same name) updates every copy: inside other blueprints (Finger -> Hand -> Arm) and in projects. Editing one copy
  (not moving it whole) changes only that copy and unlinks it; the inventory blueprint and other copies stay the same.
- 2026-10-17 — Phase 2 done: rename / delete blueprints and models (right-click in the inventory), blueprint files
  (.aybp, Save as file + Esc > Blueprint files import). Phase 3 built: rigger tubes (place start + end joint, chains),
  Bones panel (K): bind pieces / groups, presets, limits, pose test; GLB export has the bone tree. Bones are not turned
  in the rest pose (axes = world X / Y / Z).
- 2026-10-17 — MORE PARTS (planner: "we need more 3D models", groups like gears, vents): built-in part library in inventory
  tabs (Mech parts with Gears / Vents / Armor / Joints & hydraulics / Thrusters & wings / Weapons / Wheels & tracks /
  Cockpit & details; Vehicles; Weapons; Space; Robots), from code + free CC0 models (Kenney, Quaternius, ...).
  Delete a bone: right click on it with the Rigger tube.
- 2026-10-17 — COLOR: own colors (Colors window, T), finishes Matte / Metal / Glow (also in the export), and SPRAY PAINT
  (planner: "like paint spray / pixel / grid: hold the mouse button to spray paint in grid sizes").
