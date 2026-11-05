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
        public void HintWobble_SwingsBothWays_InsideTheGapBetweenCells()
        {
            Assert.That(BoardView.HintWobble(0f, 0f), Is.Zero);
            Assert.That(BoardView.HintWobble(0.05f, 0.1f), Is.Zero, "not its turn yet");

            float least = 0f, most = 0f;
            for (float time = 0f; time < 3f; time += 0.01f)
            {
                float offset = BoardView.HintWobble(time, 0f);
                least = Mathf.Min(least, offset);
                most = Mathf.Max(most, offset);
            }

            Assert.That(most, Is.EqualTo(4f).Within(0.01f));
            Assert.That(least, Is.EqualTo(-1f).Within(0.01f));
            Assert.That(BoardView.HintScale(4f, Axis.Row).x * 46f, Is.LessThan(49f), "stretch included");
        }

        [Test]
        public void MoveHint_WobblesOnlyTheCarriedBits_AsAWave_AndClearPutsThemBack()
        {
            var view = BuildHeart(out var board);
            Assert.That(view.ShowMoveHint(Move.Row(5, Direction.Left), board.Bits), Is.True);
            Assert.That(CountCells(view.IsBitHinted), Is.EqualTo(4), "row 6 is 00001111");

            view.TickHintWobble(0.5f);
            Assert.That(view.BitOffset(5, 7).x, Is.EqualTo(-4f / 46f).Within(0.001f), "the back of the line, first");
            Assert.That(view.BitOffset(5, 4).x, Is.GreaterThan(view.BitOffset(5, 7).x), "the front is still behind");
            Assert.That(view.BitOffset(4, 3), Is.EqualTo(Vector2.zero), "another row keeps still");

            view.ClearMoveHint();
            Assert.That(CountCells(view.IsBitHinted), Is.Zero);
            Assert.That(view.BitOffset(5, 7), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void MoveHint_KeepsHintedBitsAwake_WithOpenEyes()
        {
            var view = BuildHeart(out var board);
            int bits = CountCells(board.GetCell);
            view.IdleLife = true;
            view.ShowMoveHint(Move.Row(5, Direction.Left), board.Bits);
            Run(view, 10.2f + 0.35f * bits);

            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(bits - 4), "everyone else naps");
            Assert.That(view.EyeSquint(5, 4), Is.Zero, "at home on the target, but showing the hint");

            view.ClearMoveHint();
            Run(view, 2f);
            Assert.That(view.EyeSquint(5, 4), Is.GreaterThan(0.9f), "relaxes again");
        }

        [Test]
        public void MoveHint_OnAnEmptyRow_ShowsTheBitToCome_OrSaysItCannotShowIt()
        {
            var level = TestLevels.Heart();
            level.fillLeftRows = new[] { 7 };
            var board = new Board(level);
            gameObject = new GameObject("Board");
            var view = gameObject.AddComponent<BoardView>();
            view.Build(board.Rules);
            view.Show(board.Bits);

            Assert.That(view.ShowMoveHint(Move.Row(7, Direction.Right), board.Bits), Is.True, "1-IN feeds a bit");
            Assert.That(CountCells(view.IsBitHinted), Is.Zero);
            Assert.That(view.BitsToCome, Is.EqualTo(1));

            view.ClearMoveHint();
            Assert.That(view.BitsToCome, Is.Zero);
            Assert.That(view.ShowMoveHint(Move.Row(7, Direction.Left), board.Bits), Is.False, "nothing to show");
        }

        [Test]
        public void Controls_WithoutMarking_FillTheArrowOnly()
        {
            gameObject = new GameObject("Board");
            var controls = gameObject.AddComponent<BoardControls>();
            var rules = LevelRules.Compile(TestLevels.Heart());
            controls.Build(rules);
            var move = Move.Row(5, Direction.Left);
            controls.ShowHint(move, rules.Start, pulse: true, marking: false);

            Assert.That(controls.ShownHint, Is.EqualTo(move));
            Assert.That(controls.HintChevrons, Is.Zero);
            controls.Highlight(-1, 0, 0);
            foreach (var button in controls.Buttons)
            {
                Assert.That(button.Fill.enabled, Is.EqualTo(button.Move.Equals(move)), button.Move.ToString());
            }
        }

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

        Transform LeftEye(int row, int column) =>
            gameObject.transform.Find($"Cells/Cell {row + 1},{column + 1}/Bit/Eye L");

        [Test]
        public void AtHome_BitsOnATargetCellSquint_AtOnceOnShow()
        {
            // Heart's row 5 starts as 00111111 and wants 01111110.
            var view = BuildHeart(out _);
            Assert.That(view.EyeSquint(4, 2), Is.EqualTo(1f), "on a target cell");
            Assert.That(view.EyeSquint(4, 7), Is.EqualTo(0f), "not on a target cell");

            Vector3 home = LeftEye(4, 2).localScale, away = LeftEye(4, 7).localScale;
            Assert.That(home.y, Is.LessThan(away.y), "a squinting eye is lower in height");
            Assert.That(LeftEye(4, 2).localPosition.y, Is.LessThan(LeftEye(4, 7).localPosition.y), "and sits lower");
        }

        [Test]
        public void AtHome_ClosedEyesWinOverASquint()
        {
            var view = BuildHeart(out var board);
            int bits = CountCells(board.GetCell);
            view.IdleLife = true;
            Run(view, 10f + 0.35f * bits);
            Assert.That(view.EyeSquint(4, 2), Is.EqualTo(1f), "it is still at home");
            Assert.That(LeftEye(4, 2).localScale.y, Is.EqualTo(LeftEye(4, 7).localScale.y).Within(1e-4f),
                "but asleep they all look the same");
        }

        [Test]
        public void AtHome_IsOff_WhileTheEditorDrawsThePicture()
        {
            var view = BuildHeart(out var board);
            view.SquintAtHome = false;
            view.Show(board.Bits);
            Assert.That(CountCells((row, column) => view.EyeSquint(row, column) > 0f), Is.EqualTo(0));
            Run(view, 1f);
            Assert.That(CountCells((row, column) => view.EyeSquint(row, column) > 0f), Is.EqualTo(0));
        }

        BoardView BuildLevel(LevelData level, out Board board)
        {
            board = new Board(level);
            gameObject = new GameObject("Board");
            var view = gameObject.AddComponent<BoardView>();
            view.Build(board.Rules);
            view.Show(board.Bits);
            return view;
        }

        [Test]
        public void Peek_CursorLines_LookOverTheEdge_OnlyTheBitsThatCouldFall()
        {
            var view = BuildHeart(out var board);
            view.SetPeek(board, 2, 3, null);
            Assert.That(view.IsBitPeeking(2, 0), Is.True, "left end of the cursor's row");
            Assert.That(view.IsBitPeeking(2, 7), Is.True, "right end of the cursor's row");
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(2), "column 4 has no bit on the board's edge");
            Assert.That(CountCells(view.IsBitAlarmed), Is.EqualTo(0), "nothing is aimed yet");

            float restX = LeftEye(2, 0).localPosition.x;
            Run(view, 1f);
            Assert.That(LeftEye(2, 0).localPosition.x, Is.LessThan(restX), "the eyes really moved left");

            view.SetPeek(board, 5, 3, null);
            Assert.That(view.IsBitPeeking(2, 0), Is.False, "the cursor moved on");
            Assert.That(view.IsBitPeeking(5, 7), Is.True);
        }

        [Test]
        public void Peek_AimedMove_AlarmsOnlyTheBitItWouldDrop()
        {
            var view = BuildHeart(out var board);
            view.SetPeek(board, -1, 0, Move.Row(2, Direction.Left));
            Assert.That(view.IsBitAlarmed(2, 0), Is.True);
            Assert.That(CountCells(view.IsBitAlarmed), Is.EqualTo(1));
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(1), "no cursor lines while aiming");

            float restHeight = LeftEye(2, 1).localScale.y;
            Run(view, 1f);
            Assert.That(LeftEye(2, 0).localScale.y, Is.GreaterThan(restHeight), "wide eyes");

            view.SetPeek(board, -1, 0, Move.Row(4, Direction.Left));
            Assert.That(CountCells(view.IsBitAlarmed), Is.EqualTo(0), "row 5 has room on the left");
        }

        [Test]
        public void Peek_FollowsTheRules_WrappingRowsAndOtherPartsDoNotPeek()
        {
            var rotating = TestLevels.OneRow("11111111");
            rotating.rotateRows = new[] { 3 };
            var view = BuildLevel(rotating, out var board);
            view.SetPeek(board, 3, 2, Move.Row(3, Direction.Left));
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(0), "a wrapping row loses nothing");
            Object.DestroyImmediate(gameObject);

            view = BuildLevel(TestLevels.OneRow("11#00011", "00#00000"), out board);
            view.SetPeek(board, 3, 1, null);
            Assert.That(view.IsBitPeeking(3, 0), Is.True);
            Assert.That(view.IsBitPeeking(3, 7), Is.False, "behind the wall: the cursor's part can't move it");
        }

        [Test]
        public void Peek_NothingOnASolvedBoard_AndClearedByShow()
        {
            var solved = TestLevels.OneRow("11111111");
            solved.target[3] = solved.start[3];
            var view = BuildLevel(solved, out var board);
            view.SetPeek(board, 3, 2, Move.Row(3, Direction.Left));
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(0));
            Object.DestroyImmediate(gameObject);

            view = BuildHeart(out board);
            view.SetPeek(board, 2, 3, null);
            view.Show(board.Bits);
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(0));
            view.SetPeek(board, 2, 3, null);
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(2), "worked out again after Show");

            view.IdleLife = true;
            view.IdleLife = false;
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(0), "not behind panels");
        }

        [Test]
        public void Peek_DoesNotKeepEdgeBitsAwake()
        {
            var view = BuildHeart(out var board);
            int bits = CountCells(board.GetCell);
            view.IdleLife = true;
            view.SetPeek(board, 2, 3, null);
            Run(view, 10f + 0.35f * bits);
            Assert.That(CountCells(view.IsBitAsleep), Is.EqualTo(bits));
            Assert.That(view.EyeLid(2, 0), Is.EqualTo(1f));
        }

        [Test]
        public void SolveParty_HappyEyesReplaceTheEyes_UntilTheBoardChanges()
        {
            var view = BuildHeart(out var board);
            board.TryMove(Move.Row(4, Direction.Left), out var outcome);
            view.Play(outcome, false, false);
            Assert.That(CountCells(view.IsBitHappy), Is.EqualTo(0), "not solved yet");

            view.Play(outcome, true, false);
            Assert.That(view.IsBitHappy(2, 0), Is.True);
            var eye = LeftEye(2, 0).GetComponent<SpriteRenderer>();
            Assert.That(eye.enabled, Is.False, "the normal eyes step aside");

            view.Show(board.Bits);
            Assert.That(CountCells(view.IsBitHappy), Is.EqualTo(0), "undo or a new level ends the party");
            Assert.That(eye.enabled, Is.True);
        }

        [Test]
        public void Rescue_DoesNothingOutsidePlayMode_AndLeavesTheBoardShown()
        {
            var view = BuildHeart(out _);
            view.PlayRescue(BoardBits.CellMask(2, 0));
            Assert.That(view.IsBitShown(2, 0), Is.True);
        }

        [TestCase(0, 8, 4, 0)]
        [TestCase(5, 8, 4, 5)]
        [TestCase(32, 8, 4, 32)]
        [TestCase(64, 8, 4, 32)]
        [TestCase(64, 4, 1, 4)]
        public void ResidentsShown_StopsAtWhatTheCardHolds(int lost, int columns, int rows, int shown)
        {
            Assert.That(ShiftRegister.Game.Hud.HudView.ResidentsShown(lost, columns, rows), Is.EqualTo(shown));
        }

        [Test]
        public void BitLife_Off_LeavesPlainFaces_AndOnBringsThemBack()
        {
            var view = BuildHeart(out var board);
            Assert.That(view.EyeSquint(2, 0), Is.EqualTo(1f), "on by default");

            view.BitLife = false;
            Assert.That(CountCells((row, column) => view.EyeSquint(row, column) > 0f), Is.EqualTo(0), "no squint");

            view.IdleLife = true;
            view.SetPeek(board, 2, 3, Move.Row(2, Direction.Left));
            Assert.That(CountCells(view.IsBitPeeking), Is.EqualTo(0), "no peek");
            Assert.That(CountCells(view.IsBitAlarmed), Is.EqualTo(0), "no alarm");

            int closed = 0;
            Run(view, 15f, () => closed += CountCells((row, column) => view.EyeLid(row, column) > 0f));
            Assert.That(closed, Is.EqualTo(0), "no blink and no nap");

            board.TryMove(Move.Row(4, Direction.Left), out var outcome);
            view.Play(outcome, true, false);
            Assert.That(CountCells(view.IsBitHappy), Is.EqualTo(0), "no happy eyes");

            view.BitLife = true;
            Assert.That(view.EyeSquint(2, 0), Is.EqualTo(1f), "back at once");
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
