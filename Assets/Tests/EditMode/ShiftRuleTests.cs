using NUnit.Framework;
using ShiftRegister.Core;
using static ShiftRegister.Tests.TestLevels;

namespace ShiftRegister.Tests
{
    /// <summary>The examples from the Mechanics and Bitwise operations mockups.</summary>
    public class ShiftRuleTests
    {
        static MoveOutcome Apply(LevelData level, Move move)
        {
            var rules = LevelRules.Compile(level);
            Assert.That(rules.TryApply(rules.Start, move, out var outcome), Is.True, $"{move} should be legal");
            return outcome;
        }

        static string Row3(MoveOutcome outcome) => BoardBits.ToBinary(BoardBits.GetRow(outcome.After, 3));

        [Test]
        public void Shift_MovesWholeRowOneCell()
        {
            var outcome = Apply(OneRow("00110100"), Move.Row(3, Direction.Right));
            Assert.That(Row3(outcome), Is.EqualTo("00011010"));
            Assert.That(outcome.LostCount, Is.Zero);
        }

        [Test]
        public void Edge_DropsBitIntoBucket()
        {
            var outcome = Apply(OneRow("10000001"), Move.Row(3, Direction.Left));
            Assert.That(Row3(outcome), Is.EqualTo("00000010"));
            Assert.That(outcome.LostCount, Is.EqualTo(1));
            Assert.That(outcome.LostCells, Is.EqualTo(BoardBits.CellMask(3, 0)));
        }

        [Test]
        public void Left_Doubles_Right_Halves()
        {
            var rules = LevelRules.Compile(OneRow("00000011"));
            rules.TryApply(rules.Start, Move.Row(3, Direction.Left), out var first);
            rules.TryApply(first.After, Move.Row(3, Direction.Left), out var second);
            Assert.That(BoardBits.GetRow(first.After, 3), Is.EqualTo(6));
            Assert.That(BoardBits.GetRow(second.After, 3), Is.EqualTo(12));
            rules.TryApply(second.After, Move.Row(3, Direction.Right), out var third);
            Assert.That(BoardBits.GetRow(third.After, 3), Is.EqualTo(6));
        }

        [Test]
        public void Column_ShiftsUpAndDown_WithEdgeLoss()
        {
            var level = new LevelData();
            level.start[0] = Bits("00100000");
            level.start[5] = Bits("00100000");
            var rules = LevelRules.Compile(level);

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
            var level = OneRow("01000110", "01#00110");
            var rules = LevelRules.Compile(level);
            Assert.That(rules.SegmentCount(Axis.Row, 3), Is.EqualTo(2));
            Assert.That(rules.SegmentOf(Axis.Row, 3, 5), Is.EqualTo(1));
            Assert.That(rules.SegmentOf(Axis.Row, 3, 2), Is.EqualTo(-1));

            var right = Apply(level, Move.Row(3, Direction.Right, segment: 1));
            Assert.That(Row3(right), Is.EqualTo("01000011"), "mockup: 01#00110 → 01#00011");

            var leftPart = Apply(level, Move.Row(3, Direction.Right, segment: 0));
            Assert.That(Row3(leftPart), Is.EqualTo("00000110"), "the wall acts as an edge");
            Assert.That(leftPart.LostCells, Is.EqualTo(BoardBits.CellMask(3, 1)));
        }

        [Test]
        public void Wall_AlsoSplitsColumns()
        {
            var level = new LevelData();
            level.walls[4] = Bits("10000000");
            level.start[3] = Bits("10000000");
            level.start[5] = Bits("10000000");
            var rules = LevelRules.Compile(level);
            Assert.That(rules.SegmentCount(Axis.Column, 0), Is.EqualTo(2));

            rules.TryApply(rules.Start, Move.Column(0, Direction.Down, segment: 0), out var outcome);
            Assert.That(BoardBits.GetCell(outcome.After, 3, 0), Is.False, "bit above the wall falls into it");
            Assert.That(BoardBits.GetCell(outcome.After, 5, 0), Is.True, "lower part does not move");
            Assert.That(outcome.LostCount, Is.EqualTo(1));
        }

        [Test]
        public void Pin_LocksItsRowAndColumn()
        {
            var level = OneRow("01011000");
            level.pins[3] = Bits("01000000");
            level.target[3] = level.start[3];
            var rules = LevelRules.Compile(level);
            Assert.That(rules.CanMove(Move.Row(3, Direction.Left)), Is.False);
            Assert.That(rules.CanMove(Move.Column(1, Direction.Up)), Is.False);
            Assert.That(rules.CanMove(Move.Column(3, Direction.Up)), Is.True);
            Assert.That(rules.CanMove(Move.Row(2, Direction.Left)), Is.True);
        }

        [Test]
        public void Rotate_WrapsBitAround()
        {
            var level = OneRow("10000110");
            level.rotateRows = new[] { 3 };
            var outcome = Apply(level, Move.Row(3, Direction.Left));
            Assert.That(Row3(outcome), Is.EqualTo("00001101"));
            Assert.That(outcome.LostCount, Is.Zero);
        }

        [Test]
        public void Carry_OverflowMovesIntoRowAbove()
        {
            var level = new LevelData { chainTopRows = new[] { 2 } };
            level.start[2] = Bits("00000001");
            level.start[3] = Bits("10000000");
            var rules = LevelRules.Compile(level);

            rules.TryApply(rules.Start, Move.Row(2, Direction.Left), out var outcome);
            Assert.That(BoardBits.GetRow(outcome.After, 2), Is.EqualTo(Bits("00000011")));
            Assert.That(BoardBits.GetRow(outcome.After, 3), Is.Zero);

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
