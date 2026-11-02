using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class BoardViewTests
    {
        GameObject go;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        [Test]
        public void Show_MatchesBoardBits_AndMarksTargetGhosts()
        {
            var board = new Board(TestLevels.Heart());
            go = new GameObject("Board");
            var view = go.AddComponent<BoardView>();
            view.Build(board.Rules);
            view.Show(board.Bits);

            Assert.That(go.transform.Find("Cells").childCount, Is.EqualTo(64));
            for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                bool on = board.GetCell(r, c);
                bool want = BoardBits.GetCell(board.Rules.Target, r, c);
                Assert.That(view.IsBitShown(r, c), Is.EqualTo(on), $"bit {r},{c}");
                Assert.That(view.IsGhostShown(r, c), Is.EqualTo(!on && want), $"ghost {r},{c}");
            }

            board.TryMove(Move.Row(4, Direction.Left), out _);
            view.Show(board.Bits);
            Assert.That(view.IsBitShown(4, 1), Is.True, "row 5 is now 126 = 01111110");
            Assert.That(view.IsBitShown(4, 7), Is.False);
        }

        [Test]
        public void CellCenter_RowZeroIsTop_PitchMatchesMockup()
        {
            Assert.That(BoardView.CellCenter(0, 0).y, Is.GreaterThan(BoardView.CellCenter(7, 0).y));
            Assert.That(BoardView.CellCenter(0, 1).x - BoardView.CellCenter(0, 0).x, Is.EqualTo(51f / 46f).Within(1e-5));
        }
    }
}
