using Builder.Core;
using NUnit.Framework;

namespace Builder.Tests
{
    public class UndoStackTests
    {
        static Piece Box(string id) => new Piece { id = id, shape = "box" };

        [Test]
        public void Place_Undo_Redo()
        {
            var content = new BuildContent();
            var stack = new UndoStack(content);

            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));
            Assert.AreEqual(1, content.pieces.Count);

            Assert.IsTrue(stack.Undo());
            Assert.AreEqual(0, content.pieces.Count);

            Assert.IsTrue(stack.Redo());
            Assert.AreEqual("a", content.pieces[0].id);
        }

        [Test]
        public void Delete_Undo_PutsPieceBackInSamePlace()
        {
            var content = new BuildContent();
            var stack = new UndoStack(content);
            stack.Execute(new AddPiecesCommand(new[] { Box("a"), Box("b"), Box("c") }));

            stack.Execute(new RemovePiecesCommand(new[] { "b" }));
            CollectionAssert.AreEqual(new[] { "a", "c" }, content.pieces.ConvertAll(p => p.id));

            stack.Undo();
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, content.pieces.ConvertAll(p => p.id));
        }

        [Test]
        public void NewEdit_ClearsRedo()
        {
            var content = new BuildContent();
            var stack = new UndoStack(content);
            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));
            stack.Undo();
            stack.Execute(new AddPiecesCommand(new[] { Box("b") }));
            Assert.IsFalse(stack.CanRedo);
        }

        [Test]
        public void Changed_ReportsTouchedPieces()
        {
            var content = new BuildContent();
            var stack = new UndoStack(content);
            ChangeSet last = null;
            stack.Changed += c => last = c;

            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));
            CollectionAssert.AreEquivalent(new[] { "a" }, last.pieceIds);
        }

        [Test]
        public void History_IsNotChangedByEditingLivePieces()
        {
            var content = new BuildContent();
            var stack = new UndoStack(content);
            stack.Execute(new AddPiecesCommand(new[] { Box("a") }));

            content.pieces[0].color = "#FF0000";   // someone edits the live piece directly (should never happen, but be safe)
            stack.Undo();
            stack.Redo();
            Assert.AreEqual("#B0B0B0", content.pieces[0].color);
        }
    }
}
