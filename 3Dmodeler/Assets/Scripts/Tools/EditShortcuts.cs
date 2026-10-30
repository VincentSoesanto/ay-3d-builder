using Builder.Core;
using Builder.Input;
using Builder.UI;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Shortcuts that work with every tool:
    ///   undo Ctrl+Z, redo Ctrl+Y / Ctrl+Shift+Z, copy Ctrl+C, paste Ctrl+V, duplicate Ctrl+D, mirror M,
    ///   grid size G / D-pad up, placement mode GRID -> SNAP -> FREE  F / D-pad down.
    /// </summary>
    public class EditShortcuts : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] ToolManager tools;
        [SerializeField] Selection selection;
        [SerializeField] Clipboard clipboard;
        [SerializeField] BuildTool buildTool;

        void Update()
        {
            var history = session.History;
            var editor = session.Document.editor;

            // Redo first: Ctrl+Shift+Z also looks like Ctrl+Z.
            if (input.Pressed(input.Redo))
            {
                string label = history.RedoLabel;
                Toast.Show(history.Redo() ? "Redo: " + label : "Nothing to redo");
            }
            else if (input.Pressed(input.Undo) && !BuilderInput.ShiftHeld)
            {
                string label = history.UndoLabel;
                Toast.Show(history.Undo() ? "Undo: " + label : "Nothing to undo");
            }

            if (input.Pressed(input.Copy)) CopySelection();
            if (input.Pressed(input.Paste)) StartPaste();
            if (input.Pressed(input.Duplicate) && CopySelection()) StartPaste();

            if (input.Pressed(input.Mirror))
            {
                editor.mirrorX = !editor.mirrorX;
                Toast.Show(editor.mirrorX ? "Mirror ON  (left = right across the red line)" : "Mirror OFF");
            }

            // Gamepad D-pad: in SNAP mode "up" picks the attach point (BuildTool); in the Select tool the D-pad
            // moves through the edit panel (EditPanel); in the Eraser "up" switches overlap/inside (EraseTool).
            // Only otherwise does it change grid size / mode.
            bool padUpTaken = editor.snapMode == SnapMode.Snap || tools.Current == ToolKind.Select || tools.Current == ToolKind.Erase;
            if (input.Pressed(input.GridSize) && !(padUpTaken && BuilderInput.FromGamepad(input.GridSize)))
                editor.gridSize = PlacementMath.NextGridSize(editor.gridSize);
            bool padDownTaken = tools.Current == ToolKind.Select;
            if (input.Pressed(input.SnapToggle) && !(padDownTaken && BuilderInput.FromGamepad(input.SnapToggle)))
                editor.snapMode = (SnapMode)(((int)editor.snapMode + 1) % 3);
        }

        bool CopySelection()
        {
            var bounds = selection.Bounds;
            if (selection.Count == 0 || bounds == null)
            {
                Toast.Show("Select pieces first (I = Select tool)");
                return false;
            }
            clipboard.Copy(selection.Pieces(), bounds.Value, session.Document.content);
            Toast.Show(clipboard.Count == 1 ? "Copied 1 piece" : $"Copied {clipboard.Count} pieces");
            return true;
        }

        void StartPaste()
        {
            if (clipboard.Count == 0)
            {
                Toast.Show("Nothing copied yet (Ctrl+C)");
                return;
            }
            tools.SetTool(ToolKind.Build, quiet: true);
            buildTool.StartPaste(clipboard.Parts);
            Toast.Show("Pasting - click to place, right click to stop");
        }
    }
}
