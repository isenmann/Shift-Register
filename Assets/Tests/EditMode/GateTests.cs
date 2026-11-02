using NUnit.Framework;
using ShiftRegister.Core;
using static ShiftRegister.Tests.TestLevels;

namespace ShiftRegister.Tests
{
    /// <summary>Chapter 6 gates, using the examples from the doc's "Bitwise gates" table.</summary>
    public class GateTests
    {
        static string ShiftRight(LevelData d)
        {
            var rules = LevelRules.Compile(d);
            Assert.That(rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var o), Is.True);
            return BoardBits.ToBinary(BoardBits.GetRow(o.After, 3));
        }

        static LevelData Gated(string row, GateOp op, string mask)
        {
            var d = OneRow(row);
            d.gates = new[] { new RowGate(3, op, Bits(mask)) };
            return d;
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
            var d = OneRow("00000110");
            d.fillLeftRows = new[] { 3 };
            Assert.That(ShiftRight(d), Is.EqualTo("10000011"));
        }

        [Test]
        public void Gates_AreApplied_OnlyToTheMovedRow()
        {
            var d = FourGates();
            var rules = LevelRules.Compile(d);
            // Solve the XOR row, then move another row: the XOR row must stay at 0.
            rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var a);
            Assert.That(BoardBits.GetRow(a.After, 3), Is.Zero);
            rules.TryApply(a.After, Move.Row(5, Direction.Right), out var b);
            Assert.That(BoardBits.GetRow(b.After, 3), Is.Zero);
            Assert.That(BoardBits.GetRow(b.After, 5), Is.EqualTo(60));
        }

        [Test]
        public void Gates_OnColumnMoves_WhenEnabled()
        {
            var d = new LevelData { gatesOnColumnMoves = true };
            d.start[2] = Bits("00010000");
            d.gates = new[] { new RowGate(3, GateOp.Or, Bits("00000001")) };
            var rules = LevelRules.Compile(d);

            rules.TryApply(rules.Start, Move.Column(3, Direction.Down), out var o);
            Assert.That(BoardBits.GetRow(o.AfterShift, 3), Is.EqualTo(Bits("00010000")));
            Assert.That(BoardBits.GetRow(o.After, 3), Is.EqualTo(Bits("00010001")));

            d.gatesOnColumnMoves = false;
            rules = LevelRules.Compile(d);
            rules.TryApply(rules.Start, Move.Column(3, Direction.Down), out o);
            Assert.That(BoardBits.GetRow(o.After, 3), Is.EqualTo(Bits("00010000")));
        }

        [Test]
        public void Gates_DoNotCountAsLostBits()
        {
            var d = Gated("00110000", GateOp.Xor, "00011000");
            var rules = LevelRules.Compile(d);
            rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var o);
            Assert.That(o.LostCount, Is.Zero);
        }
    }
}
