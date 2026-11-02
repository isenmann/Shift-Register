using System;

namespace ShiftRegister.Core
{
    public enum Axis : byte { Row, Column }

    public enum Direction : byte { Left, Right, Up, Down }

    /// <summary>
    /// One shift of one line part. Lines without walls have a single part (segment 0);
    /// walls split a line into parts that shift on their own, numbered from the start
    /// (left for rows, top for columns).
    /// </summary>
    public readonly struct Move : IEquatable<Move>
    {
        public readonly Direction Direction;
        public readonly byte Line;
        public readonly byte Segment;

        public Move(Direction direction, int line, int segment = 0)
        {
            if (line < 0 || line >= BoardBits.Size) throw new ArgumentOutOfRangeException(nameof(line));
            if (segment < 0 || segment >= BoardBits.Size) throw new ArgumentOutOfRangeException(nameof(segment));
            Direction = direction;
            Line = (byte)line;
            Segment = (byte)segment;
        }

        public static Move Row(int row, Direction direction, int segment = 0)
        {
            if (direction != Direction.Left && direction != Direction.Right)
                throw new ArgumentException("Rows shift left or right.", nameof(direction));
            return new Move(direction, row, segment);
        }

        public static Move Column(int column, Direction direction, int segment = 0)
        {
            if (direction != Direction.Up && direction != Direction.Down)
                throw new ArgumentException("Columns shift up or down.", nameof(direction));
            return new Move(direction, column, segment);
        }

        public Axis Axis => Direction == Direction.Left || Direction == Direction.Right ? Axis.Row : Axis.Column;

        /// <summary>Left and Up move bits toward index 0 of the line.</summary>
        public bool TowardStart => Direction == Direction.Left || Direction == Direction.Up;

        public bool Equals(Move other) => Direction == other.Direction && Line == other.Line && Segment == other.Segment;
        public override bool Equals(object obj) => obj is Move m && Equals(m);
        public override int GetHashCode() => ((int)Direction << 16) | (Line << 8) | Segment;
        public static bool operator ==(Move a, Move b) => a.Equals(b);
        public static bool operator !=(Move a, Move b) => !a.Equals(b);

        /// <summary>Player-facing notation with 1-based lines, e.g. "R5←", "C4↑", "R2.2→" (part 2 of row 2).</summary>
        public override string ToString()
        {
            char arrow = Direction switch
            {
                Direction.Left => '←',
                Direction.Right => '→',
                Direction.Up => '↑',
                _ => '↓'
            };
            string part = Segment > 0 ? "." + (Segment + 1) : "";
            return (Axis == Axis.Row ? "R" : "C") + (Line + 1) + part + arrow;
        }
    }
}
