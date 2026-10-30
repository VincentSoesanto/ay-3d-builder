using System;
using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>
    /// Runs commands on a BuildContent and remembers them for undo/redo.
    /// Anyone who shows the model (the View) listens to <see cref="Changed"/>.
    ///
    /// Merging: while an edit is "open" (e.g. a slider is being dragged), commands of the same kind merge into
    /// the last undo step. <see cref="SealMerge"/> closes it (called when the mouse button is released).
    /// </summary>
    public class UndoStack
    {
        public const int DefaultLimit = 200;

        readonly BuildContent content;
        readonly List<ICommand> undoList = new List<ICommand>();
        readonly List<ICommand> redoList = new List<ICommand>();
        readonly int limit;
        bool mergeOpen;

        /// <summary>Raised after every Execute, Undo and Redo, with what changed.</summary>
        public event Action<ChangeSet> Changed;

        public UndoStack(BuildContent content, int limit = DefaultLimit)
        {
            this.content = content;
            this.limit = limit;
        }

        public bool CanUndo => undoList.Count > 0;
        public bool CanRedo => redoList.Count > 0;

        /// <summary>Name of the step Undo would take back (for "Undo: Place box" messages), or null.</summary>
        public string UndoLabel => CanUndo ? undoList[undoList.Count - 1].Label : null;

        /// <summary>Name of the step Redo would do again, or null.</summary>
        public string RedoLabel => CanRedo ? redoList[redoList.Count - 1].Label : null;

        public void Execute(ICommand command)
        {
            // Continue the open edit (same slider / same drag)?
            if (mergeOpen && undoList.Count > 0 && undoList[undoList.Count - 1] is IMergeableCommand last
                && last.TryMerge(command))
            {
                var merged = command.Do(content);
                redoList.Clear();
                Changed?.Invoke(merged);
                return;
            }

            var changes = command.Do(content);
            undoList.Add(command);
            if (undoList.Count > limit) undoList.RemoveAt(0);   // forget the oldest step
            redoList.Clear();                                   // a new edit makes the old "future" invalid
            mergeOpen = command is IMergeableCommand;
            Changed?.Invoke(changes);
        }

        /// <summary>Ends the open edit: the next command starts a new undo step.</summary>
        public void SealMerge() => mergeOpen = false;

        public bool Undo()
        {
            if (!CanUndo) return false;
            mergeOpen = false;
            var command = undoList[undoList.Count - 1];
            undoList.RemoveAt(undoList.Count - 1);
            var changes = command.Undo(content);
            redoList.Add(command);
            Changed?.Invoke(changes);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            mergeOpen = false;
            var command = redoList[redoList.Count - 1];
            redoList.RemoveAt(redoList.Count - 1);
            var changes = command.Do(content);
            undoList.Add(command);
            Changed?.Invoke(changes);
            return true;
        }
    }
}
