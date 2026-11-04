using System;
using System.Collections.Generic;
using System.Text;

namespace ShiftRegister.Core
{
    public enum ShareCodeError
    {
        None,
        /// <summary>Not a share code: wrong prefix or not base64url.</summary>
        NotACode,
        /// <summary>Made by a newer version of the game.</summary>
        UnknownVersion,
        /// <summary>A typo or a cut-off paste.</summary>
        Damaged,
        /// <summary>The level breaks the rules.</summary>
        InvalidLevel,
        /// <summary>The solution in the code does not solve the level.</summary>
        BadSolution
    }

    /// <summary>
    /// Levels as short text: "SR1-" plus base64url bytes; the prefix carries the format version.
    /// Nothing in a code is trusted. Decoding checks the rules and replays the solution, so a level
    /// that decodes is proven solvable without a search, and par is simply the solution's length.
    /// </summary>
    public static class ShareCode
    {
        public const string Prefix = "SR1-";
        public const int MaxSolutionMoves = 255;

        const int FlagWalls = 1, FlagPins = 2, FlagRotate = 4, FlagChain = 8;
        const int FlagFillLeft = 16, FlagFillRight = 32, FlagGates = 64, FlagGatesOnColumns = 128;
        const int MaxGates = BoardBits.Size;

        /// <summary>Encodes a proven level. The caller must have checked it (see <see cref="CanEncode"/>).</summary>
        /// <exception cref="ArgumentException">The level is not valid or has no replaying solution.</exception>
        public static string Encode(LevelData level)
        {
            if (!CanEncode(level, out string reason))
            {
                throw new ArgumentException(reason, nameof(level));
            }

            Move.TryParseList(level.solution, out var solution);

            var bytes = new List<byte>();
            int flags = 0;
            if (Any(level.walls))
            {
                flags |= FlagWalls;
            }

            if (Any(level.pins))
            {
                flags |= FlagPins;
            }

            if (HasRows(level.rotateRows))
            {
                flags |= FlagRotate;
            }

            if (HasRows(level.chainTopRows))
            {
                flags |= FlagChain;
            }

            if (HasRows(level.fillLeftRows))
            {
                flags |= FlagFillLeft;
            }

            if (HasRows(level.fillRightRows))
            {
                flags |= FlagFillRight;
            }

            if (level.gates != null && level.gates.Length > 0)
            {
                flags |= FlagGates;
            }

            if (level.gatesOnColumnMoves)
            {
                flags |= FlagGatesOnColumns;
            }

            bytes.Add((byte)flags);
            bytes.Add((byte)((int)level.lossRule | (level.lossCount << 2)));
            WriteRows(bytes, level.start);
            WriteRows(bytes, level.target);
            if ((flags & FlagWalls) != 0)
            {
                WriteRows(bytes, level.walls);
            }

            if ((flags & FlagPins) != 0)
            {
                WriteRows(bytes, level.pins);
            }

            if ((flags & FlagRotate) != 0)
            {
                bytes.Add(RowSet(level.rotateRows));
            }

            if ((flags & FlagChain) != 0)
            {
                bytes.Add(RowSet(level.chainTopRows));
            }

            if ((flags & FlagFillLeft) != 0)
            {
                bytes.Add(RowSet(level.fillLeftRows));
            }

            if ((flags & FlagFillRight) != 0)
            {
                bytes.Add(RowSet(level.fillRightRows));
            }

            if ((flags & FlagGates) != 0)
            {
                bytes.Add((byte)level.gates.Length);
                foreach (var gate in level.gates)
                {
                    bytes.Add((byte)(gate.row | ((int)gate.op << 3)));
                    bytes.Add((byte)gate.mask);
                }
            }

            bytes.Add((byte)solution.Count);
            foreach (var move in solution)
            {
                bytes.Add(move.ToByte());
            }

            string name = LevelEdit.SanitizeName(level.name);
            bytes.Add((byte)name.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(name));

            int crc = Crc16(bytes, bytes.Count);
            bytes.Add((byte)(crc >> 8));
            bytes.Add((byte)crc);
            return Prefix + ToBase64Url(bytes.ToArray());
        }

        /// <summary>
        /// False, with the reason, if this level cannot be shared: invalid, or without a replaying solution.
        /// </summary>
        public static bool CanEncode(LevelData level, out string reason)
        {
            reason = "";
            var errors = LevelEdit.AuthoredErrors(level);
            if (errors.Count > 0)
            {
                reason = errors[0];
                return false;
            }

            if (!Move.TryParseList(level.solution, out var solution) || solution.Count > MaxSolutionMoves
                || !Solver.Replays(LevelRules.Compile(level), solution))
            {
                reason = "The level has no solution that replays.";
                return false;
            }

            return true;
        }

