using System.Collections.Generic;
using System.Linq;

namespace Builder.Core
{
    /// <summary>
    /// A command that can swallow the next command of the same kind, so a whole slider drag or gizmo drag
    /// becomes ONE undo step instead of hundreds.
    /// </summary>
    public interface IMergeableCommand : ICommand
    {
        /// <summary>Takes over <paramref name="next"/>'s result if it continues this edit. Returns true if merged.</summary>
        bool TryMerge(ICommand next);
    }

    /// <summary>
    /// Changes existing pieces (move, rotate, resize, edit settings). Stores full copies of the pieces
    /// before and after, so it works for any kind of change.
    /// </summary>
    public class ModifyPiecesCommand : IMergeableCommand
    {
        readonly List<Piece> before;
        List<Piece> after;
        readonly string mergeKey;

        public string Label { get; }

        /// <param name="mergeKey">Edits with the same key (e.g. "edit:width") in a row merge into one undo step.
        /// Null = never merge.</param>
        public ModifyPiecesCommand(IEnumerable<Piece> before, IEnumerable<Piece> after, string label, string mergeKey = null)
        {
            this.before = before.Select(p => p.Clone()).ToList();
            this.after = after.Select(p => p.Clone()).ToList();
            this.mergeKey = mergeKey;
            Label = label;
        }

        public ChangeSet Do(BuildContent content) => Apply(content, after);
        public ChangeSet Undo(BuildContent content) => Apply(content, before);

        static ChangeSet Apply(BuildContent content, List<Piece> pieces)
        {
            foreach (var p in pieces)
            {
                int i = content.pieces.FindIndex(x => x.id == p.id);
                if (i >= 0) content.pieces[i] = p.Clone();
            }
            return ChangeSet.ForPieces(pieces.Select(p => p.id));
        }

        public bool TryMerge(ICommand next)
        {
            if (mergeKey == null || !(next is ModifyPiecesCommand m) || m.mergeKey != mergeKey) return false;
            // Only the same pieces: a drag on a different selection is a new step.
            if (!new HashSet<string>(after.Select(p => p.id)).SetEquals(m.after.Select(p => p.id))) return false;
            after = m.after.Select(p => p.Clone()).ToList();
            return true;
        }
    }
}
