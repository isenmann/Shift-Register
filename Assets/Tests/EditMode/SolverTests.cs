using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class SolverTests
    {
        static void AssertReplays(LevelData d, SolveResult result)
        {
            var board = new Board(d);
            foreach (var m in result.Solution)
            {
                Assert.That(board.TryMove(m, out _), Is.True, m.ToString());
            }

            Assert.That(board.IsSolved, Is.True, BoardBits.ToPicture(board.Bits));
        }

        [Test]
        public void AlreadySolved_ParZero()
        {
            var d = TestLevels.OneRow("00011000");
            d.target = (int[])d.start.Clone();
            Assert.That(Solver.Solve(d).Par, Is.Zero);
        }

        [Test]
        public void Heart_Par6()
        {
            var d = TestLevels.Heart();
            var r = Solver.Solve(d);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.Solved), r.ToString());
            Assert.That(r.Par, Is.EqualTo(6), r.ToString());
            AssertReplays(d, r);
        }

        [Test]
        public void Domino_Par6()
        {
            var d = TestLevels.Domino();
            var r = Solver.Solve(d);
            Assert.That(r.Par, Is.EqualTo(6), r.ToString());
            AssertReplays(d, r);
        }

        [Test]
        public void FourGates_Par5()
        {
            var d = TestLevels.FourGates();
            var r = Solver.Solve(d);
            Assert.That(r.Par, Is.EqualTo(5), r.ToString());
            AssertReplays(d, r);
        }

        [Test]
        public void ExactlyRule_FindsBucketGoal()
        {
            var d = TestLevels.OneRow("11110000");
            d.target[3] = TestLevels.Bits("11000000");
            d.lossRule = LossRule.Exactly;
            d.lossCount = 2;
            var r = Solver.Solve(d);
            Assert.That(r.Par, Is.EqualTo(2), r.ToString());
        }

        [Test]
        public void MoreBitsThanStart_Unsolvable()
        {
            var d = TestLevels.OneRow("00010000");
            d.target[3] = TestLevels.Bits("00011000");
            Assert.That(Solver.Solve(d).Status, Is.EqualTo(SolveStatus.Unsolvable));
        }

        [Test]
        public void LoseZero_WhenLossIsForced_Unsolvable()
        {
            var d = TestLevels.OneRow("11000000");
            d.target[3] = TestLevels.Bits("10000000");
            d.lossRule = LossRule.AtMost;
            Assert.That(Solver.Solve(d).Status, Is.EqualTo(SolveStatus.Unsolvable));
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(1000)]
        public void SlicedJob_MatchesSolve_ForAnySliceSize(int slice)
        {
            foreach (var d in new[] { TestLevels.Heart(), TestLevels.OneRow("00011000") })
            {
                var whole = Solver.Solve(d);
                var job = Solver.Begin(LevelRules.Compile(d));
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
            var d = TestLevels.OneRow("10000000");
            d.target[0] = 255;
            var whole = Solver.Solve(d);
            Assert.That(whole.Status, Is.EqualTo(SolveStatus.Unsolvable));
            for (int slice = 1; slice <= whole.StatesVisited; slice++)
            {
                var job = Solver.Begin(LevelRules.Compile(d));
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
            var d = TestLevels.OneRow("00000001");
            d.target[3] = 0;
            d.lossRule = LossRule.AtMost;
            d.lossCount = 0;
            Assert.That(Solver.Replays(LevelRules.Compile(d), new[] { Move.Row(3, Direction.Right) }), Is.False);
        }

        [Test]
        public void MoveByte_RoundTrips_ForEveryMove()
        {
            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                for (int line = 0; line < 8; line++)
                {
                    for (int seg = 0; seg < 8; seg++)
                    {
                        var m = new Move(dir, line, seg);
                        Assert.That(Move.FromByte(m.ToByte()), Is.EqualTo(m));
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
            var r = Solver.Solve(TestLevels.Heart(), maxStates: 50);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.LimitReached));
        }
    }
}
