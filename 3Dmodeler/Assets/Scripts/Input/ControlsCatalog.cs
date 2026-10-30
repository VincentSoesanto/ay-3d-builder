using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Builder.Input
{
    /// <summary>
    /// The list of controls shown in the Controls page (rebinding) and the F1 help: a friendly name, which
    /// keyboard/mouse binding of which action it is, and the gamepad button (fixed for now).
    /// </summary>
    public static class ControlsCatalog
    {
        public const string KeyboardGroup = "Keyboard&Mouse";

        public class Entry
        {
            public string section;     // heading it is listed under
            public string label;       // e.g. "Fly up"
            public string action;      // action name in BuilderControls, or null for fixed controls
            public string part;        // composite part ("up", "binding" ...) or null
            public int nth;            // the n-th plain keyboard/mouse binding of the action (0 = first)
            public bool ctrl;          // shown as "Ctrl + key" (shortcut with Ctrl)
            public bool shift;         // shown as "Shift + key"
            public string pad;         // gamepad button, shown in the help
            public string fixedKeys;   // keys that can't be changed (e.g. "Mouse"), shown instead of a binding
            public bool inHelp = true; // listed in the F1 help (hotbar 2-9 are not, to keep it short)

            public bool Rebindable => action != null && fixedKeys == null;

            /// <summary>Index of this entry's binding inside the action, or -1 if it isn't there.</summary>
            public int BindingIndex(InputAction a)
            {
                var b = a.bindings;
                for (int i = 0, n = 0; i < b.Count; i++)
                {
                    if (!InKeyboardGroup(b[i])) continue;
                    if (part != null)
                    {
                        if (b[i].isPartOfComposite && b[i].name == part) return i;   // first composite with that part
                    }
                    else if (!b[i].isComposite && !b[i].isPartOfComposite)
                    {
                        if (n == nth) return i;
                        n++;
                    }
                }
                return -1;
            }

            /// <summary>What to show for the key, e.g. "W", "Ctrl + Z", "Left Button".</summary>
            public string KeyText(InputActionAsset asset)
            {
                if (fixedKeys != null) return fixedKeys;
                var a = asset.FindAction(action);
                int index = a != null ? BindingIndex(a) : -1;
                if (index < 0) return "-";
                string key = a.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
                if (string.IsNullOrEmpty(key)) key = "(none)";
                return ctrl ? "Ctrl + " + key : shift ? "Shift + " + key : key;
            }

            /// <summary>Which binding this is ("Action#index"); two entries with the same id are the same key.</summary>
            public string BindingId(InputActionAsset asset)
            {
                var a = action != null ? asset.FindAction(action) : null;
                int index = a != null ? BindingIndex(a) : -1;
                return index < 0 ? null : action + "#" + index;
            }

            /// <summary>The key's path after any change (e.g. "&lt;Keyboard&gt;/g"), used to find double-used keys.</summary>
            public string EffectivePath(InputActionAsset asset)
            {
                var a = action != null ? asset.FindAction(action) : null;
                int index = a != null ? BindingIndex(a) : -1;
                if (index < 0) return null;
                return (ctrl ? "ctrl+" : shift ? "shift+" : "") + a.bindings[index].effectivePath;
            }
        }

        static bool InKeyboardGroup(InputBinding b) => b.groups != null && b.groups.Contains(KeyboardGroup);

        static Entry E(string section, string label, string action, string pad, string part = null, int nth = 0,
                       bool ctrl = false, string fixedKeys = null, bool inHelp = true, bool shift = false) =>
            new Entry { section = section, label = label, action = action, pad = pad, part = part, nth = nth,
                        ctrl = ctrl, shift = shift, fixedKeys = fixedKeys, inHelp = inHelp };

        public static readonly List<Entry> All = Build();

        static List<Entry> Build()
        {
            const string Move = "Moving", Wheel = "Mouse wheel", Place = "Building", Precise = "Precision mode (V)",
                         Erase = "Eraser (X)", Select = "Select tool (I)", Paint = "Paint tool (P)", Edit = "Editing", Other = "Other";
            // Some controls are listed again under a mode (e.g. left click in the eraser). It is the same key:
            // changing it in one place changes it everywhere.
            var list = new List<Entry>
            {
                E(Move, "Move forward", "Move", "Left stick", part: "up"),
                E(Move, "Move back", "Move", "Left stick", part: "down", inHelp: false),
                E(Move, "Move left", "Move", "Left stick", part: "left", inHelp: false),
                E(Move, "Move right", "Move", "Left stick", part: "right", inHelp: false),
                E(Move, "Look around", null, "Right stick", fixedKeys: "Mouse"),
                E(Move, "Fly up", "Up", "A"),
                E(Move, "Fly down", "Down", "B"),
                E(Move, "Fly faster (hold)", "Fast", "Press left stick"),
                E(Move, "Orbit camera", "ToggleCamera", "Press right stick"),

                E(Wheel, "Wheel: rotate / next axis", "RotateAxis", "X"),
                E(Wheel, "Turn step 90 / 45 / 15 / 5", "RotateStep", "-", part: "binding", shift: true),
                E(Wheel, "Flip (rotate mode): Y=left/right, X=up/down, Z=front/back", "Flip", "-"),
                E(Wheel, "Wheel: size", "ScaleMode", "-"),
                E(Wheel, "Wheel: hotbar item", "ItemMode", "-"),
                E(Wheel, "Use the wheel", null, "D-pad left / right", fixedKeys: "Mouse wheel"),

                E(Place, "Place", "Place", "RT"),
                E(Place, "Delete (aimed piece)", "Delete", "LT"),
                E(Place, "Grid size", "GridSize", "D-pad up"),
                E(Place, "Mode grid / snap / free", "SnapToggle", "D-pad down"),
                E(Place, "Attach point (snap mode)", "SnapPoint", "D-pad up (snap)"),
                E(Place, "Attach point list", null, "-", fixedKeys: "Hold Q + wheel", inHelp: false),
                E(Place, "Mirror on / off", "Mirror", "-"),
                E(Place, "FREE: how far the held item floats", null, "-", fixedKeys: "Ctrl + wheel", inHelp: false),
                E(Place, "Build solid from points", "BuildHull", "-"),
                E(Place, "Rigger tube: start / end joint", null, "RT", fixedKeys: "Click, click", inHelp: false),
                E(Place, "Rigger tube: stop chain / delete aimed bone", null, "LT", fixedKeys: "Right click", inHelp: false),
                E(Place, "Inventory", "Inventory", "View"),
                E(Place, "Hotbar 1", "Slot1", "LB / RB"),
            };
            for (int i = 2; i <= 9; i++) list.Add(E(Place, "Hotbar " + i, "Slot" + i, "LB / RB", inHelp: false));
            list.AddRange(new[]
            {
                E(Precise, "Precision mode on / off", "Precision", "-"),
                E(Precise, "Move the piece", null, "Right stick", fixedKeys: "Mouse", inHelp: false),
                E(Precise, "Move up / down", null, "-", fixedKeys: "Mouse wheel", inHelp: false),
                E(Precise, "Look around (piece stays)", null, "-", fixedKeys: "Hold right mouse", inHelp: false),
                E(Precise, "Place / erase there", "Place", "RT", inHelp: false),

                E(Erase, "Eraser tool", "EraseTool", "Y (all tools)"),
                E(Erase, "Mode overlap / inside / cut", "EraseCatch", "D-pad up (eraser)"),
                E(Erase, "Erase / cut", "Place", "RT", inHelp: false),
                E(Erase, "Rotate / size / grid", null, "-", fixedKeys: "as when building", inHelp: false),

                E(Select, "Select tool", "ToggleTool", "Y (all tools)"),
                E(Select, "Select / add more", null, "RT", fixedKeys: "Click / Shift+click", inHelp: false),
                E(Select, "Box select (Ctrl = add)", null, "-", fixedKeys: "Drag / Ctrl+drag", inHelp: false),
                E(Select, "Add what the mouse passes over", null, "-", fixedKeys: "Shift+drag", inHelp: false),
                E(Select, "Look around", null, "Right stick", fixedKeys: "Hold right mouse", inHelp: false),
                E(Select, "Move / turn / resize", null, "-", fixedKeys: "Drag arrows / rings / cubes", inHelp: false),
                E(Select, "Delete selection", "Delete", "LT", nth: 1),
                E(Select, "Groups panel", "GroupsPanel", "-"),
                E(Select, "Bones panel (bind, limits, pose test)", "BonesPanel", "-"),
                E(Select, "Bones: wheel = size of the chosen bones", "ScaleMode", "-", inHelp: false),
                E(Select, "Bones: choose more", null, "-", fixedKeys: "Ctrl / Shift + click", inHelp: false),

                E(Paint, "Paint tool", "PaintTool", "Y (all tools)"),
                E(Paint, "Paint the piece / spray (hold)", "Place", "RT", inHelp: false),
                E(Paint, "Pick a color (eyedropper)", "Delete", "LT", inHelp: false),
                E(Paint, "Whole piece / spray paint", "EraseCatch", "-"),
                E(Paint, "Spray brush size (1 / 3 / 5)", "ScaleMode", "-", inHelp: false),
                E(Paint, "Spray square size (= grid size)", "GridSize", "-", inHelp: false),
                E(Paint, "Rub spray paint off (hold)", null, "-", fixedKeys: "Shift + hold click", inHelp: false),
                E(Paint, "Colors window (own colors, metal, glow)", "ColorWindow", "-"),
                E(Paint, "Choose color", null, "LB / RB", fixedKeys: "1-9 / wheel", inHelp: false),

                E(Edit, "Undo", "Undo", "-", part: "binding", ctrl: true),
                E(Edit, "Redo", "Redo", "-", part: "binding", ctrl: true),
                E(Edit, "Copy", "Copy", "-", part: "binding", ctrl: true),
                E(Edit, "Paste", "Paste", "-", part: "binding", ctrl: true),
                E(Edit, "Stop pasting", "Delete", "LT", inHelp: false),
                E(Edit, "Duplicate", "Duplicate", "-", part: "binding", ctrl: true),
                E(Edit, "Save project", "Save", "-", part: "binding", ctrl: true),
                E(Edit, "Export .glb", "Export", "-", part: "binding", ctrl: true),

                E(Other, "Menu (settings)", "Menu", "Start", fixedKeys: "Esc"),
                E(Other, "Help (this list)", "Help", "-"),
            });
            return list;
        }
    }
}
