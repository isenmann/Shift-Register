using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.View;
using UnityEngine;
using static ShiftRegister.Tests.TestLevels;

namespace ShiftRegister.Tests
{
    public class InputMappingTests
    {
        static LevelRules WalledRow()
        {
            var level = OneRow("01000110", "01#00110"); // wall at row 3, column 2
            return LevelRules.Compile(level);
        }

        [Test]
        public void Cursor_ShiftsThePartUnderIt()
        {
            var rules = WalledRow();
            Assert.That(InputMapping.TryCursorMove(rules, 3, 6, Direction.Right, out var move), Is.True);
            Assert.That(move, Is.EqualTo(Move.Row(3, Direction.Right, 1)));
            Assert.That(InputMapping.TryCursorMove(rules, 3, 0, Direction.Left, out move), Is.True);
            Assert.That(move, Is.EqualTo(Move.Row(3, Direction.Left, 0)));
            Assert.That(InputMapping.TryCursorMove(rules, 3, 6, Direction.Up, out move), Is.True);
            Assert.That(move, Is.EqualTo(Move.Column(6, Direction.Up)));
        }

        [Test]
        public void Cursor_OnWall_NoMove()
        {
            Assert.That(InputMapping.TryCursorMove(WalledRow(), 3, 2, Direction.Left, out _), Is.False);
        }

        [Test]
        public void EdgeButtons_ShiftThePartNextToThem()
        {
            var rules = WalledRow();
            InputMapping.TryEdgeMove(rules, 3, Direction.Left, out var left);
            InputMapping.TryEdgeMove(rules, 3, Direction.Right, out var right);
            Assert.That(left.Segment, Is.EqualTo(0));
            Assert.That(right.Segment, Is.EqualTo(1));
            Assert.That(InputMapping.SegmentRange(rules, Axis.Row, 3, 1), Is.EqualTo((3, 7)));
        }

        [Test]
        public void EdgeButtons_DisabledOnPinnedLine()
        {
            var level = OneRow("01000000");
            level.pins[3] = Bits("01000000");
            level.target[3] = level.start[3];
            var rules = LevelRules.Compile(level);
            Assert.That(InputMapping.TryEdgeMove(rules, 3, Direction.Left, out _), Is.False);
            Assert.That(InputMapping.TryEdgeMove(rules, 1, Direction.Up, out _), Is.False);
            Assert.That(InputMapping.TryEdgeMove(rules, 2, Direction.Up, out _), Is.True);
        }

        [Test]
        public void HitCell_MapsBoardLocalPoints()
        {
            Assert.That(BoardControls.HitCell(BoardView.CellCenter(2, 5), out int row, out int column), Is.True);
            Assert.That((row, column), Is.EqualTo((2, 5)));
            Assert.That(BoardControls.HitCell(new Vector2(10f, 0f), out _, out _), Is.False);
        }

        [Test]
        public void Controls_Build32Buttons_HitTestFindsThem()
        {
            var gameObject = new GameObject("Board");
            try
            {
                var controls = gameObject.AddComponent<BoardControls>();
                controls.Build(WalledRow());
                Assert.That(controls.Buttons.Count, Is.EqualTo(32));
                var button = controls.Buttons[0];
                Assert.That(controls.HitButton(button.Rect.center), Is.EqualTo(0));
                Assert.That(controls.HitButton(Vector2.zero), Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
