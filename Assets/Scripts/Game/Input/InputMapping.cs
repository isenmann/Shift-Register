using ShiftRegister.Core;

namespace ShiftRegister.Game.Input
{
    /// <summary>Turns player intents (cursor + direction, edge arrow buttons) into moves.</summary>
    public static class InputMapping
    {
        public static Axis AxisOf(Direction direction) => direction == Direction.Left || direction == Direction.Right ? Axis.Row : Axis.Column;

        /// <summary>Shift the line part under the cursor cell. False on a wall cell or a locked line.</summary>
        public static bool TryCursorMove(LevelRules rules, int row, int column, Direction direction, out Move move)
        {
            var axis = AxisOf(direction);
            int line = axis == Axis.Row ? row : column;
            int index = axis == Axis.Row ? column : row;
            int segment = rules.SegmentOf(axis, line, index);
            move = segment >= 0 ? new Move(direction, line, segment) : default;
            return segment >= 0 && rules.CanMove(move);
        }

        /// <summary>
        /// An arrow button at the edge of a line shifts the part next to that edge:
        /// left/up buttons the first part, right/down buttons the last.
        /// </summary>
        public static bool TryEdgeMove(LevelRules rules, int line, Direction direction, out Move move)
        {
            var axis = AxisOf(direction);
            int count = rules.SegmentCount(axis, line);
            if (count == 0)
            {
                move = default;
                return false;
            }

            bool towardStart = direction == Direction.Left || direction == Direction.Up;
            move = new Move(direction, line, towardStart ? 0 : count - 1);
            return rules.CanMove(move);
        }

        /// <summary>First and last cell index (along the line) of a line part.</summary>
        public static (int first, int last) SegmentRange(LevelRules rules, Axis axis, int line, int segment)
        {
            int first = -1, last = -1;
            for (int i = 0; i < BoardBits.Size; i++)
            {
                if (rules.SegmentOf(axis, line, i) != segment)
                {
                    continue;
                }

                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }

            return (first, last);
        }
    }
}
