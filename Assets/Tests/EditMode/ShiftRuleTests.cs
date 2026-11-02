using NUnit.Framework;
using ShiftRegister.Core;
using static ShiftRegister.Tests.TestLevels;

namespace ShiftRegister.Tests
{
    /// <summary>The examples from the Mechanics and Bitwise operations mockups.</summary>
    public class ShiftRuleTests
    {
        static MoveOutcome Apply(LevelData d, Move m)
        {
            var rules = LevelRules.Compile(d);
            Assert.That(rules.TryApply(rules.Start, m, out var o), Is.True, $"{m} should be legal");
            return o;
        }

        static string Row3(MoveOutcome o) => BoardBits.ToBinary(BoardBits.GetRow(o.After, 3));

        [Test]
        public void Shift_MovesWholeRowOneCell()
        {
            var o = Apply(OneRow("00110100"), Move.Row(3, Direction.Right));
            Assert.That(Row3(o), Is.EqualTo("00011010"));
            Assert.That(o.LostCount, Is.Zero);
        }

        [Test]
        public void Edge_DropsBitIntoBucket()
        {
            var o = Apply(OneRow("10000001"), Move.Row(3, Direction.Left));
            Assert.That(Row3(o), Is.EqualTo("00000010"));
            Assert.That(o.LostCount, Is.EqualTo(1));
            Assert.That(o.LostCells, Is.EqualTo(BoardBits.CellMask(3, 0)));
        }

        [Test]
        public void Left_Doubles_Right_Halves()
        {
            var rules = LevelRules.Compile(OneRow("00000011"));
            rules.TryApply(rules.Start, Move.Row(3, Direction.Left), out var a);
            rules.TryApply(a.After, Move.Row(3, Direction.Left), out var b);
            Assert.That(BoardBits.GetRow(a.After, 3), Is.EqualTo(6));
            Assert.That(BoardBits.GetRow(b.After, 3), Is.EqualTo(12));
            rules.TryApply(b.After, Move.Row(3, Direction.Right), out var c);
            Assert.That(BoardBits.GetRow(c.After, 3), Is.EqualTo(6));
        }

        [Test]
        public void Column_ShiftsUpAndDown_WithEdgeLoss()
        {
            var d = new LevelData();
            d.start[0] = Bits("00100000");
            d.start[5] = Bits("00100000");
            var rules = LevelRules.Compile(d);

            rules.TryApply(rules.Start, Move.Column(2, Direction.Up), out var up);
            Assert.That(BoardBits.GetColumn(up.After, 2), Is.EqualTo(0b00001000));
            Assert.That(up.LostCells, Is.EqualTo(BoardBits.CellMask(0, 2)));

            rules.TryApply(rules.Start, Move.Column(2, Direction.Down), out var down);
            Assert.That(BoardBits.GetColumn(down.After, 2), Is.EqualTo(0b01000010));
            Assert.That(down.LostCount, Is.Zero);
        }

        [Test]
        public void Wall_SplitsRowIntoPartsThatShiftOnTheirOwn()
        {
            var d = OneRow("01000110", "01#00110");
            var rules = LevelRules.Compile(d);
            Assert.That(rules.SegmentCount(Axis.Row, 3), Is.EqualTo(2));
            Assert.That(rules.SegmentOf(Axis.Row, 3, 5), Is.EqualTo(1));
            Assert.That(rules.SegmentOf(Axis.Row, 3, 2), Is.EqualTo(-1));

            var right = Apply(d, Move.Row(3, Direction.Right, segment: 1));
            Assert.That(Row3(right), Is.EqualTo("01000011"), "mockup: 01#00110 → 01#00011");

            var leftPart = Apply(d, Move.Row(3, Direction.Right, segment: 0));
            Assert.That(Row3(leftPart), Is.EqualTo("00000110"), "the wall acts as an edge");
            Assert.That(leftPart.LostCells, Is.EqualTo(BoardBits.CellMask(3, 1)));
        }

        [Test]
        public void Wall_AlsoSplitsColumns()
        {
            var d = new LevelData();
            d.walls[4] = Bits("10000000");
            d.start[3] = Bits("10000000");
            d.start[5] = Bits("10000000");
            var rules = LevelRules.Compile(d);
            Assert.That(rules.SegmentCount(Axis.Column, 0), Is.EqualTo(2));

            rules.TryApply(rules.Start, Move.Column(0, Direction.Down, segment: 0), out var o);
            Assert.That(BoardBits.GetCell(o.After, 3, 0), Is.False, "bit above the wall falls into it");
            Assert.That(BoardBits.GetCell(o.After, 5, 0), Is.True, "lower part does not move");
            Assert.That(o.LostCount, Is.EqualTo(1));
        }

        [Test]
        public void Pin_LocksItsRowAndColumn()
        {
            var d = OneRow("01011000");
            d.pins[3] = Bits("01000000");
            d.target[3] = d.start[3];
            var rules = LevelRules.Compile(d);
            Assert.That(rules.CanMove(Move.Row(3, Direction.Left)), Is.False);
            Assert.That(rules.CanMove(Move.Column(1, Direction.Up)), Is.False);
            Assert.That(rules.CanMove(Move.Column(3, Direction.Up)), Is.True);
            Assert.That(rules.CanMove(Move.Row(2, Direction.Left)), Is.True);
        }

        [Test]
        public void Rotate_WrapsBitAround()
        {
            var d = OneRow("10000110");
            d.rotateRows = new[] { 3 };
            var o = Apply(d, Move.Row(3, Direction.Left));
            Assert.That(Row3(o), Is.EqualTo("00001101"));
            Assert.That(o.LostCount, Is.Zero);
        }

        [Test]
        public void Carry_OverflowMovesIntoRowAbove()
        {
            var d = new LevelData { chainTopRows = new[] { 2 } };
            d.start[2] = Bits("00000001");
            d.start[3] = Bits("10000000");
            var rules = LevelRules.Compile(d);

            rules.TryApply(rules.Start, Move.Row(2, Direction.Left), out var o);
            Assert.That(BoardBits.GetRow(o.After, 2), Is.EqualTo(Bits("00000011")));
            Assert.That(BoardBits.GetRow(o.After, 3), Is.Zero);

            Assert.That(rules.TryApply(rules.Start, Move.Row(3, Direction.Right), out var down), Is.True,
                "either row of a chain moves the whole chain");
            Assert.That(BoardBits.GetRow(down.After, 2), Is.Zero);
            Assert.That(BoardBits.GetRow(down.After, 3), Is.EqualTo(Bits("11000000")));
        }

        [Test]
        public void Chain_IsListedOnceInMoves()
        {
            var rules = LevelRules.Compile(new LevelData { chainTopRows = new[] { 2 } });
            Assert.That(rules.Moves, Has.Member(Move.Row(2, Direction.Left)));
            Assert.That(rules.Moves, Has.No.Member(Move.Row(3, Direction.Left)));
            Assert.That(rules.Moves.Count, Is.EqualTo(7 * 2 + 8 * 2));
        }
    }
}
