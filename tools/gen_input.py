# Makes 3Dmodeler/Assets/Input/BuilderControls.inputactions (all keys and gamepad buttons).
# Run: python tools/gen_input.py   (Unity closed). Edit the action list below, then run it again.
import json, uuid
KM, GP = "Keyboard&Mouse", "Gamepad"
# Ids are made from the names (not random), so they stay the same every time this script runs.
# That matters: saved key changes (rebinding) are stored by these ids.
NS = uuid.UUID("6f1d3c2a-8b4e-4f7a-9c1d-2e3f4a5b6c7d")
def u(key): return str(uuid.uuid5(NS, key))
actions, bindings = [], []
def action(name, typ, ctrl):
    actions.append({"name": name, "type": typ, "id": u("action:" + name), "expectedControlType": ctrl,
                    "processors": "", "interactions": "", "initialStateCheck": typ == "Value"})
def bind(act, path, group):
    bindings.append({"name": "", "id": u(f"binding:{act}:{path}"), "path": path, "interactions": "", "processors": "",
                     "groups": group, "action": act, "isComposite": False, "isPartOfComposite": False})
def composite(act, kind, name, parts, group):
    bindings.append({"name": name, "id": u(f"composite:{act}:{name}"), "path": kind, "interactions": "", "processors": "",
                     "groups": "", "action": act, "isComposite": True, "isPartOfComposite": False})
    for part, path in parts:
        bindings.append({"name": part, "id": u(f"part:{act}:{name}:{part}"), "path": path, "interactions": "", "processors": "",
                         "groups": group, "action": act, "isComposite": False, "isPartOfComposite": True})

action("Move", "Value", "Vector2")
composite("Move", "2DVector", "WASD", [("up", "<Keyboard>/w"), ("down", "<Keyboard>/s"), ("left", "<Keyboard>/a"), ("right", "<Keyboard>/d")], KM)
bind("Move", "<Gamepad>/leftStick", GP)
action("LookDelta", "Value", "Vector2");  bind("LookDelta", "<Mouse>/delta", KM)
action("LookStick", "Value", "Vector2");  bind("LookStick", "<Gamepad>/rightStick", GP)
for name, key, pad in [("Up", "space", "buttonSouth"), ("Down", "leftAlt", "buttonEast"), ("Fast", "leftShift", "leftStickPress"),
                       ("RotateAxis", "r", "buttonWest"), ("ToggleCamera", "o", "rightStickPress"), ("GridSize", "g", "dpad/up")]:
    action(name, "Button", "Button"); bind(name, f"<Keyboard>/{key}", KM); bind(name, f"<Gamepad>/{pad}", GP)
action("Place", "Button", "Button");  bind("Place", "<Mouse>/leftButton", KM);  bind("Place", "<Gamepad>/rightTrigger", GP)
action("Delete", "Button", "Button"); bind("Delete", "<Mouse>/rightButton", KM); bind("Delete", "<Keyboard>/delete", KM); bind("Delete", "<Gamepad>/leftTrigger", GP)
action("Zoom", "Value", "Axis");      bind("Zoom", "<Mouse>/scroll/y", KM)
for name, key in [("Undo", "z"), ("Redo", "y"), ("Copy", "c"), ("Paste", "v"), ("Duplicate", "d"), ("Export", "e"), ("Save", "s")]:
    action(name, "Button", "Button")
    composite(name, "OneModifier", "Ctrl+" + key.upper(), [("modifier", "<Keyboard>/ctrl"), ("binding", f"<Keyboard>/{key}")], KM)
# Rotation step size (90 / 45 / 15 / 5 degrees): Shift+R.
action("RotateStep", "Button", "Button")
composite("RotateStep", "OneModifier", "Shift+R", [("modifier", "<Keyboard>/shift"), ("binding", "<Keyboard>/r")], KM)
# Redo also with Ctrl+Shift+Z.
composite("Redo", "TwoModifiers", "Ctrl+Shift+Z", [("modifier1", "<Keyboard>/ctrl"), ("modifier2", "<Keyboard>/shift"), ("binding", "<Keyboard>/z")], KM)
for name, key in [("Mirror", "m"), ("PaintTool", "p"), ("EraseTool", "x"), ("ScaleMode", "e"), ("EraseCatch", "c"), ("GroupsPanel", "h"), ("Precision", "v"), ("BuildHull", "b"), ("Flip", "j"), ("BonesPanel", "k"), ("ColorWindow", "t")]:
    action(name, "Button", "Button"); bind(name, f"<Keyboard>/{key}", KM)
# Middle mouse button: the wheel chooses the hotbar item.
action("ItemMode", "Button", "Button"); bind("ItemMode", "<Mouse>/middleButton", KM)
# Cancel: Esc frees the mouse while building; Esc / B closes menus.
action("Cancel", "Button", "Button"); bind("Cancel", "<Keyboard>/escape", KM); bind("Cancel", "<Gamepad>/buttonEast", GP)
for i in range(1, 10):
    action(f"Slot{i}", "Button", "Button"); bind(f"Slot{i}", f"<Keyboard>/{i}", KM)
action("HotbarNext", "Button", "Button"); bind("HotbarNext", "<Gamepad>/rightShoulder", GP)
action("HotbarPrev", "Button", "Button"); bind("HotbarPrev", "<Gamepad>/leftShoulder", GP)
# Turn the piece 90 degrees around the chosen axis: mouse wheel, or D-pad left/right.
action("TurnPlus", "Button", "Button");   bind("TurnPlus", "<Mouse>/scroll/up", KM);     bind("TurnPlus", "<Gamepad>/dpad/right", GP)
action("TurnMinus", "Button", "Button");  bind("TurnMinus", "<Mouse>/scroll/down", KM);  bind("TurnMinus", "<Gamepad>/dpad/left", GP)
action("Inventory", "Button", "Button");  bind("Inventory", "<Keyboard>/tab", KM);       bind("Inventory", "<Gamepad>/select", GP)
action("SnapToggle", "Button", "Button"); bind("SnapToggle", "<Keyboard>/f", KM);        bind("SnapToggle", "<Gamepad>/dpad/down", GP)
# FREE mode: choose which point of the ghost sticks to other pieces (gamepad: D-pad up in FREE mode, handled in code).
action("SnapPoint", "Button", "Button");  bind("SnapPoint", "<Keyboard>/q", KM)
# Switch between the Build and Select tools.
action("ToggleTool", "Button", "Button"); bind("ToggleTool", "<Keyboard>/i", KM); bind("ToggleTool", "<Gamepad>/buttonNorth", GP)
action("Help", "Button", "Button");       bind("Help", "<Keyboard>/f1", KM)
# The menu (settings, controls ...): Esc / gamepad Start.
action("Menu", "Button", "Button");       bind("Menu", "<Keyboard>/escape", KM);         bind("Menu", "<Gamepad>/start", GP)

dev = lambda p: {"devicePath": p, "isOptional": False, "isOR": False}
asset = {"name": "BuilderControls",
         "maps": [{"name": "Builder", "id": u("map:Builder"), "actions": actions, "bindings": bindings}],
         "controlSchemes": [{"name": KM, "bindingGroup": KM, "devices": [dev("<Keyboard>"), dev("<Mouse>")]},
                            {"name": GP, "bindingGroup": GP, "devices": [dev("<Gamepad>")]}]}
import os
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "3Dmodeler", "Assets", "Input", "BuilderControls.inputactions")
open(out, "w", encoding="utf-8", newline="\n").write(json.dumps(asset, indent=4) + "\n")
print(len(actions), "actions,", len(bindings), "bindings")
