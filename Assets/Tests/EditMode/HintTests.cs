using System.Linq;
using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.Progress;
using ShiftRegister.Game.View;
using UnityEditor;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class HintTests
    {
        static HintSearch Finish(HintSearch search)
        {
            while (!search.Step(1000))
            {
            }

            return search;
        }

        /// <summary>Heart with its stored solution, which plays rows 5, 6 and 7 in order.</summary>
        static LevelData HeartWithSolution()
        {
            var level = TestLevels.Heart();
            level.solution = "R5← R6← R6← R7← R7← R7←";
            return level;
        }

        [Test]
        public void AtTheStart_GivesTheFirstStoredMove()
        {
            var level = HeartWithSolution();
            var search = new HintSearch(new Board(level), level.solution);
            Assert.That(search.Status, Is.EqualTo(HintStatus.Move));
            Assert.That(search.Move, Is.EqualTo(Move.Row(4, Direction.Left)));
            Assert.That(search.MovesLeft, Is.EqualTo(6));
        }

        [Test]
        public void OnTheStoredPath_AnswersWithoutSearching()
        {
            var level = HeartWithSolution();
            var board = new Board(level);
            board.TryMove(Move.Row(4, Direction.Left), out _);
            board.TryMove(Move.Row(5, Direction.Left), out _);
            var search = new HintSearch(board, level.solution);
            Assert.That(search.IsDone, Is.True);
            Assert.That(search.Move, Is.EqualTo(Move.Row(5, Direction.Left)));
            Assert.That(search.MovesLeft, Is.EqualTo(4));
        }

        [Test]
        public void OffThePath_SearchesForTheShortestWayOn()
        {
            var level = HeartWithSolution();
            var board = new Board(level);
            board.TryMove(Move.Row(6, Direction.Left), out _); // the right row, but not the stored order
            var search = new HintSearch(board, level.solution);
            Assert.That(search.IsDone, Is.False);
            Finish(search);
            Assert.That(search.Status, Is.EqualTo(HintStatus.Move));
            Assert.That(search.MovesLeft, Is.EqualTo(5));
            Assert.That(board.TryMove(search.Move, out _), Is.True);
        }

        [Test]
        public void NoStoredSolution_SearchesFromTheBoard()
        {
            var board = new Board(TestLevels.Domino());
            var search = Finish(new HintSearch(board, ""));
            Assert.That(search.Status, Is.EqualTo(HintStatus.Move));
            Assert.That(search.MovesLeft, Is.EqualTo(6));
        }

        [Test]
        public void WrongStoredSolution_IsIgnored()
        {
            var board = new Board(TestLevels.Domino());
            var search = Finish(new HintSearch(board, "R5←"));
            Assert.That(search.Status, Is.EqualTo(HintStatus.Move));
            Assert.That(search.MovesLeft, Is.EqualTo(6));
        }

        [Test]
        public void ADetour_LeadsBackToThePath_WithoutASearchOfTheWholeLevel()
        {
            var level = HeartWithSolution();
            var board = new Board(level);
            board.TryMove(Move.Row(4, Direction.Left), out _);
            board.TryMove(Move.Column(0, Direction.Up), out _); // the top row is empty, so nothing falls off
            var search = Finish(new HintSearch(board, level.solution, maxStates: 1000));
            Assert.That(search.Status, Is.EqualTo(HintStatus.Move));
            Assert.That(search.Move, Is.EqualTo(Move.Column(0, Direction.Down)));
            Assert.That(search.MovesLeft, Is.EqualTo(6), "one move back, then the five left on the path");
        }

        [Test]
        public void TooHard_IsCuredByUndoingBackToThePath()
        {
            var level = HeartWithSolution();
            var board = new Board(level);
            board.TryMove(Move.Row(6, Direction.Left), out _);
            Assert.That(Finish(new HintSearch(board, level.solution, maxStates: 2)).Status,
                Is.EqualTo(HintStatus.TooHard));
            board.Undo();
            Assert.That(Finish(new HintSearch(board, level.solution, maxStates: 2)).Status,
                Is.EqualTo(HintStatus.Move));
        }

        [Test]
        public void SolvedBoard_NeedsNoHint()
        {
            var level = TestLevels.OneRow("00011000");
            level.target = (int[])level.start.Clone();
            Assert.That(new HintSearch(new Board(level), "").Status, Is.EqualTo(HintStatus.AlreadySolved));
        }

        [Test]
        public void LostTheBitsItNeeds_IsADeadEnd()
        {
            var level = HeartWithSolution();
            var board = new Board(level);
            board.TryMove(Move.Row(2, Direction.Left), out _); // a full row: one bit falls off
            var search = Finish(new HintSearch(board, level.solution));
            Assert.That(search.Status, Is.EqualTo(HintStatus.DeadEnd));
        }

        [Test]
        public void BrokenLossRule_IsADeadEnd()
        {
            var board = new Board(TestLevels.Domino());
            board.TryMove(Move.Row(0, Direction.Right), out _);
            Assert.That(board.IsRuleBroken, Is.True);
            var search = Finish(new HintSearch(board, ""));
            Assert.That(search.Status, Is.EqualTo(HintStatus.DeadEnd));
        }

        [Test]
        public void SearchOutOfStates_IsTooHard()
        {
            var board = new Board(TestLevels.FourGates());
            var search = Finish(new HintSearch(board, "", maxStates: 2));
            Assert.That(search.Status, Is.EqualTo(HintStatus.TooHard));
        }

        [Test]
        public void SlicedSearch_GivesTheSameAnswer()
        {
            var board = new Board(TestLevels.Domino());
            var whole = Finish(new HintSearch(board, ""));
            var sliced = new HintSearch(board, "");
            while (!sliced.Step(1))
            {
            }

            Assert.That(sliced.Move, Is.EqualTo(whole.Move));
            Assert.That(sliced.MovesLeft, Is.EqualTo(whole.MovesLeft));
        }

        [Test]
        public void BeginFrom_TheStart_MatchesBegin()
        {
            var rules = LevelRules.Compile(TestLevels.Domino());
            var job = Solver.BeginFrom(rules, rules.Start, 0);
            while (!job.Step(int.MaxValue))
            {
            }

            Assert.That(job.Result.Par, Is.EqualTo(Solver.Solve(rules).Par));
        }

        /// <summary>
        /// From any scrambled board, doing what the hints say must end in a solve: play the move,
        /// undo when the board is a dead end or the search gives up.
        /// </summary>
        [Test]
        public void EveryCatalogLevel_HintsLeadToTheSolve_FromScrambledBoards()
        {
            var catalog = AssetDatabase.FindAssets("t:" + nameof(LevelCatalog))
                .Select(guid => AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetDatabase.GUIDToAssetPath(guid)))
                .Single();
            var random = new System.Random(2026);
            foreach (var (_, _, file) in catalog.AllLevels())
            {
                var level = LevelJson.Parse(file);
                var rules = LevelRules.Compile(level);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var board = new Board(rules);
                    for (int scramble = attempt * 2; scramble > 0; scramble--)
                    {
                        board.TryMove(rules.Moves[random.Next(rules.Moves.Count)], out _);
                    }

                    if (board.IsSolved)
                    {
                        continue;
                    }

                    int steps = 0;
                    while (!board.IsSolved)
                    {
                        Assert.That(++steps, Is.LessThan(200), $"{level.id}: the hints never finish");
                        var hint = Finish(new HintSearch(board, level.solution));
                        switch (hint.Status)
                        {
                            case HintStatus.Move:
                                Assert.That(board.TryMove(hint.Move, out _), Is.True, $"{level.id}: {hint.Move}");
                                break;
                            case HintStatus.DeadEnd:
                            case HintStatus.TooHard:
                                Assert.That(board.Undo(), Is.True, $"{level.id}: {hint.Status} with nothing to undo");
                                break;
                            default:
                                Assert.Fail($"{level.id}: {hint.Status}");
                                break;
                        }
                    }
                }
            }
        }

        [Test]
        public void Controls_MarkTheHintedLine_AndClearIt()
        {
            var gameObject = new GameObject("Board");
            try
            {
                var controls = gameObject.AddComponent<BoardControls>();
                var rules = LevelRules.Compile(TestLevels.Heart());
                controls.Build(rules);
                var move = Move.Row(5, Direction.Left);
                controls.ShowHint(move, rules.Start, pulse: false);
                Assert.That(controls.ShownHint, Is.EqualTo(move));
                Assert.That(controls.HintChevrons, Is.EqualTo(BoardBits.Size));

                controls.Highlight(-1, 0, 0);
                foreach (var button in controls.Buttons)
                {
                    Assert.That(button.Fill.enabled, Is.EqualTo(button.Move.Equals(move)), button.Move.ToString());
                }

                controls.ClearHint();
                Assert.That(controls.ShownHint, Is.Null);
                Assert.That(controls.HintChevrons, Is.Zero);
                controls.Highlight(-1, 0, 0);
                Assert.That(controls.Buttons.Any(button => button.Fill.enabled), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Controls_MarkOnlyThePartOfAWalledLine()
        {
            var gameObject = new GameObject("Board");
            try
            {
                var controls = gameObject.AddComponent<BoardControls>();
                var rules = LevelRules.Compile(TestLevels.OneRow("11000000", "00#00000"));
                controls.Build(rules);
                controls.ShowHint(Move.Row(3, Direction.Right, 1), rules.Start, pulse: false);
                Assert.That(controls.HintChevrons, Is.EqualTo(5));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HelpExplainsTheCost_OnBothControlLists()
        {
            Assert.That(HelpText.Controls.Any(entry => entry.Title == "T" && entry.Text.Contains("1 star")), Is.True);
            Assert.That(HelpText.TouchControls.Any(entry => entry.Title == "HINT" && entry.Text.Contains("1 star")),
                Is.True);
        }

        [Test]
        public void HintSolve_IsOneStar_EvenAtPar()
        {
            Assert.That(Scoring.Stars(6, 6, usedHint: true), Is.EqualTo(Scoring.HintStars));
            var progress = new PlayerProgress();
            var result = progress.Record("2-3", 6, 6, usedHint: true);
            Assert.That(result.Stars, Is.EqualTo(1));
            Assert.That(progress.StarsFor("2-3"), Is.EqualTo(1));
            Assert.That(progress.IsSolved("2-3"), Is.True, "a hint solve still counts for unlocks");
        }

        [Test]
        public void OwnSolve_ReplacesAHintBest_EvenWithMoreMoves()
        {
            var progress = new PlayerProgress();
            progress.Record("2-3", 6, 6, usedHint: true);
            var own = progress.Record("2-3", 8, 6);
            Assert.That(own.Stars, Is.EqualTo(2));
            Assert.That(own.PreviousBest, Is.Zero, "the banner must not call it a new best");
            Assert.That(progress.Get("2-3").bestMoves, Is.EqualTo(8));
            Assert.That(progress.StarsFor("2-3"), Is.EqualTo(2));
        }

        [Test]
        public void HintSolve_NeverTouchesAnOwnBest()
        {
            var progress = new PlayerProgress();
            progress.Record("2-3", 8, 6);
            var hinted = progress.Record("2-3", 6, 6, usedHint: true);
            Assert.That(hinted.IsNewBest, Is.False);
            Assert.That(progress.Get("2-3").bestMoves, Is.EqualTo(8));
            Assert.That(progress.StarsFor("2-3"), Is.EqualTo(2));
        }

        [Test]
        public void HintSolves_KeepTheirOwnBest_UntilAnOwnSolve()
        {
            var progress = new PlayerProgress();
            progress.Record("2-3", 9, 6, usedHint: true);
            var better = progress.Record("2-3", 7, 6, usedHint: true);
            Assert.That(better.IsNewBest, Is.True);
            Assert.That(progress.Get("2-3").bestMoves, Is.EqualTo(7));
            Assert.That(progress.Get("2-3").bestUsedHint, Is.True);
            Assert.That(progress.StarsFor("2-3"), Is.EqualTo(1));
        }
    }
}
