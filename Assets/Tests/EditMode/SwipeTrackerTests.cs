using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class SwipeTrackerTests
    {
        static SwipeTracker Started(out SwipeTracker t)
        {
            t = new SwipeTracker(1f, 0.4f);
            t.Begin(Vector2.zero, 5, 2);
            return t;
        }

        [Test]
        public void SmallMove_IsATap()
        {
            Started(out var t);
            Assert.That(t.Step(new Vector2(0.3f, -0.2f)), Is.Null);
            Assert.That(t.Swiped, Is.False);
        }

        [Test]
        public void FirstShiftAtThreshold_ThenOnePerStep()
        {
            Started(out var t);
            Assert.That(t.Step(new Vector2(-0.5f, 0.1f)), Is.EqualTo(Direction.Left));
            Assert.That(t.Step(new Vector2(-0.5f, 0.1f)), Is.Null, "each shift is earned once");
            Assert.That(t.Step(new Vector2(-1.2f, 0f)), Is.Null);
            Assert.That(t.Step(new Vector2(-1.5f, 0f)), Is.EqualTo(Direction.Left));
            Assert.That(t.Row, Is.EqualTo(5));
            Assert.That(t.Column, Is.EqualTo(2));
        }

        [Test]
        public void FastDrag_EarnsSeveralShiftsAtOnce()
        {
            Started(out var t);
            int shifts = 0;
            while (t.Step(new Vector2(0f, 2.5f)) == Direction.Up) shifts++;
            Assert.That(shifts, Is.EqualTo(3), "0.4, 1.4 and 2.4 are crossed");
        }

        [Test]
        public void AxisLocksOnTheFirstShift()
        {
            Started(out var t);
            Assert.That(t.Step(new Vector2(0f, -0.6f)), Is.EqualTo(Direction.Down));
            Assert.That(t.Step(new Vector2(3f, -0.6f)), Is.Null, "sideways drift does not shift the row");
        }

        [Test]
        public void DraggingBack_ShiftsTheOtherWay()
        {
            Started(out var t);
            Assert.That(t.Step(new Vector2(0.5f, 0f)), Is.EqualTo(Direction.Right));
            Assert.That(t.Step(new Vector2(-0.7f, 0f)), Is.EqualTo(Direction.Left));
        }

        [Test]
        public void Ended_ReturnsNothing()
        {
            Started(out var t);
            t.End();
            Assert.That(t.Step(new Vector2(5f, 0f)), Is.Null);
        }
    }
}
