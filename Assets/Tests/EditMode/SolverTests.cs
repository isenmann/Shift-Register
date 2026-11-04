using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class SolverTests
    {
        static void AssertReplays(LevelData level, SolveResult result)
        {
            var board = new Board(level);
            foreach (var move in result.Solution)
            {
                Assert.That(board.TryMove(move, out _), Is.True, move.ToString());
            }

            Assert.That(board.IsSolved, Is.True, BoardBits.ToPicture(board.Bits));
        }

        [Test]
        public void AlreadySolved_ParZero()
        {
            var level = TestLevels.OneRow("00011000");
            level.target = (int[])level.start.Clone();
            Assert.That(Solver.Solve(level).Par, Is.Zero);
        }

        [Test]
        public void Heart_Par6()
        {
            var level = TestLevels.Heart();
            var result = Solver.Solve(level);
            Assert.That(result.Status, Is.EqualTo(SolveStatus.Solved), result.ToString());
            Assert.That(result.Par, Is.EqualTo(6), result.ToString());
            AssertReplays(level, result);
        }

        [Test]
        public void Domino_Par6()
        {
            var level = TestLevels.Domino();
            var result = Solver.Solve(level);
            Assert.That(result.Par, Is.EqualTo(6), result.ToString());
            AssertReplays(level, result);
        }

        [Test]
        public void FourGates_Par5()
        {
            var level = TestLevels.FourGates();
            var result = Solver.Solve(level);
            Assert.That(result.Par, Is.EqualTo(5), result.ToString());
            AssertReplays(level, result);
        }

        [Test]
        public void ExactlyRule_FindsBucketGoal()
        {
            var level = TestLevels.OneRow("11110000");
            level.target[3] = TestLevels.Bits("11000000");
            level.lossRule = LossRule.Exactly;
            level.lossCount = 2;
            var result = Solver.Solve(level);
            Assert.That(result.Par, Is.EqualTo(2), result.ToString());
        }

        [Test]
        public void MoreBitsThanStart_Unsolvable()
        {
            var level = TestLevels.OneRow("00010000");
            level.target[3] = TestLevels.Bits("00011000");
            Assert.That(Solver.Solve(level).Status, Is.EqualTo(SolveStatus.Unsolvable));
        }

        [Test]
        public void LoseZero_WhenLossIsForced_Unsolvable()
        {
            var level = TestLevels.OneRow("11000000");
            level.target[3] = TestLevels.Bits("10000000");
            level.lossRule = LossRule.AtMost;
            Assert.That(Solver.Solve(level).Status, Is.EqualTo(SolveStatus.Unsolvable));
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(1000)]
        public void SlicedJob_MatchesSolve_ForAnySliceSize(int slice)
        {
            foreach (var level in new[] { TestLevels.Heart(), TestLevels.OneRow("00011000") })
            {
                var whole = Solver.Solve(level);
                var job = Solver.Begin(LevelRules.Compile(level));
                while (!job.Step(slice))
                {
                }

                Assert.That(job.Result.Status, Is.EqualTo(whole.Status));
                Assert.That(job.Result.Par, Is.EqualTo(whole.Par));
                Assert.That(job.Result.StatesVisited, Is.EqualTo(whole.StatesVisited));
                Assert.That(job.Result.Solution, Is.EqualTo(whole.Solution));
                Assert.That(job.StatesVisited, Is.EqualTo(whole.StatesVisited), "still readable after the job frees its memory");
            }
        }

        [Test]
        public void SlicedJob_ReportsLimit_And_DoneJobStaysDone()
        {
            var job = Solver.Begin(LevelRules.Compile(TestLevels.Heart()), maxStates: 50);
            while (!job.Step(3))
            {
            }

            Assert.That(job.Result.Status, Is.EqualTo(SolveStatus.LimitReached));
            Assert.That(job.Step(3), Is.True);
        }

        [Test]
        public void SlicedJob_ReportsUnsolvable_WhenTheLastNodeEndsASlice()
        {
            var level = TestLevels.OneRow("10000000");
            level.target[0] = 255;
            var whole = Solver.Solve(level);
            Assert.That(whole.Status, Is.EqualTo(SolveStatus.Unsolvable));
            for (int slice = 1; slice <= whole.StatesVisited; slice++)
            {
                var job = Solver.Begin(LevelRules.Compile(level));
                while (!job.Step(slice))
                {
                }

                Assert.That(job.Result.Status, Is.EqualTo(SolveStatus.Unsolvable), $"slice {slice}");
            }
        }

        [Test]
        public void Replays_FollowsTheBoardsRules()
        {
            var rules = LevelRules.Compile(TestLevels.Heart());
            var solution = new[]
            {
                Move.Row(4, Direction.Left), Move.Row(5, Direction.Left), Move.Row(5, Direction.Left),
                Move.Row(6, Direction.Left), Move.Row(6, Direction.Left), Move.Row(6, Direction.Left)
            };
            Assert.That(Solver.Replays(rules, solution), Is.True);
            Assert.That(Solver.Replays(rules, new[] { Move.Row(4, Direction.Left) }), Is.False, "not solved yet");
            Assert.That(Solver.Replays(rules, new Move[0]), Is.False, "an empty list never counts");

            var pastTheSolve = new System.Collections.Generic.List<Move>(solution) { Move.Row(0, Direction.Left) };
            Assert.That(Solver.Replays(rules, pastTheSolve), Is.False, "the board refuses moves once solved");
        }

        [Test]
        public void Replays_RejectsBrokenLossRule()
        {
            var level = TestLevels.OneRow("00000001");
            level.target[3] = 0;
            level.lossRule = LossRule.AtMost;
            level.lossCount = 0;
            Assert.That(Solver.Replays(LevelRules.Compile(level), new[] { Move.Row(3, Direction.Right) }), Is.False);
        }

        [Test]
        public void MoveByte_RoundTrips_ForEveryMove()
        {
            foreach (Direction direction in System.Enum.GetValues(typeof(Direction)))
            {
                for (int line = 0; line < 8; line++)
                {
                    for (int seg = 0; seg < 8; seg++)
                    {
                        var move = new Move(direction, line, seg);
                        Assert.That(Move.FromByte(move.ToByte()), Is.EqualTo(move));
                    }
                }
            }
        }

        [Test]
        public void MoveNotation_RoundTrips()
        {
            var moves = new[] { Move.Row(4, Direction.Left), Move.Column(3, Direction.Up), Move.Row(1, Direction.Right, 1) };
            Assert.That(Move.TryParseList(string.Join(" ", moves), out var back), Is.True);
            Assert.That(back, Is.EqualTo(moves));
            Assert.That(Move.TryParseList("", out var none), Is.True);
            Assert.That(none, Is.Empty);
        }

        [TestCase("X1←")]
        [TestCase("R9←")]
        [TestCase("R1↑")]
        [TestCase("C1→")]
        [TestCase("R1")]
        [TestCase("R1.9←")]
        public void MoveNotation_RejectsNonsense(string text) =>
            Assert.That(Move.TryParseList(text, out _), Is.False);

        [Test]
        public void StateLimit_ReportsLimitReached()
        {
            var result = Solver.Solve(TestLevels.Heart(), maxStates: 50);
            Assert.That(result.Status, Is.EqualTo(SolveStatus.LimitReached));
        }
    }
}
