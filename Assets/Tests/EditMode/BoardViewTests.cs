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
            for (int row = 0; row < 8; row++)
            {
                for (int column = 0; column < 8; column++)
                {
                    bool on = board.GetCell(row, column);
                    bool want = BoardBits.GetCell(board.Rules.Target, row, column);
                    Assert.That(view.IsBitShown(row, column), Is.EqualTo(on), $"bit {row},{column}");
                    Assert.That(view.IsGhostShown(row, column), Is.EqualTo(!on && want), $"ghost {row},{column}");
                }
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
            Assert.That(BoardView.CellCenter(0, 1).x - BoardView.CellCenter(0, 0).x, Is.EqualTo(53f / 46f).Within(1e-5), "46 px cells, 7 px gaps (layout A scales the board to 52 px cells)");
        }
    }
}
