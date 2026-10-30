using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>
    /// One undoable change to a group: create it, delete it, or change it (rename, pivot, show/hide),
    /// plus any pieces that move into or out of it at the same time.
    /// <c>before</c> = null means "create", <c>after</c> = null means "delete".
    /// </summary>
    public class GroupCommand : ICommand
    {
        readonly Group before, after;
        // Pieces whose group changes: (piece id, group id before, group id after).
        readonly List<(string piece, string from, string to)> moves = new List<(string, string, string)>();

        public string Label { get; }

        public GroupCommand(Group before, Group after, string label, IEnumerable<(string piece, string from, string to)> pieceMoves = null)
        {
            this.before = before?.Clone();
            this.after = after?.Clone();
            if (pieceMoves != null) moves.AddRange(pieceMoves);
            Label = label;
        }

        public ChangeSet Do(BuildContent content) => Apply(content, before, after, forward: true);
        public ChangeSet Undo(BuildContent content) => Apply(content, after, before, forward: false);

        ChangeSet Apply(BuildContent content, Group from, Group to, bool forward)
        {
            var changes = new ChangeSet();
            // (Moving pieces only: the same group before and after, or no group at all when ungrouping.)
            string id = (to ?? from)?.id;
            if (id != null)
            {
                changes.groupIds.Add(id);
                int index = content.groups.FindIndex(g => g.id == id);
                if (to == null) { if (index >= 0) content.groups.RemoveAt(index); }
                else if (index >= 0) content.groups[index] = to.Clone();
                else content.groups.Add(to.Clone());
            }

            foreach (var (pieceId, fromGroup, toGroup) in moves)
            {
                var piece = content.FindPiece(pieceId);
                if (piece == null) continue;
                piece.groupId = forward ? toGroup : fromGroup;
                changes.pieceIds.Add(pieceId);
            }
            return changes;
        }
    }
}
