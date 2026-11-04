using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class BoardTests
    {
        [Test]
        public void Domino_ObviousMove_BreaksRule_UndoRecovers()
        {
            var board = new Board(TestLevels.Domino());
            Assert.That(board.TryMove(Move.Column(3, Direction.Up), out var o), Is.True);
            Assert.That(o.LostCount, Is.EqualTo(1));
            Assert.That(board.IsRuleBroken, Is.True);
            Assert.That(board.TryMove(Move.Row(7, Direction.Left), out _), Is.False, "no moves while a rule is broken");

            Assert.That(board.Undo(), Is.True);
            Assert.That(board.IsRuleBroken, Is.False);
            Assert.That(board.Lost, Is.Zero);
            Assert.That(board.MoveCount, Is.Zero);
        }

        [Test]
        public void Domino_DocSolution_Solves()
        {
            var board = new Board(TestLevels.Domino());
            var solution = new[]
            {
                Move.Row(7, Direction.Left),
                Move.Column(7, Direction.Down),
                Move.Row(0, Direction.Right),
                Move.Row(0, Direction.Right),
                Move.Row(0, Direction.Right),
                Move.Column(3, Direction.Up)
            };
            foreach (var m in solution)
            {
                Assert.That(board.TryMove(m, out _), Is.True, $"{m}\n{BoardBits.ToPicture(board.Bits)}");
            }

            Assert.That(board.IsSolved, Is.True, BoardBits.ToPicture(board.Bits));
            Assert.That(board.Lost, Is.Zero);
            Assert.That(board.MoveCount, Is.EqualTo(6));
            Assert.That(board.TryMove(Move.Row(1, Direction.Left), out _), Is.False, "no moves after solving");
        }

        [Test]
        public void Reset_RestoresStart()
        {
            var board = new Board(TestLevels.Heart());
            board.TryMove(Move.Row(4, Direction.Left), out _);
            board.TryMove(Move.Row(6, Direction.Right), out _);
            board.Reset();
            Assert.That(board.Bits, Is.EqualTo(board.Rules.Start));
            Assert.That(board.Lost, Is.Zero);
            Assert.That(board.CanUndo, Is.False);
        }

        [Test]
        public void ExactlyRule_RequiresExactBucketCount()
        {
            var d = TestLevels.OneRow("11110000");
            d.target[3] = TestLevels.Bits("11000000");
            d.lossRule = LossRule.Exactly;
            d.lossCount = 2;
            var board = new Board(d);
            board.TryMove(Move.Row(3, Direction.Left), out _);
            board.TryMove(Move.Row(3, Direction.Left), out _);
            Assert.That(board.IsSolved, Is.True);
            Assert.That(board.Lost, Is.EqualTo(2));
        }
    }
}
