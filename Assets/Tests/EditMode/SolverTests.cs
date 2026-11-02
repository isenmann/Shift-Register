using NUnit.Framework;
using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    public class SolverTests
    {
        static void AssertReplays(LevelData d, SolveResult result)
        {
            var board = new Board(d);
            foreach (var m in result.Solution) Assert.That(board.TryMove(m, out _), Is.True, m.ToString());
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

        [Test]
        public void StateLimit_ReportsLimitReached()
        {
            var r = Solver.Solve(TestLevels.Heart(), maxStates: 50);
            Assert.That(r.Status, Is.EqualTo(SolveStatus.LimitReached));
        }
    }
}
