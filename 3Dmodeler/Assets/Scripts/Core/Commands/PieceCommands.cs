using System.Collections.Generic;
using System.Linq;

namespace Builder.Core
{
    /// <summary>Adds one or more pieces (place, paste, mirror copy...).</summary>
    public class AddPiecesCommand : ICommand
    {
        // We keep our own copies, so later edits to the live pieces can't change the history.
        readonly List<Piece> pieces;

        public string Label { get; }

        public AddPiecesCommand(IEnumerable<Piece> pieces, string label = "Place")
        {
            this.pieces = pieces.Select(p => p.Clone()).ToList();
            Label = label;
        }

        public ChangeSet Do(BuildContent content)
        {
            foreach (var p in pieces) content.pieces.Add(p.Clone());
            return ChangeSet.ForPieces(pieces.Select(p => p.id));
        }

        public ChangeSet Undo(BuildContent content)
        {
            var ids = new HashSet<string>(pieces.Select(p => p.id));
            content.pieces.RemoveAll(p => ids.Contains(p.id));
            return ChangeSet.ForPieces(ids);
        }
    }

    /// <summary>Removes pieces by id. Undo puts them back in the same place in the list.</summary>
    public class RemovePiecesCommand : ICommand
    {
        readonly HashSet<string> ids;
        // Filled in by Do: each removed piece and the list index it had.
        readonly List<(int index, Piece piece)> removed = new List<(int, Piece)>();

        public string Label { get; }

        public RemovePiecesCommand(IEnumerable<string> ids, string label = "Delete")
        {
            this.ids = new HashSet<string>(ids);
            Label = label;
        }

        public ChangeSet Do(BuildContent content)
        {
            removed.Clear();
            for (int i = 0; i < content.pieces.Count; i++)
                if (ids.Contains(content.pieces[i].id))
                    removed.Add((i, content.pieces[i].Clone()));
            content.pieces.RemoveAll(p => ids.Contains(p.id));
            return ChangeSet.ForPieces(ids);
        }

        public ChangeSet Undo(BuildContent content)
        {
            // Insert from lowest index up, so every piece lands at its original position.
            foreach (var (index, piece) in removed)
                content.pieces.Insert(index, piece.Clone());
            return ChangeSet.ForPieces(ids);
        }
    }
}