        /// <summary>Whitespace and line breaks in the text are ignored. The level comes back with no id.</summary>
        public static ShareCodeError TryDecode(string text, out LevelData level)
        {
            level = null;
            string code = RemoveWhitespace(text);
            if (code.Length < 4 || code[0] != 'S' || code[1] != 'R' || code[3] != '-')
            {
                return ShareCodeError.NotACode;
            }

            if (code[2] != Prefix[2])
            {
                return char.IsDigit(code[2]) ? ShareCodeError.UnknownVersion : ShareCodeError.NotACode;
            }

            if (!TryFromBase64Url(code.Substring(Prefix.Length), out var bytes))
            {
                return ShareCodeError.NotACode;
            }

            if (bytes.Length < 2)
            {
                return ShareCodeError.Damaged;
            }

            int stored = (bytes[bytes.Length - 2] << 8) | bytes[bytes.Length - 1];
            if (stored != Crc16(bytes, bytes.Length - 2))
            {
                return ShareCodeError.Damaged;
            }

            var decoded = new LevelData();
            var solution = new List<Move>();
            var reader = new Reader(bytes, bytes.Length - 2);
            try
            {
                int flags = reader.Next();
                int loss = reader.Next();
                decoded.lossRule = (LossRule)(loss & 3);
                decoded.lossCount = loss >> 2;
                decoded.gatesOnColumnMoves = (flags & FlagGatesOnColumns) != 0;
                ReadRows(reader, decoded.start);
                ReadRows(reader, decoded.target);
                if ((flags & FlagWalls) != 0)
                {
                    ReadRows(reader, decoded.walls);
                }

                if ((flags & FlagPins) != 0)
                {
                    ReadRows(reader, decoded.pins);
                }

                if ((flags & FlagRotate) != 0)
                {
                    decoded.rotateRows = RowList(reader.Next());
                }

                if ((flags & FlagChain) != 0)
                {
                    decoded.chainTopRows = RowList(reader.Next());
                }

                if ((flags & FlagFillLeft) != 0)
                {
                    decoded.fillLeftRows = RowList(reader.Next());
                }

                if ((flags & FlagFillRight) != 0)
                {
                    decoded.fillRightRows = RowList(reader.Next());
                }

                if ((flags & FlagGates) != 0)
                {
                    int count = reader.Next();
                    if (count > MaxGates)
                    {
                        return ShareCodeError.InvalidLevel;
                    }

                    decoded.gates = new RowGate[count];
                    for (int i = 0; i < count; i++)
                    {
                        int rowOp = reader.Next();
                        decoded.gates[i] = new RowGate(rowOp & 7, (GateOp)(rowOp >> 3), reader.Next());
                    }
                }

                int moves = reader.Next();
                for (int i = 0; i < moves; i++)
                {
                    solution.Add(Move.FromByte(reader.Next()));
                }

                int nameLength = reader.Next();
                var name = new StringBuilder();
                for (int i = 0; i < nameLength; i++)
                {
                    name.Append((char)reader.Next());
                }

                decoded.name = LevelEdit.SanitizeName(name.ToString());
            }
            catch (IndexOutOfRangeException)
            {
                return ShareCodeError.Damaged;
            }

            if (!reader.AtEnd)
            {
                return ShareCodeError.Damaged;
            }

            decoded.par = solution.Count;
            decoded.solution = string.Join(" ", solution);
            if (LevelEdit.AuthoredErrors(decoded).Count > 0)
            {
                return ShareCodeError.InvalidLevel;
            }

            if (!Solver.Replays(LevelRules.Compile(decoded), solution))
            {
                return ShareCodeError.BadSolution;
            }

            level = decoded;
            return ShareCodeError.None;
        }

        static void WriteRows(List<byte> bytes, int[] rows)
        {
            int present = 0;
            for (int row = 0; row < BoardBits.Size; row++)
            {
                if (rows[row] != 0)
                {
                    present |= 1 << row;
                }
            }

            bytes.Add((byte)present);
            for (int row = 0; row < BoardBits.Size; row++)
            {
                if (rows[row] != 0)
                {
                    bytes.Add((byte)rows[row]);
                }
            }
        }

        static void ReadRows(Reader reader, int[] rows)
        {
            int present = reader.Next();
            for (int row = 0; row < BoardBits.Size; row++)
            {
                rows[row] = (present & (1 << row)) != 0 ? reader.Next() : 0;
            }
        }

        static byte RowSet(int[] rows)
        {
            int rowBits = 0;
            foreach (int row in rows)
            {
                rowBits |= 1 << row;
            }

            return (byte)rowBits;
        }

        static int[] RowList(int rowBits)
        {
            var rows = new List<int>();
            for (int row = 0; row < BoardBits.Size; row++)
            {
                if ((rowBits & (1 << row)) != 0)
                {
                    rows.Add(row);
                }
            }

            return rows.ToArray();
        }

        static bool Any(int[] rows)
        {
            foreach (int row in rows)
            {
                if (row != 0)
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasRows(int[] rows) => rows != null && rows.Length > 0;

        /// <summary>CRC-16/CCITT-FALSE.</summary>
        static int Crc16(IReadOnlyList<byte> data, int count)
        {
            int crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[i] << 8;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
                }
            }

            return crc;
        }

        static string RemoveWhitespace(string text)
        {
            var kept = new StringBuilder();
            foreach (char character in text ?? "")
            {
                if (!char.IsWhiteSpace(character))
                {
                    kept.Append(character);
                }
            }

            return kept.ToString();
        }

        static string ToBase64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static bool TryFromBase64Url(string text, out byte[] bytes)
        {
            bytes = null;
            if (text.Length % 4 == 1)
            {
                return false;
            }

            string padded = text.Replace('-', '+').Replace('_', '/')
                .PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
            try
            {
                bytes = Convert.FromBase64String(padded);
                // The last character can hide unused bits; only the one spelling of these bytes counts.
                return ToBase64Url(bytes) == text;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        sealed class Reader
        {
            readonly byte[] bytes;
            readonly int end;
            int position;

            public bool AtEnd => position == end;

            public Reader(byte[] bytes, int end)
            {
                this.bytes = bytes;
                this.end = end;
            }

            public byte Next()
            {
                if (position >= end)
                {
                    throw new IndexOutOfRangeException();
                }

                return bytes[position++];
            }
        }
    }
}
