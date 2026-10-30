# AY's 3D Builder

A game-style "creative mode" 3D modeling app made with **Unity 6**. You fly around and build low-poly models
(mainly mechs, also vehicles and structures) the way you build in Minecraft creative or Space Engineers, not the way
you model in Blender. Models are exported as **.glb** (with bones) and can go straight into a Unity game.

Runs on **Windows** and **Android**, with keyboard + mouse, gamepad, and touch.

> Screenshots: _coming soon_ (`docs/screenshots/`)

## Features

- **Fly camera and orbit camera**, game-style controls, everything rebindable in the in-app menu (saved to disk).
- **Parametric shapes** made as procedural meshes: box, wedge, corner wedge, cylinder, half cylinder, cone, sphere,
  beam, plate, with size, bevel, taper, hollow and segment settings. Flat shading, low poly.
- **Placement modes**: GRID (grid sizes 1 / 0.5 / 0.25 / 0.1), SNAP (snap points on corners, edges and faces with a
  choosable attach point) and FREE. Ghost preview, 3-axis rotation with step sizes, flip, scale wheel.
- **Precision mode** for exact moves and placement.
- **Select tool**: click, box select and paint-select; move / rotate / resize handles; edit panel for shape values.
- **Mirror build** (X axis): placing, editing and deleting all mirror to the other side.
- **Eraser with Cut mode**: remove whole pieces, or cut away only the overlapping part (BSP-based CSG subtract).
- **Points + hull**: place points and turn them into a solid convex hull (custom plates and shapes).
- **Paint** with a color picker (hue bar, hex code, matte / metal finishes) and spray-style painting.
- **Groups** (Head, Body, LeftArm ...) with show/hide and an editable pivot.
- **Blueprints**: save a selection as a reusable part with an auto thumbnail; **linked copies** update everywhere
  when the blueprint changes; blueprints can contain blueprints.
- **Built-in part library** of ~280 CC0 parts (mech parts, vehicles, weapons, structures).
- **Bones ("rigger tubes")**: build a skeleton (biped template or by hand), skeleton tree, bone handles,
  auto-bind pieces to bones, pose test. Mech frame presets with parts already on the bones.
- **Undo / redo** for every action (command pattern).
- **Projects**: create, rename, delete, autosave, import / export as JSON. Versioned save format with migrations.
- **Export**: .glb (glTFast; groups and bone hierarchy as named transforms, colors that survive URP import) and .obj.
- **Import**: .glb / .gltf (keeps colors), .obj, and 3D-print files (.stl, .3mf with colors).
- **Cloud sync (optional)**: your own projects and blueprints on PC and phone through your own free Firebase project,
  using Firebase's REST API (no plugin, same code on Windows and Android). See `docs/CLOUD_SETUP.md`.
- **Unity helper** (`3Dmodeler/Assets/UnityHelper`): a small component that lets an exported mech play Humanoid
  (e.g. Mixamo) animations in another Unity project.

## Controls (default, keyboard + mouse)

| Key | Action |
|---|---|
| W A S D, mouse | Fly, look |
| Space / Left Alt | Up / down |
| Left Shift | Fast |
| Left click / Right click | Place / delete |
| 1-9, middle mouse + wheel | Hotbar item |
| Tab | Inventory |
| R, mouse wheel | Rotate axis, turn the piece (Shift+R = rotation step) |
| E | Scale mode (wheel scales) |
| J | Flip |
| F | Placement mode: GRID / SNAP / FREE |
| G | Grid size |
| Q | Snap / attach point |
| V | Precision mode |
| I | Build / Select tool |
| M | Mirror |
| P / T | Paint tool / color window |
| X / C | Eraser / eraser catch mode (overlap, inside, cut) |
| B | Build solid from points |
| H | Groups panel |
| K | Bones panel |
| O | Orbit camera |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C / Ctrl+V / Ctrl+D | Copy / paste / duplicate |
| Ctrl+S / Ctrl+E | Save / export |
| Esc | Menu (settings, controls, projects, export, cloud) |
| F1 | Help |

Gamepad: left stick move, right stick look, RT place, LT delete, LB / RB hotbar, A up, B down, X rotate axis,
Y build / select tool, D-pad turn / snap / grid, Start menu, Select inventory. Touch controls on Android.

## Tech

- **Unity 6** (6000.0.84f1 LTS), **URP**, C#
- **Input System** (one action asset for keyboard/mouse, gamepad and touch; runtime rebinding)
- **glTFast** (runtime .glb export / import), **Newtonsoft Json** (save files)
- **uGUI + TextMeshPro** for all UI; custom shaders for the grid, ghost preview and overlays
- **Unity Test Framework**: about 185 EditMode and PlayMode tests

## Architecture

- **Document / view split.** The model is a plain C# document (`Scripts/Core/Document`: pieces, groups, blueprint
  instances, bones) that is saved as versioned JSON. GameObjects are only a picture of it: `Scripts/View` rebuilds
  just the changed objects when the document changes.
- **Commands for every edit** (`Scripts/Core/Commands`): add / remove / modify commands store before/after copies;
  an undo stack runs them, so undo / redo, mirror edits and linked blueprints all go through one path.
- **Assemblies**: `Builder.Core` (pure C#, cannot touch scene code, easy to test), `Builder.Runtime` (shapes,
  tools, camera, input, UI, IO) and the test assemblies.
- **Folders**: `Shapes` (procedural mesh generators + CSG), `Tools` (build, select, paint, erase, bones),
  `Input`, `UI`, `IO` (projects, blueprints, part library, GLB / OBJ / print import-export, cloud sync).
- **Key bindings** are generated by `tools/gen_input.py` with stable ids, so saved rebinds keep working.

More detail: `docs/DESIGN.md` (technical design) and `docs/SPEC.md` (product brief and later decisions).

## How to open and build

1. Install **Unity 6000.0.84f1** (Unity Hub, free Personal license). Add **Android Build Support** if you want the APK.
2. Unity Hub -> **Add** -> **Add project from disk** -> choose the `3Dmodeler` folder.
3. Open the scene `Assets/Scenes/Builder.unity` and press Play.
4. Build from the menu **Builder -> Build Windows exe** (output: `Build/Windows/AY3DBuilder.exe`) or
   **Builder -> Build Android apk** (output: `Build/Android/AY3DBuilder.apk`).

Headless (Unity closed):

```
Unity.exe -batchmode -nographics -projectPath 3Dmodeler -runTests -testPlatform EditMode -testResults results.xml
Unity.exe -batchmode -nographics -projectPath 3Dmodeler -executeMethod Builder.EditorTools.BuilderBuild.BuildWindows -quit
```

## Credits

The built-in part library uses CC0 models by Kenney and by Quaternius and others on Poly Pizza; see
`3Dmodeler/Assets/StreamingAssets/Library/CREDITS.md`. Liberation Sans font (SIL OFL) via TextMesh Pro.

## License

MIT, see `LICENSE`. Third-party assets keep their own licenses (listed above).
