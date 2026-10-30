using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>
    /// One undoable edit. Every change to the model goes through a command, so undo/redo always works.
    /// Do and Undo return which pieces they touched, so the View only rebuilds those.
    /// </summary>
    public interface ICommand
    {
        /// <summary>Short name for the undo history, e.g. "Place box".</summary>
        string Label { get; }

        ChangeSet Do(BuildContent content);
        ChangeSet Undo(BuildContent content);
    }

    /// <summary>The ids of everything a command added, removed or changed (pieces, groups and bones).</summary>
    public class ChangeSet
    {
        public readonly HashSet<string> pieceIds = new HashSet<string>();
        public readonly HashSet<string> groupIds = new HashSet<string>();
        public readonly HashSet<string> boneIds = new HashSet<string>();

        public static ChangeSet ForPieces(IEnumerable<string> ids)
        {
            var c = new ChangeSet();
            c.pieceIds.UnionWith(ids);
            return c;
        }
    }
}
