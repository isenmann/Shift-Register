using ShiftRegister.Core;
using UnityEngine;

namespace ShiftRegister.Game.Input
{
    /// <summary>
    /// Turns a drag on the board into shifts of the row or column it started on. The first shift fires
    /// after <see cref="threshold"/> of movement and locks the axis; every further <see cref="step"/>
    /// along it shifts again, so the line follows the finger. Positions are in board-local units
    /// (y up). Plain C#, so it is testable without input devices.
    /// </summary>
    public sealed class SwipeTracker
    {
        readonly float step, threshold;
        Vector2 origin;
        Axis? axis;

        public bool Active { get; private set; }
        /// <summary>True once the current drag has shifted at least once (it is not a tap).</summary>
        public bool Swiped { get; private set; }
        public int Row { get; private set; }
        public int Column { get; private set; }
        /// <summary>The way the drag last shifted, which is where it will shift next if it goes on.</summary>
        public Direction? LastDirection { get; private set; }

        public SwipeTracker(float step, float threshold)
        {
            this.step = step;
            this.threshold = threshold;
        }

        public void Begin(Vector2 local, int row, int column)
        {
            origin = local;
            axis = null;
            Row = row;
            Column = column;
            Active = true;
            Swiped = false;
            LastDirection = null;
        }

        public void End() => Active = false;

        /// <summary>The next shift the drag has earned, if any; call again until it returns null.</summary>
        public Direction? Step(Vector2 local)
        {
            if (!Active)
            {
                return null;
            }

            Vector2 delta = local - origin;
            if (axis == null)
            {
                if (Mathf.Abs(delta.x) < threshold && Mathf.Abs(delta.y) < threshold)
                {
                    return null;
                }

                axis = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? Axis.Row : Axis.Column;
            }

            float along = axis == Axis.Row ? delta.x : delta.y;
            float need = Swiped ? step : threshold;
            if (Mathf.Abs(along) < need)
            {
                return null;
            }

            float sign = Mathf.Sign(along);
            origin += (axis == Axis.Row ? Vector2.right : Vector2.up) * sign * need;
            Swiped = true;
            if (axis == Axis.Row)
            {
                LastDirection = sign > 0 ? Direction.Right : Direction.Left;
            }
            else
            {
                LastDirection = sign > 0 ? Direction.Up : Direction.Down;
            }

            return LastDirection;
        }
    }
}
