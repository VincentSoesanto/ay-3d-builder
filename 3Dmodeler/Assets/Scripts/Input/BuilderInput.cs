using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Builder.Input
{
    /// <summary>
    /// The one place that knows about the input actions (BuilderControls.inputactions).
    /// Other scripts ask it "was Place pressed?" instead of reading keys directly, so keyboard/mouse and gamepad
    /// (and later touch and rebinding) all work the same.
    ///
    /// Mouse cursor, three situations:
    ///   - Building: cursor locked (hidden, used for looking). Opening the menu (Esc) frees it; a click locks it again.
    ///   - Pointer mode (Select tool): cursor free for clicking pieces and UI; hold the right mouse button to look.
    ///   - Menu open (inventory): cursor free, building and camera pause.
    /// While typing in a number box, game keys are ignored (so typing "2" doesn't switch the hotbar).
    /// </summary>
    [DefaultExecutionOrder(-200)]   // touch presses become visible at the very start of a frame, for everyone
    public class BuilderInput : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        public InputAction Move { get; private set; }
        public InputAction LookDelta { get; private set; }   // mouse, in pixels this frame
        public InputAction LookStick { get; private set; }   // gamepad stick, -1..1
        public InputAction Up { get; private set; }
        public InputAction Down { get; private set; }
        public InputAction Fast { get; private set; }
        public InputAction Place { get; private set; }
        public InputAction Delete { get; private set; }
        public InputAction RotateAxis { get; private set; }   // switch which axis the wheel turns around
        public InputAction RotateStep { get; private set; }   // Shift+R: turn step 90 / 45 / 15 / 5 degrees
        public InputAction TurnPlus { get; private set; }     // wheel up / D-pad right
        public InputAction TurnMinus { get; private set; }    // wheel down / D-pad left
        public InputAction ToggleCamera { get; private set; }
        public InputAction Zoom { get; private set; }
        public InputAction GridSize { get; private set; }
        public InputAction SnapToggle { get; private set; }
        public InputAction SnapPoint { get; private set; }    // SNAP mode: attach point (Q)
        public InputAction Undo { get; private set; }
        public InputAction Redo { get; private set; }
        public InputAction Cancel { get; private set; }
        public InputAction HotbarNext { get; private set; }
        public InputAction HotbarPrev { get; private set; }
        public InputAction Inventory { get; private set; }
        public InputAction ToggleTool { get; private set; }   // Build <-> Select (I / gamepad Y)
        public InputAction Help { get; private set; }
        public InputAction Menu { get; private set; }         // Esc / gamepad Start: the menu
        public InputAction Copy { get; private set; }         // Ctrl+C
        public InputAction Paste { get; private set; }        // Ctrl+V
        public InputAction Duplicate { get; private set; }    // Ctrl+D
        public InputAction Mirror { get; private set; }       // M
        public InputAction PaintTool { get; private set; }    // P
        public InputAction EraseTool { get; private set; }    // X
        public InputAction ScaleMode { get; private set; }    // E: the wheel scales
        public InputAction ItemMode { get; private set; }     // middle mouse: the wheel picks the hotbar item
        public InputAction EraseCatch { get; private set; }   // C: eraser overlap / fully inside
        public InputAction Export { get; private set; }       // Ctrl+E: export .glb
        public InputAction Save { get; private set; }         // Ctrl+S: save the project
        public InputAction Precision { get; private set; }    // V: precision mode on/off
        public InputAction BuildHull { get; private set; }    // B: build a solid from the points
        public InputAction Flip { get; private set; }         // J: mirror the ghost (rotate mode)
        public InputAction GroupsPanel { get; private set; }  // H: show/hide the Groups panel
        public InputAction BonesPanel { get; private set; }   // K: open/close the Bones panel
        public InputAction ColorWindow { get; private set; }  // T: the Colors window (Paint tool)
        /// <summary>Slot1..Slot9 (keys 1-9). Index 0 = slot 1.</summary>
        public InputAction[] Slots { get; private set; }

        public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>True while a menu (inventory, ...) is open: the cursor is free and building pauses.</summary>
        public bool MenuOpen { get; private set; }

        /// <summary>True in the Select tool: free cursor, hold right mouse to look.</summary>
        public bool PointerMode { get; private set; }

        /// <summary>
        /// Precision mode: the mouse (and gamepad right stick) moves the piece instead of turning the camera.
        /// The cursor stays locked so the mouse can move as far as needed.
        /// </summary>
        public bool PrecisionOn { get; private set; }

        /// <summary>True while game controls should do nothing (menu open, or typing in a text box).</summary>
        public bool Blocked => MenuOpen || Typing;

        /// <summary>True while a text/number box has the keyboard.</summary>
        public static bool Typing
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
                return field != null && field.isFocused;
            }
        }

        /// <summary>True when the mouse is over a UI panel (so clicks belong to the UI, not the world).</summary>
        public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>Mouse position in screen pixels.</summary>
        public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        int lockedOnFrame = -1;
        int menuChangedOnFrame = -1;

        /// <summary>All the actions (the Controls page changes their keys).</summary>
        public InputActionAsset Actions => actions;

        void Awake()
        {
            BindingStore.Load(actions);   // the player's own key choices, if any
            var map = actions.FindActionMap("Builder", throwIfNotFound: true);
            InputAction A(string name) => map.FindAction(name, throwIfNotFound: true);
            Move = A("Move");
            LookDelta = A("LookDelta");
            LookStick = A("LookStick");
            Up = A("Up");
            Down = A("Down");
            Fast = A("Fast");
            Place = A("Place");
            Delete = A("Delete");
            RotateAxis = A("RotateAxis");
            RotateStep = A("RotateStep");
            TurnPlus = A("TurnPlus");
            TurnMinus = A("TurnMinus");
            ToggleCamera = A("ToggleCamera");
            Zoom = A("Zoom");
            GridSize = A("GridSize");
            SnapToggle = A("SnapToggle");
            SnapPoint = A("SnapPoint");
            Undo = A("Undo");
            Redo = A("Redo");
            Cancel = A("Cancel");
            HotbarNext = A("HotbarNext");
            HotbarPrev = A("HotbarPrev");
            Inventory = A("Inventory");
            ToggleTool = A("ToggleTool");
            Help = A("Help");
            Menu = A("Menu");
            Copy = A("Copy");
            Paste = A("Paste");
            Duplicate = A("Duplicate");
            Mirror = A("Mirror");
            PaintTool = A("PaintTool");
            EraseTool = A("EraseTool");
            ScaleMode = A("ScaleMode");
            ItemMode = A("ItemMode");
            EraseCatch = A("EraseCatch");
            Export = A("Export");
            Save = A("Save");
            Precision = A("Precision");
            BuildHull = A("BuildHull");
            Flip = A("Flip");
            GroupsPanel = A("GroupsPanel");
            BonesPanel = A("BonesPanel");
            ColorWindow = A("ColorWindow");
            Slots = new InputAction[9];
            for (int i = 0; i < 9; i++) Slots[i] = A("Slot" + (i + 1));
        }

        void OnEnable() => actions.Enable();
        void OnDisable() => actions.Disable();

        void Start() => SetCursorLocked(true);

        // ---------- touch (on-screen controls, phones) ----------
        // On-screen buttons "press" the same actions as keys / gamepad buttons, so every tool works with them.
        // A press is collected during the frame (UI events) and seen by everyone during the whole next frame.

        readonly HashSet<InputAction> touchPending = new HashSet<InputAction>();
        readonly HashSet<InputAction> touchPressed = new HashSet<InputAction>();
        readonly HashSet<InputAction> touchHeld = new HashSet<InputAction>();
        Vector2 touchLookPending, touchLook;

        /// <summary>The on-screen move stick (-1..1).</summary>
        public Vector2 TouchMove { get; set; }

        /// <summary>A finger went down on an on-screen button for this action.</summary>
        public void TouchDown(InputAction action)
        {
            touchPending.Add(action);
            touchHeld.Add(action);
        }

        public void TouchUp(InputAction action) => touchHeld.Remove(action);

        /// <summary>A finger dragged on the look area (pixels).</summary>
        public void AddTouchLook(Vector2 pixels) => touchLookPending += pixels;

        void TouchFrame()
        {
            touchPressed.Clear();
            touchPressed.UnionWith(touchPending);
            touchPending.Clear();
            touchLook = touchLookPending;
            touchLookPending = Vector2.zero;
        }

        void Update()
        {
            TouchFrame();
            if (MenuOpen || Time.frameCount == menuChangedOnFrame) return;

            if (PrecisionOn)
            {
                if (!CursorLocked && Application.isFocused) SetCursorLocked(true);
                return;
            }

            if (PointerMode)
            {
                // Look around only while the right mouse button is held.
                bool look = Mouse.current != null && Mouse.current.rightButton.isPressed && Application.isFocused;
                if (look != CursorLocked) SetCursorLocked(look);
                return;
            }

            var mouse = Mouse.current;
            if (!CursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame && Application.isFocused)
            {
                SetCursorLocked(true);
                lockedOnFrame = Time.frameCount;
            }
        }

        /// <summary>Opens/closes "menu mode" (called by the inventory and later menus).</summary>
        public void SetMenuOpen(bool open)
        {
            MenuOpen = open;
            menuChangedOnFrame = Time.frameCount;   // the key that opened/closed the menu must not do anything else
            SetCursorLocked(!open && !PointerMode);
            if (!open) lockedOnFrame = Time.frameCount;
        }

        /// <summary>Turns precision mode on or off (see PrecisionMode).</summary>
        public void SetPrecision(bool on)
        {
            PrecisionOn = on;
            if (!MenuOpen) SetCursorLocked(on || !PointerMode);
            lockedOnFrame = Time.frameCount;
        }

        /// <summary>Turns pointer mode (Select tool) on or off.</summary>
        public void SetPointerMode(bool on)
        {
            PointerMode = on;
            if (!MenuOpen) SetCursorLocked(!on);
            lockedOnFrame = Time.frameCount;
        }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>
        /// For building/camera controls: true if the button went down this frame and controls are not blocked.
        /// Mouse buttons: while building, only with a locked cursor (otherwise the click was for the window or for
        /// re-locking); in pointer mode, only when the mouse is not over a UI panel. Keyboard/gamepad always count.
        /// </summary>
        public bool Pressed(InputAction action)
        {
            if (Blocked || Time.frameCount == menuChangedOnFrame) return false;
            if (touchPressed.Contains(action)) return true;
            if (!action.WasPressedThisFrame()) return false;
            if (!FromMouse(action)) return true;
            if (PointerMode) return !PointerOverUI;
            return CursorLocked && Time.frameCount != lockedOnFrame;
        }

        /// <summary>For menus: true if the button went down this frame (ignores the frame the menu opened).</summary>
        public bool PressedInMenu(InputAction action) =>
            Time.frameCount != menuChangedOnFrame && !Typing && (action.WasPressedThisFrame() || touchPressed.Contains(action));

        /// <summary>True if the action's last input came from a gamepad.</summary>
        public static bool FromGamepad(InputAction action) => action.activeControl?.device is Gamepad;

        /// <summary>True if the action's last input came from the mouse (e.g. the wheel instead of LB/RB).</summary>
        public static bool FromMouse(InputAction action) => action.activeControl?.device is Mouse;

        /// <summary>Mouse look movement in pixels this frame (zero while the cursor is free or controls are blocked).</summary>
        public Vector2 MouseLook =>
            (CursorLocked && !Blocked && (!PrecisionOn || PrecisionLooking) ? LookDelta.ReadValue<Vector2>() : Vector2.zero)
            + (!Blocked && !PrecisionOn ? touchLook : Vector2.zero);

        /// <summary>Precision mode with touch: dragging on the look area moves the piece (like the mouse does).</summary>
        public Vector2 TouchPrecision => !Blocked && PrecisionOn ? touchLook : Vector2.zero;

        /// <summary>
        /// Precision mode + right mouse button held: the mouse turns the camera again and the piece stays put.
        /// </summary>
        public bool PrecisionLooking => PrecisionOn && Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>Mouse movement in pixels this frame while precision mode is on (it moves the piece).</summary>
        public Vector2 PrecisionMouse =>
            CursorLocked && !Blocked && PrecisionOn && !PrecisionLooking ? LookDelta.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>Gamepad right stick while precision mode is on (-1..1).</summary>
        public Vector2 PrecisionStick => !Blocked && PrecisionOn ? LookStick.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>Gamepad look stick (zero while blocked, so the stick can move through a menu).</summary>
        public Vector2 StickLook => Blocked || PrecisionOn ? Vector2.zero : LookStick.ReadValue<Vector2>();

        /// <summary>
        /// Movement (zero while blocked). While Ctrl is held the keyboard doesn't move you, so shortcuts like
        /// Ctrl+D (duplicate) don't also slide the camera sideways.
        /// </summary>
        public Vector2 MoveValue
        {
            get
            {
                if (Blocked) return Vector2.zero;
                bool ctrl = Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;
                if (ctrl && Move.activeControl?.device is Keyboard) return Vector2.zero;
                return Vector2.ClampMagnitude(Move.ReadValue<Vector2>() + TouchMove, 1f);
            }
        }

        /// <summary>True while a Ctrl key is held on the keyboard (single-key shortcuts like E or C then do nothing).</summary>
        public static bool CtrlHeld => Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;

        /// <summary>True while a Shift key is held on the keyboard.</summary>
        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        /// <summary>True while a hold-type control (up/down/fast) is held and controls are not blocked.</summary>
        public bool Held(InputAction action) => !Blocked && (action.IsPressed() || touchHeld.Contains(action));
    }
}
