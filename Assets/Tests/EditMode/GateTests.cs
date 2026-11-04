using NUnit.Framework;
using ShiftRegister.Core;
using static ShiftRegister.Tests.TestLevels;

namespace ShiftRegister.Tests
{
    /// <summary>Chapter 6 gates, using the examples from the doc's "Bitwise gates" table.</summary>
    public class GateTests
    {
        static string ShiftRight(LevelData level)
        {
            var rules = LevelRules.Compile(level);
            Assert.That(rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var outcome), Is.True);
            return BoardBits.ToBinary(BoardBits.GetRow(outcome.After, 3));
        }

        static LevelData Gated(string row, GateOp op, string mask)
        {
            var level = OneRow(row);
            level.gates = new[] { new RowGate(3, op, Bits(mask)) };
            return level;
        }

        [Test]
        public void And_KeepsOnlyMaskedBits() =>
            Assert.That(ShiftRight(Gated("11011011", GateOp.And, "00111100")), Is.EqualTo("00101100"));

        [Test]
        public void Or_PrintsMaskBits() =>
            Assert.That(ShiftRight(Gated("10000000", GateOp.Or, "00011000")), Is.EqualTo("01011000"));

        [Test]
        public void Xor_MatchingBitsCancel() =>
            Assert.That(ShiftRight(Gated("00110000", GateOp.Xor, "00011000")), Is.EqualTo("00000000"));

        [Test]
        public void XorCell_GapLandingBecomesBit() =>
            Assert.That(ShiftRight(Gated("00000000", GateOp.Xor, "00001000")), Is.EqualTo("00001000"));

        [Test]
        public void NotPad_BitLandingInvertsRow() =>
            Assert.That(ShiftRight(Gated("00000110", GateOp.Not, "00000001")), Is.EqualTo("11111100"));

        [Test]
        public void NotPad_NoBitOnPad_NoChange() =>
            Assert.That(ShiftRight(Gated("00001100", GateOp.Not, "00000001")), Is.EqualTo("00000110"));

        [Test]
        public void FillEdge_FeedsBitWhenShiftingAway()
        {
            var level = OneRow("00000110");
            level.fillLeftRows = new[] { 3 };
            Assert.That(ShiftRight(level), Is.EqualTo("10000011"));
        }

        [Test]
        public void Gates_AreApplied_OnlyToTheMovedRow()
        {
            var level = FourGates();
            var rules = LevelRules.Compile(level);
            // Solve the XOR row, then move another row: the XOR row must stay at 0.
            rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var first);
            Assert.That(BoardBits.GetRow(first.After, 3), Is.Zero);
            rules.TryApply(first.After, Move.Row(5, Direction.Right), out var second);
            Assert.That(BoardBits.GetRow(second.After, 3), Is.Zero);
            Assert.That(BoardBits.GetRow(second.After, 5), Is.EqualTo(60));
        }

        [Test]
        public void Gates_OnColumnMoves_WhenEnabled()
        {
            var level = new LevelData { gatesOnColumnMoves = true };
            level.start[2] = Bits("00010000");
            level.gates = new[] { new RowGate(3, GateOp.Or, Bits("00000001")) };
            var rules = LevelRules.Compile(level);

            rules.TryApply(rules.Start, Move.Column(3, Direction.Down), out var outcome);
            Assert.That(BoardBits.GetRow(outcome.AfterShift, 3), Is.EqualTo(Bits("00010000")));
            Assert.That(BoardBits.GetRow(outcome.After, 3), Is.EqualTo(Bits("00010001")));

            level.gatesOnColumnMoves = false;
            rules = LevelRules.Compile(level);
            rules.TryApply(rules.Start, Move.Column(3, Direction.Down), out outcome);
            Assert.That(BoardBits.GetRow(outcome.After, 3), Is.EqualTo(Bits("00010000")));
        }

        [Test]
        public void Gates_DoNotCountAsLostBits()
        {
            var level = Gated("00110000", GateOp.Xor, "00011000");
            var rules = LevelRules.Compile(level);
            rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var outcome);
            Assert.That(outcome.LostCount, Is.Zero);
        }
    }
}
