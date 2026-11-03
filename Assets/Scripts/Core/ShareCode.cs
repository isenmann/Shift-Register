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
        public static string Encode(LevelData d)
        {
            if (!CanEncode(d, out string why)) throw new ArgumentException(why, nameof(d));
            Move.TryParseList(d.solution, out var solution);

            var b = new List<byte>();
            int flags = 0;
            if (Any(d.walls)) flags |= FlagWalls;
            if (Any(d.pins)) flags |= FlagPins;
            if (HasRows(d.rotateRows)) flags |= FlagRotate;
            if (HasRows(d.chainTopRows)) flags |= FlagChain;
            if (HasRows(d.fillLeftRows)) flags |= FlagFillLeft;
            if (HasRows(d.fillRightRows)) flags |= FlagFillRight;
            if (d.gates != null && d.gates.Length > 0) flags |= FlagGates;
            if (d.gatesOnColumnMoves) flags |= FlagGatesOnColumns;

            b.Add((byte)flags);
            b.Add((byte)((int)d.lossRule | (d.lossCount << 2)));
            WriteRows(b, d.start);
            WriteRows(b, d.target);
            if ((flags & FlagWalls) != 0) WriteRows(b, d.walls);
            if ((flags & FlagPins) != 0) WriteRows(b, d.pins);
            if ((flags & FlagRotate) != 0) b.Add(RowSet(d.rotateRows));
            if ((flags & FlagChain) != 0) b.Add(RowSet(d.chainTopRows));
            if ((flags & FlagFillLeft) != 0) b.Add(RowSet(d.fillLeftRows));
            if ((flags & FlagFillRight) != 0) b.Add(RowSet(d.fillRightRows));
            if ((flags & FlagGates) != 0)
            {
                b.Add((byte)d.gates.Length);
                foreach (var g in d.gates)
                {
                    b.Add((byte)(g.row | ((int)g.op << 3)));
                    b.Add((byte)g.mask);
                }
            }

            b.Add((byte)solution.Count);
            foreach (var m in solution) b.Add(m.ToByte());

            string name = LevelEdit.SanitizeName(d.name);
            b.Add((byte)name.Length);
            b.AddRange(Encoding.ASCII.GetBytes(name));

            int crc = Crc16(b, b.Count);
            b.Add((byte)(crc >> 8));
            b.Add((byte)crc);
            return Prefix + ToBase64Url(b.ToArray());
        }

        /// <summary>False, with the reason, if this level cannot be shared: invalid, or without a replaying solution.</summary>
        public static bool CanEncode(LevelData d, out string why)
        {
            why = "";
            var errors = LevelEdit.AuthoredErrors(d);
            if (errors.Count > 0)
            {
                why = errors[0];
                return false;
            }
            if (!Move.TryParseList(d.solution, out var solution) || solution.Count > MaxSolutionMoves
                || !Solver.Replays(LevelRules.Compile(d), solution))
            {
                why = "The level has no solution that replays.";
                return false;
            }
            return true;
        }

        /// <summary>Whitespace and line breaks in the text are ignored. The level comes back with no id.</summary>
        public static ShareCodeError TryDecode(string text, out LevelData level)
        {
            level = null;
            string code = RemoveWhitespace(text);
            if (code.Length < 4 || code[0] != 'S' || code[1] != 'R' || code[3] != '-') return ShareCodeError.NotACode;
            if (code[2] != Prefix[2]) return char.IsDigit(code[2]) ? ShareCodeError.UnknownVersion : ShareCodeError.NotACode;

            if (!TryFromBase64Url(code.Substring(Prefix.Length), out var bytes)) return ShareCodeError.NotACode;
            if (bytes.Length < 2) return ShareCodeError.Damaged;
            int stored = (bytes[bytes.Length - 2] << 8) | bytes[bytes.Length - 1];
            if (stored != Crc16(bytes, bytes.Length - 2)) return ShareCodeError.Damaged;

            var d = new LevelData();
            var solution = new List<Move>();
            var reader = new Reader(bytes, bytes.Length - 2);
            try
            {
                int flags = reader.Next();
                int loss = reader.Next();
                d.lossRule = (LossRule)(loss & 3);
                d.lossCount = loss >> 2;
                d.gatesOnColumnMoves = (flags & FlagGatesOnColumns) != 0;
                ReadRows(reader, d.start);
                ReadRows(reader, d.target);
                if ((flags & FlagWalls) != 0) ReadRows(reader, d.walls);
                if ((flags & FlagPins) != 0) ReadRows(reader, d.pins);
                if ((flags & FlagRotate) != 0) d.rotateRows = RowList(reader.Next());
                if ((flags & FlagChain) != 0) d.chainTopRows = RowList(reader.Next());
                if ((flags & FlagFillLeft) != 0) d.fillLeftRows = RowList(reader.Next());
                if ((flags & FlagFillRight) != 0) d.fillRightRows = RowList(reader.Next());
                if ((flags & FlagGates) != 0)
                {
                    int count = reader.Next();
                    if (count > MaxGates) return ShareCodeError.InvalidLevel;
                    d.gates = new RowGate[count];
                    for (int i = 0; i < count; i++)
                    {
                        int rowOp = reader.Next();
                        d.gates[i] = new RowGate(rowOp & 7, (GateOp)(rowOp >> 3), reader.Next());
                    }
                }

                int moves = reader.Next();
                for (int i = 0; i < moves; i++) solution.Add(Move.FromByte(reader.Next()));

                int nameLength = reader.Next();
                var name = new StringBuilder();
                for (int i = 0; i < nameLength; i++) name.Append((char)reader.Next());
                d.name = LevelEdit.SanitizeName(name.ToString());
            }
            catch (IndexOutOfRangeException)
            {
                return ShareCodeError.Damaged;
            }
            if (!reader.AtEnd) return ShareCodeError.Damaged;

            d.par = solution.Count;
            d.solution = string.Join(" ", solution);
            if (LevelEdit.AuthoredErrors(d).Count > 0) return ShareCodeError.InvalidLevel;
            if (!Solver.Replays(LevelRules.Compile(d), solution)) return ShareCodeError.BadSolution;

            level = d;
            return ShareCodeError.None;
        }

        sealed class Reader
        {
            readonly byte[] bytes;
            readonly int end;
            int at;
            public Reader(byte[] bytes, int end) { this.bytes = bytes; this.end = end; }
            public bool AtEnd => at == end;
            public byte Next()
            {
                if (at >= end) throw new IndexOutOfRangeException();
                return bytes[at++];
            }
        }

        static void WriteRows(List<byte> b, int[] rows)
        {
            int present = 0;
            for (int r = 0; r < BoardBits.Size; r++) if (rows[r] != 0) present |= 1 << r;
            b.Add((byte)present);
            for (int r = 0; r < BoardBits.Size; r++) if (rows[r] != 0) b.Add((byte)rows[r]);
        }

        static void ReadRows(Reader reader, int[] rows)
        {
            int present = reader.Next();
            for (int r = 0; r < BoardBits.Size; r++) rows[r] = (present & (1 << r)) != 0 ? reader.Next() : 0;
        }

        static byte RowSet(int[] rows)
        {
            int set = 0;
            foreach (int r in rows) set |= 1 << r;
            return (byte)set;
        }

        static int[] RowList(int set)
        {
            var rows = new List<int>();
            for (int r = 0; r < BoardBits.Size; r++) if ((set & (1 << r)) != 0) rows.Add(r);
            return rows.ToArray();
        }

        static bool Any(int[] rows)
        {
            foreach (int v in rows) if (v != 0) return true;
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
                    crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
            return crc;
        }

        static string RemoveWhitespace(string text)
        {
            var kept = new StringBuilder();
            foreach (char ch in text ?? "") if (!char.IsWhiteSpace(ch)) kept.Append(ch);
            return kept.ToString();
        }

        static string ToBase64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static bool TryFromBase64Url(string text, out byte[] bytes)
        {
            bytes = null;
            if (text.Length % 4 == 1) return false;
            string padded = text.Replace('-', '+').Replace('_', '/').PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
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
    }
}
