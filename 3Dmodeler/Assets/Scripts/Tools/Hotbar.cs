using System;
using Builder.Input;
using Builder.Shapes;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// The 9-slot hotbar, Minecraft style. Keys 1-9 pick a slot, gamepad LB / RB step through them.
    /// The mouse wheel steps through them after the middle mouse button is pressed (wheel "item" mode).
    /// The chosen slot's shape is what the build tool places. The slots are saved in the project (EditorState).
    /// </summary>
    public class Hotbar : MonoBehaviour
    {
        public const int SlotCount = 9;

        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] BuildTool buildTool;
        [SerializeField] ToolManager tools;
        [SerializeField] IO.BlueprintLibrary library;

        /// <summary>Raised when the selected slot or a slot's content changes (the HUD redraws).</summary>
        public event Action Changed;

        Core.EditorState Editor => session.Document.editor;

        public int Selected => Editor.hotbarSlot;

        /// <summary>What is in slot <paramref name="index"/> (a shape id), or null if empty.</summary>
        public string ItemAt(int index) => Editor.hotbar[index];

        void Start()
        {
            session.Replaced += FillAndApply;   // another project was opened: use its hotbar
            library.Changed += Apply;           // a blueprint was added / changed / deleted
            FillAndApply();
        }

        void OnDestroy()
        {
            if (session != null) session.Replaced -= FillAndApply;
            if (library != null) library.Changed -= Apply;
        }

        void FillAndApply()
        {
            // Fill empty slots with the shapes in inventory order.
            for (int i = 0; i < SlotCount; i++)
                if (string.IsNullOrEmpty(Editor.hotbar[i]) && i < ShapeRegistry.All.Count)
                    Editor.hotbar[i] = ShapeRegistry.All[i].Id;
            Apply();
        }

        void Update()
        {
            if (tools.Current == ToolKind.Paint) return;   // while painting, 1-9 and LB/RB choose colors

            for (int i = 0; i < SlotCount; i++)
                if (input.Pressed(input.Slots[i])) Select(i);

            if (input.Pressed(input.HotbarNext)) Select((Selected + 1) % SlotCount);
            if (input.Pressed(input.HotbarPrev)) Select((Selected + SlotCount - 1) % SlotCount);

            // Wheel in item mode (middle mouse button): wheel down = next slot, wheel up = previous.
            bool wheelPicks = buildTool.enabled && buildTool.Wheel == WheelMode.Item && !buildTool.WheelZooms && !input.PrecisionOn
                              && !BuilderInput.CtrlHeld;   // Ctrl + wheel = how far the item floats
            if (wheelPicks && input.Pressed(input.TurnMinus) && BuilderInput.FromMouse(input.TurnMinus))
                Select((Selected + 1) % SlotCount);
            if (wheelPicks && input.Pressed(input.TurnPlus) && BuilderInput.FromMouse(input.TurnPlus))
                Select((Selected + SlotCount - 1) % SlotCount);
        }

        public void Select(int index)
        {
            Editor.hotbarSlot = Mathf.Clamp(index, 0, SlotCount - 1);
            Apply();
        }

        /// <summary>Puts a shape into a slot (from the inventory).</summary>
        public void Assign(int index, string shapeId)
        {
            Editor.hotbar[index] = shapeId;
            Apply();
        }

        void Apply()
        {
            buildTool.SetShape(ItemAt(Selected));
            Changed?.Invoke();
        }
    }
}
