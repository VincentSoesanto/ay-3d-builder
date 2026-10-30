using System;
using Builder.Input;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>An on-screen button: while a finger is on it, its action counts as pressed / held.</summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public BuilderInput input;
        public InputAction action;
        Image image;
        Color normal;

        void Awake()
        {
            image = GetComponent<Image>();
            if (image != null) normal = image.color;
        }

        public void OnPointerDown(PointerEventData e)
        {
            input.TouchDown(action);
            if (image != null) image.color = UiKit.Accent * new Color(1f, 1f, 1f, 0.7f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            input.TouchUp(action);
            if (image != null) image.color = normal;
        }

        void OnDisable() { if (input != null && action != null) input.TouchUp(action); }
    }

    /// <summary>The on-screen move stick: drag the knob; how far = how fast.</summary>
    public class TouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public BuilderInput input;
        public RectTransform knob;
        public float radius = 120f;

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var v = Vector2.ClampMagnitude(local / radius, 1f);
            knob.anchoredPosition = v * radius;
            input.TouchMove = v;
        }

        public void OnPointerUp(PointerEventData e)
        {
            knob.anchoredPosition = Vector2.zero;
            input.TouchMove = Vector2.zero;
        }

        void OnDisable() { if (input != null) input.TouchMove = Vector2.zero; }
    }

    /// <summary>The look area (right part of the screen): drag a finger to look around (in precision mode: to move the piece).</summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler
    {
        public BuilderInput input;
        public float sensitivity = 0.35f;

        public void OnDrag(PointerEventData e) => input.AddTouchLook(e.delta * sensitivity);
    }

    /// <summary>
    /// Touch controls for phones and tablets (Phase 5). Shown on mobile devices (or on a PC started with "-touch").
    ///   - Left: move stick.  Right side: drag to look around.
    ///   - PLACE (tap; hold to spray paint), DEL, UP / DOWN, AXIS / TURN &lt; / TURN &gt; / SIZE (rotate and scale the ghost),
    ///     TOOL / PAINT / ERASE / MIRROR / MODE / GRID, and UNDO / REDO / BAG (inventory) / MENU.
    ///   - Tap a hotbar slot to choose it.
    /// Every button "presses" the same action as its key, so all tools work the same as with a keyboard or gamepad.
    /// A keyboard, mouse or gamepad still works next to it.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        [SerializeField] BuilderInput input;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform canvasRoot;
        [SerializeField] RectTransform hotbarRow;

        GameObject root;

        public static bool Wanted =>
            Application.isMobilePlatform || Array.IndexOf(Environment.GetCommandLineArgs(), "-touch") >= 0;

        void Start()
        {
            if (!Wanted) return;
            Build();
        }

        void Update()
        {
            if (root == null) return;
            bool show = !input.MenuOpen;   // menus have their own buttons
            if (root.activeSelf != show) root.SetActive(show);
            HookHotbar();
        }

        /// <summary>The hotbar slots become tappable (they are made by the hotbar view, maybe after this started).</summary>
        void HookHotbar()
        {
            if (hotbarRow == null || hotbarRow.childCount == 0 || hotbarRow.GetChild(0).GetComponent<TouchButton>() != null) return;
            for (int i = 0; i < hotbarRow.childCount && i < input.Slots.Length; i++)
            {
                var slot = hotbarRow.GetChild(i).GetComponent<Image>();
                if (slot == null) continue;
                slot.raycastTarget = true;
                var b = slot.gameObject.AddComponent<TouchButton>();
                b.input = input;
                b.action = input.Slots[i];
            }
        }

        void Build()
        {
            var overlay = UiKit.Rect(canvasRoot, "Touch Controls");
            UiKit.Fill(overlay);
            overlay.SetAsFirstSibling();   // under every panel and menu
            root = overlay.gameObject;

            // Look area: the right 60 % of the screen (buttons on top of it take their own touches).
            var look = UiKit.Rect(overlay, "Look Area");
            look.anchorMin = new Vector2(0.4f, 0f);
            look.anchorMax = Vector2.one;
            look.offsetMin = look.offsetMax = Vector2.zero;
            var lookImage = look.gameObject.AddComponent<Image>();
            lookImage.color = new Color(0f, 0f, 0f, 0f);
            look.gameObject.AddComponent<TouchLookArea>().input = input;

            // Move stick, bottom left.
            var stickBase = UiKit.Box(overlay, "Move Stick", theme, new Color(1f, 1f, 1f, 0.12f), clickable: true);
            UiKit.Pin(stickBase.rectTransform, new Vector2(0f, 0f), new Vector2(90f, 110f), new Vector2(280f, 280f));
            var knob = UiKit.Box(stickBase.transform, "Knob", theme, new Color(1f, 1f, 1f, 0.35f));
            knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            knob.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            knob.rectTransform.sizeDelta = new Vector2(110f, 110f);
            var stick = stickBase.gameObject.AddComponent<TouchStick>();
            stick.input = input;
            stick.knob = knob.rectTransform;
            stick.radius = 120f;
            var moveLabel = UiKit.Label(stickBase.transform, "Label", theme, 16f, UiKit.SubtleText, TextAlignmentOptions.Top, "MOVE");
            UiKit.Fill(moveLabel.rectTransform, 10f);

            // Bottom right: place / delete / up / down.
            Button(overlay, "PLACE", input.Place, new Vector2(1f, 0f), new Vector2(-60f, 80f), 180f, 24f);
            Button(overlay, "DEL", input.Delete, new Vector2(1f, 0f), new Vector2(-270f, 60f), 110f);
            Button(overlay, "UP", input.Up, new Vector2(1f, 0f), new Vector2(-40f, 420f), 100f);
            Button(overlay, "DOWN", input.Down, new Vector2(1f, 0f), new Vector2(-40f, 300f), 100f);

            // Turning and size of the ghost.
            Button(overlay, "AXIS", input.RotateAxis, new Vector2(1f, 0f), new Vector2(-270f, 200f), 100f);
            Button(overlay, "TURN <", input.TurnPlus, new Vector2(1f, 0f), new Vector2(-380f, 200f), 100f);
            Button(overlay, "TURN >", input.TurnMinus, new Vector2(1f, 0f), new Vector2(-160f, 300f), 100f);
            Button(overlay, "SIZE", input.ScaleMode, new Vector2(1f, 0f), new Vector2(-160f, 420f), 100f);

            // Tools and modes, top right.
            string[] names = { "TOOL", "PAINT", "ERASE", "MIRROR", "MODE", "GRID" };
            InputAction[] actions = { input.ToggleTool, input.PaintTool, input.EraseTool, input.Mirror, input.SnapToggle, input.GridSize };
            for (int i = 0; i < names.Length; i++)
                Button(overlay, names[i], actions[i], new Vector2(1f, 1f), new Vector2(-40f - (i % 2) * 110f, -150f - (i / 2) * 105f), 95f, 16f);

            // Top row: undo, redo, inventory, menu.
            string[] top = { "MENU", "BAG", "REDO", "UNDO" };
            InputAction[] topActions = { input.Menu, input.Inventory, input.Redo, input.Undo };
            for (int i = 0; i < top.Length; i++)
                Button(overlay, top[i], topActions[i], new Vector2(1f, 1f), new Vector2(-40f - i * 110f, -30f), 95f, 16f, height: 80f);
        }

        void Button(RectTransform parent, string label, InputAction action, Vector2 anchor, Vector2 position, float size,
                    float fontSize = 18f, float height = -1f)
        {
            var box = UiKit.Box(parent, label, theme, new Color(0.1f, 0.11f, 0.14f, 0.55f), clickable: true);
            UiKit.Pin(box.rectTransform, anchor, position, new Vector2(size, height > 0f ? height : size));
            var text = UiKit.Label(box.transform, "Label", theme, fontSize, UiKit.Text, TextAlignmentOptions.Center, "<b>" + label + "</b>");
            UiKit.Fill(text.rectTransform);
            var b = box.gameObject.AddComponent<TouchButton>();
            b.input = input;
            b.action = action;
        }
    }
}
