using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class BoardViewTests
    {
        GameObject gameObject;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(gameObject);

        [Test]
        public void Show_MatchesBoardBits_AndMarksTargetGhosts()
        {
            var board = new Board(TestLevels.Heart());
            gameObject = new GameObject("Board");
            var view = gameObject.AddComponent<BoardView>();
            view.Build(board.Rules);
            view.Show(board.Bits);

            Assert.That(gameObject.transform.Find("Cells").childCount, Is.EqualTo(64));
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
        public void LeanScale_StretchesAlongTheMove_ThenRests_AndStaysInsideItsCell()
        {
            Vector3 rowLean = BoardView.LeanScale(0.07f, Axis.Row);
            Assert.That(rowLean.x, Is.GreaterThan(1f), "stretched along a row move");
            Assert.That(rowLean.y, Is.LessThan(1f));

            Vector3 columnLean = BoardView.LeanScale(0.07f, Axis.Column);
            Assert.That(columnLean.y, Is.GreaterThan(1f), "stretched along a column move");
            Assert.That(columnLean.x, Is.LessThan(1f));

            Assert.That(BoardView.LeanScale(0f, Axis.Row), Is.EqualTo(Vector3.one));
            Assert.That(BoardView.LeanScale(1f, Axis.Row), Is.EqualTo(Vector3.one));

            for (float time = 0f; time < 0.3f; time += 0.005f)
            {
                Assert.That(BoardView.LeanScale(time, Axis.Row).x * 46f, Is.LessThan(53f), $"at {time}");
            }
        }

        [Test]
        public void GlanceDirection_LooksAtTheMovedPart_OnlyFromRightBesideIt()
        {
            ulong rowFour = 0;
            for (int column = 0; column < 8; column++)
            {
                rowFour |= BoardBits.CellMask(4, column);
            }

            Assert.That(BoardView.GlanceDirection(rowFour, Axis.Row, 3, 2), Is.EqualTo(Vector2.down), "row above");
            Assert.That(BoardView.GlanceDirection(rowFour, Axis.Row, 5, 2), Is.EqualTo(Vector2.up), "row below");
            Assert.That(BoardView.GlanceDirection(rowFour, Axis.Row, 4, 2), Is.EqualTo(Vector2.zero), "moved itself");
            Assert.That(BoardView.GlanceDirection(rowFour, Axis.Row, 2, 2), Is.EqualTo(Vector2.zero), "too far away");

            ulong columnSeven = 0;
            for (int row = 0; row < 8; row++)
            {
                columnSeven |= BoardBits.CellMask(row, 7);
            }

            Assert.That(BoardView.GlanceDirection(columnSeven, Axis.Column, 3, 6), Is.EqualTo(Vector2.right));
            Assert.That(BoardView.GlanceDirection(columnSeven, Axis.Column, 3, 5), Is.EqualTo(Vector2.zero));

            ulong partOfRow = BoardBits.CellMask(4, 0) | BoardBits.CellMask(4, 1);
            Assert.That(BoardView.GlanceDirection(partOfRow, Axis.Row, 3, 1), Is.EqualTo(Vector2.down));
            Assert.That(BoardView.GlanceDirection(partOfRow, Axis.Row, 3, 5), Is.EqualTo(Vector2.zero),
                "a wall splits the row: the other part did not move");
        }

        BoardView BuildHeart(out Board board)
        {
            board = new Board(TestLevels.Heart());
            gameObject = new GameObject("Board");
            var view = gameObject.AddComponent<BoardView>();
            view.Build(board.Rules);
            view.Show(board.Bits);
            return view;
        }

        static void Run(BoardView view, float seconds, System.Action afterEachTick = null)
        {
            const float frame = 0.02f;
            for (float time = 0f; time < seconds; time += frame)
            {
                view.TickFaces(frame);
                afterEachTick?.Invoke();
            }
        }

        static int CountCells(System.Func<int, int, bool> test)
        {
            int count = 0;
            for (int row = 0; row < 8; row++)
            {
                for (int column = 0; column < 8; column++)
                {
                    if (test(row, column))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        [Test]
        public void IdleLife_BlinksOneBitAtATime_BeforeAnyoneNaps()
        {
            var view = BuildHeart(out _);
            view.IdleLife = true;
            int most = 0, blinkFrames = 0;
            Run(view, 9.5f, () =>
            {
                int closing = CountCells((row, column) => view.EyeLid(row, column) > 0f);
                most = Mathf.Max(most, closing);
                blinkFrames += closing;
            });

            Assert.That(blinkFrames, Is.GreaterThan(0), "somebody blinked");
            Assert.That(most, Is.EqualTo(1), "never two at once");
            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(0), "too early for a nap");
        }

        [Test]
        public void Nap_ClosesEyesOneAtATime_OnlyOnBits_AndInputWakesThem()
        {
            var view = BuildHeart(out var board);
            int bits = CountCells(board.GetCell);
            view.IdleLife = true;
            Run(view, 10.2f);
            int early = CountCells(view.IsBitAsleep);
            Assert.That(early, Is.InRange(1, 2), "they drop off one by one");

            Run(view, 0.35f * bits);
            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(bits), "all asleep, and only real bits");
            Assert.That(CountCells((row, column) => view.EyeLid(row, column) == 1f), Is.EqualTo(bits));

            view.WakeUp();
            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(0));
            Run(view, 0.5f);
            Assert.That(CountCells((row, column) => view.EyeLid(row, column) > 0f), Is.LessThanOrEqualTo(1),
                "eyes open again, apart from a blink");

            Run(view, 5f);
            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(0), "the idle clock started over");
        }

        [Test]
        public void IdleLife_Off_KeepsEveryEyeOpen()
        {
            var view = BuildHeart(out _);
            view.IdleLife = true;
            view.IdleLife = false;
            int closed = 0;
            Run(view, 15f, () => closed += CountCells((row, column) => view.EyeLid(row, column) > 0f));
            Assert.That(closed, Is.EqualTo(0));
        }

        [Test]
        public void Nap_IsHeldOnASolvedBoard_UntilTheBoardChanges()
        {
            var view = BuildHeart(out var board);
            view.IdleLife = true;
            board.TryMove(Move.Row(4, Direction.Left), out var outcome);
            view.Play(outcome, true, false);
            Run(view, 15f);
            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(0));

            view.Show(board.Bits);
            Run(view, 11f);
            Assert.That(CountCells(view.IsBitAsleep), Is.GreaterThan(0));
        }

        [Test]
        public void CellCenter_RowZeroIsTop_PitchMatchesMockup()
        {
            Assert.That(BoardView.CellCenter(0, 0).y, Is.GreaterThan(BoardView.CellCenter(7, 0).y));
            Assert.That(BoardView.CellCenter(0, 1).x - BoardView.CellCenter(0, 0).x, Is.EqualTo(53f / 46f).Within(1e-5),
                "46 px cells, 7 px gaps (layout A scales the board to 52 px cells)");
        }
    }
}
