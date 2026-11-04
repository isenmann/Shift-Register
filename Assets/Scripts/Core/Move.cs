using System;
using System.Collections.Generic;

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
            if (line < 0 || line >= BoardBits.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(line));
            }

            if (segment < 0 || segment >= BoardBits.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(segment));
            }

            Direction = direction;
            Line = (byte)line;
            Segment = (byte)segment;
        }

        public static Move Row(int row, Direction direction, int segment = 0)
        {
            if (direction != Direction.Left && direction != Direction.Right)
            {
                throw new ArgumentException("Rows shift left or right.", nameof(direction));
            }

            return new Move(direction, row, segment);
        }

        public static Move Column(int column, Direction direction, int segment = 0)
        {
            if (direction != Direction.Up && direction != Direction.Down)
            {
                throw new ArgumentException("Columns shift up or down.", nameof(direction));
            }

            return new Move(direction, column, segment);
        }

        public Axis Axis => Direction == Direction.Left || Direction == Direction.Right ? Axis.Row : Axis.Column;

        /// <summary>Left and Up move bits toward index 0 of the line.</summary>
        public bool TowardStart => Direction == Direction.Left || Direction == Direction.Up;

        /// <summary>Packs the move into one byte: direction (2 bits), line (3 bits), part (3 bits).</summary>
        public byte ToByte() => (byte)(((int)Direction << 6) | (Line << 3) | Segment);

        public static Move FromByte(byte value) =>
            new Move((Direction)(value >> 6), (value >> 3) & 7, value & 7);

        /// <summary>
        /// Reads a space-separated list in the notation of <see cref="ToString"/>, e.g. "R5← C4↑ R2.2→".
        /// </summary>
        public static bool TryParseList(string text, out List<Move> moves)
        {
            moves = new List<Move>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            foreach (string token in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParse(token, out var move))
                {
                    return false;
                }

                moves.Add(move);
            }

            return true;
        }

        static bool TryParse(string text, out Move move)
        {
            move = default;
            if (text.Length < 3)
            {
                return false;
            }

            bool isRow = text[0] == 'R';
            if (!isRow && text[0] != 'C')
            {
                return false;
            }

            Direction direction;
            switch (text[text.Length - 1])
            {
                case '←':
                    direction = Direction.Left;
                    break;
                case '→':
                    direction = Direction.Right;
                    break;
                case '↑':
                    direction = Direction.Up;
                    break;
                case '↓':
                    direction = Direction.Down;
                    break;
                default:
                    return false;
            }

            bool rowDirection = direction == Direction.Left || direction == Direction.Right;
            if (rowDirection != isRow)
            {
                return false;
            }

            int line = text[1] - '1';
            if (line < 0 || line >= BoardBits.Size)
            {
                return false;
            }

            int segment = 0;
            if (text.Length == 5 && text[2] == '.')
            {
                segment = text[3] - '1';
            }
            else if (text.Length != 3)
            {
                return false;
            }

            if (segment < 0 || segment >= BoardBits.Size)
            {
                return false;
            }

            move = new Move(direction, line, segment);
            return true;
        }

        public bool Equals(Move other) =>
            Direction == other.Direction && Line == other.Line && Segment == other.Segment;
        public override bool Equals(object obj) => obj is Move move && Equals(move);
        public override int GetHashCode() => ((int)Direction << 16) | (Line << 8) | Segment;
        public static bool operator ==(Move left, Move right) => left.Equals(right);
        public static bool operator !=(Move left, Move right) => !left.Equals(right);

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
