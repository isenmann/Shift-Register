using System;
using System.Collections.Generic;

namespace ShiftRegister.Core
{
    /// <summary>Everything a view needs to animate one move.</summary>
    public readonly struct MoveOutcome
    {
        public readonly Move Move;
        public readonly ulong Before;
        /// <summary>Board after the shift itself, before gates were applied.</summary>
        public readonly ulong AfterShift;
        public readonly ulong After;
        /// <summary>Cells in <see cref="Before"/> whose bits fell off an edge or into a wall.</summary>
        public readonly ulong LostCells;
        public readonly int LostCount;

        public MoveOutcome(Move move, ulong before, ulong afterShift, ulong after, ulong lostCells)
        {
            Move = move;
            Before = before;
            AfterShift = afterShift;
            After = after;
            LostCells = lostCells;
            LostCount = BoardBits.PopCount(lostCells);
        }
    }

    public sealed class LevelValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public LevelValidationException(string levelId, IReadOnlyList<string> errors)
            : base($"Level '{levelId}' is invalid:\n- " + string.Join("\n- ", errors))
        {
            Errors = errors;
        }
    }

    /// <summary>
    /// A level compiled for fast simulation. Stateless: the board is passed in as a ulong
    /// (see <see cref="BoardBits"/>), so the same rules serve gameplay and the solver.
    /// </summary>
    public sealed class LevelRules
    {
        sealed class Line
        {
            public byte[] Segments;
            public byte[] SegStart;
            public byte[] SegEnd;
            public bool Locked;
            public bool Rotate;
            /// <summary>1-IN edge at index 0 (left / top): shifting toward the end feeds a 1 there.</summary>
            public bool FillStart;
            /// <summary>1-IN edge at index 7 (right / bottom): shifting toward the start feeds a 1 there.</summary>
            public bool FillEnd;
        }

        readonly struct Gate
        {
            public readonly GateOp Op;
            public readonly byte Mask;
            public Gate(GateOp op, byte mask)
            {
                Op = op;
                Mask = mask;
            }
        }

        static readonly Gate[] NoGates = Array.Empty<Gate>();

        readonly Line[] rows = new Line[BoardBits.Size];
        readonly Line[] columns = new Line[BoardBits.Size];
        readonly int[] chainTop = new int[BoardBits.Size];
        readonly Gate[][] gatesByRow = new Gate[BoardBits.Size][];
        readonly byte[] wallRows = new byte[BoardBits.Size];
        readonly byte[] pinRows = new byte[BoardBits.Size];
        readonly bool gatesOnColumnMoves;
        readonly Move[] moves;

        public LevelData Data { get; }
        public ulong Start { get; }
        public ulong Target { get; }
        public ulong Walls { get; }
        public ulong Pins { get; }
        public LossRule LossRule { get; }
        public int LossCount { get; }

        /// <summary>True if any rule can create bits (OR, XOR, NOT, fill edges).</summary>
        public bool HasCreators { get; }
        /// <summary>True if anything other than an edge or wall can remove bits (AND, XOR, NOT).</summary>
        public bool HasDestroyers { get; }

        /// <summary>Every distinct legal move, ignoring the board state. Chains appear once, via their top row.</summary>
        public IReadOnlyList<Move> Moves => moves;

        public static List<string> Validate(LevelData d)
        {
            var errors = new List<string>();
            if (d == null)
            {
                errors.Add("Level data is null.");
                return errors;
            }

            bool RowsOk(int[] values, string field)
            {
                if (values == null || values.Length != BoardBits.Size)
                {
                    errors.Add($"{field} needs exactly {BoardBits.Size} rows.");
                    return false;
                }

                bool ok = true;
                for (int r = 0; r < values.Length; r++)
                {
                    if (values[r] < 0 || values[r] > 255)
                    {
                        errors.Add($"{field}[{r}] = {values[r]} is not a byte.");
                        ok = false;
                    }
                }

                return ok;
            }

            bool RowIndexOk(int r, string field)
            {
                if (r >= 0 && r < BoardBits.Size)
                {
                    return true;
                }

                errors.Add($"{field} has row index {r} outside 0..7.");
                return false;
            }

            bool startOk = RowsOk(d.start, nameof(d.start));
            bool targetOk = RowsOk(d.target, nameof(d.target));
            bool wallsOk = RowsOk(d.walls, nameof(d.walls));
            bool pinsOk = RowsOk(d.pins, nameof(d.pins));
            if (!(startOk && targetOk && wallsOk && pinsOk))
            {
                return errors;
            }

            for (int r = 0; r < BoardBits.Size; r++)
            {
                if ((d.start[r] & d.walls[r]) != 0)
                {
                    errors.Add($"start row {r} has bits on wall cells.");
                }

                if ((d.target[r] & d.walls[r]) != 0)
                {
                    errors.Add($"target row {r} has bits on wall cells.");
                }

                if ((d.pins[r] & d.walls[r]) != 0)
                {
                    errors.Add($"pins row {r} overlap walls.");
                }

                if ((d.pins[r] & ~d.start[r]) != 0)
                {
                    errors.Add($"pins row {r} must also be bits in start.");
                }

                if ((d.pins[r] & ~d.target[r]) != 0)
                {
                    errors.Add($"pins row {r} must also be bits in target.");
                }
            }

            var rotate = new bool[BoardBits.Size];
            foreach (int r in d.rotateRows ?? Array.Empty<int>())
            {
                if (RowIndexOk(r, nameof(d.rotateRows)))
                {
                    rotate[r] = true;
                }
            }

            var fillLeft = new bool[BoardBits.Size];
            foreach (int r in d.fillLeftRows ?? Array.Empty<int>())
            {
                if (!RowIndexOk(r, nameof(d.fillLeftRows)))
                {
                    continue;
                }

                fillLeft[r] = true;
                if ((d.walls[r] & 0x80) != 0)
                {
                    errors.Add($"row {r} has a 1-IN left edge but its leftmost cell is a wall.");
                }

                if (rotate[r])
                {
                    errors.Add($"row {r} cannot both rotate and have a 1-IN edge.");
                }
            }

            var fillRight = new bool[BoardBits.Size];
            foreach (int r in d.fillRightRows ?? Array.Empty<int>())
            {
                if (!RowIndexOk(r, nameof(d.fillRightRows)))
                {
                    continue;
                }

                fillRight[r] = true;
                if ((d.walls[r] & 0x01) != 0)
                {
                    errors.Add($"row {r} has a 1-IN right edge but its rightmost cell is a wall.");
                }

                if (rotate[r])
                {
                    errors.Add($"row {r} cannot both rotate and have a 1-IN edge.");
                }
            }

            var inChain = new bool[BoardBits.Size];
            foreach (int top in d.chainTopRows ?? Array.Empty<int>())
            {
                if (top < 0 || top >= BoardBits.Size - 1)
                {
                    errors.Add($"chainTopRows has {top}; a chain needs rows r and r+1 with r in 0..6.");
                    continue;
                }

                int bottom = top + 1;
                if (inChain[top] || inChain[bottom])
                {
                    errors.Add($"rows {top}/{bottom} are in more than one chain.");
                }

                inChain[top] = inChain[bottom] = true;
                if (d.walls[top] != 0 || d.walls[bottom] != 0)
                {
                    errors.Add($"chained rows {top}/{bottom} cannot have walls.");
                }

                if (rotate[top] != rotate[bottom])
                {
                    errors.Add($"chained rows {top}/{bottom} must both rotate or both not.");
                }

                if (fillRight[top] || fillLeft[bottom])
                {
                    errors.Add($"chained rows {top}/{bottom} have a 1-IN edge in the middle of the chain.");
                }
            }

            foreach (var g in d.gates ?? Array.Empty<RowGate>())
            {
                if (g == null)
                {
                    errors.Add("gates contains a null entry.");
                    continue;
                }

                if (!RowIndexOk(g.row, nameof(d.gates)))
                {
                    continue;
                }

                if (!Enum.IsDefined(typeof(GateOp), g.op))
                {
                    errors.Add($"gate on row {g.row} has unknown op {(int)g.op}.");
                }

                if (g.mask < 0 || g.mask > 255)
                {
                    errors.Add($"gate on row {g.row} has mask {g.mask}, not a byte.");
                }
                else if ((g.mask & d.walls[g.row]) != 0)
                {
                    errors.Add($"gate on row {g.row} covers wall cells.");
                }
            }

            if (d.lossCount < 0)
            {
                errors.Add("lossCount cannot be negative.");
            }

            if (!Enum.IsDefined(typeof(LossRule), d.lossRule))
            {
                errors.Add($"unknown lossRule {(int)d.lossRule}.");
            }

            if (d.par < 0)
            {
                errors.Add("par cannot be negative.");
            }

            return errors;
        }

        public static LevelRules Compile(LevelData data)
        {
            var errors = Validate(data);
            if (errors.Count > 0)
            {
                throw new LevelValidationException(data?.id ?? "?", errors);
            }

            return new LevelRules(data);
        }

        LevelRules(LevelData d)
        {
            Data = d;
            Start = BoardBits.FromRows(d.start);
            Target = BoardBits.FromRows(d.target);
            Walls = BoardBits.FromRows(d.walls);
            Pins = BoardBits.FromRows(d.pins);
            LossRule = d.lossRule;
            LossCount = d.lossCount;
            gatesOnColumnMoves = d.gatesOnColumnMoves;

            var rotate = new bool[BoardBits.Size];
            var fillLeft = new bool[BoardBits.Size];
            var fillRight = new bool[BoardBits.Size];
            foreach (int r in d.rotateRows ?? Array.Empty<int>())
            {
                rotate[r] = true;
            }

            foreach (int r in d.fillLeftRows ?? Array.Empty<int>())
            {
                fillLeft[r] = true;
            }

            foreach (int r in d.fillRightRows ?? Array.Empty<int>())
            {
                fillRight[r] = true;
            }

            for (int i = 0; i < BoardBits.Size; i++)
            {
                wallRows[i] = (byte)d.walls[i];
                pinRows[i] = (byte)d.pins[i];
                chainTop[i] = -1;
            }

            foreach (int top in d.chainTopRows ?? Array.Empty<int>())
            {
                chainTop[top] = chainTop[top + 1] = top;
            }

            for (int i = 0; i < BoardBits.Size; i++)
            {
                rows[i] = BuildLine(wallRows[i], pinRows[i] != 0, rotate[i], fillLeft[i], fillRight[i]);
                columns[i] = BuildLine(BoardBits.GetColumn(Walls, i), BoardBits.GetColumn(Pins, i) != 0, false, false, false);
            }

            bool creators = Array.IndexOf(fillLeft, true) >= 0 || Array.IndexOf(fillRight, true) >= 0;
            bool destroyers = false;
            var perRow = new List<Gate>[BoardBits.Size];
            foreach (var g in d.gates ?? Array.Empty<RowGate>())
            {
                (perRow[g.row] ??= new List<Gate>()).Add(new Gate(g.op, (byte)g.mask));
                switch (g.op)
                {
                    case GateOp.And:
                        destroyers = true;
                        break;
                    case GateOp.Or:
                        creators = true;
                        break;
                    default:
                        creators = destroyers = true;
                        break;
                }
            }

            for (int i = 0; i < BoardBits.Size; i++)
            {
                gatesByRow[i] = perRow[i]?.ToArray() ?? NoGates;
            }

            HasCreators = creators;
            HasDestroyers = destroyers;

            var list = new List<Move>();
            for (int r = 0; r < BoardBits.Size; r++)
            {
                if (chainTop[r] >= 0 && chainTop[r] != r)
                {
                    continue; // a chain is listed once, by its top row
                }

                if (IsRowLocked(r))
                {
                    continue;
                }

                for (int s = 0; s < rows[r].Segments.Length; s++)
                {
                    list.Add(Move.Row(r, Direction.Left, s));
                    list.Add(Move.Row(r, Direction.Right, s));
                }
            }

            for (int c = 0; c < BoardBits.Size; c++)
            {
                if (columns[c].Locked)
                {
                    continue;
                }

                for (int s = 0; s < columns[c].Segments.Length; s++)
                {
                    list.Add(Move.Column(c, Direction.Up, s));
                    list.Add(Move.Column(c, Direction.Down, s));
                }
            }

            moves = list.ToArray();
        }

        static Line BuildLine(byte walls, bool locked, bool rotate, bool fillStart, bool fillEnd)
        {
            var segs = new List<byte>();
            var starts = new List<byte>();
            var ends = new List<byte>();
            int current = 0, first = 0, last = 0;
            for (int i = 0; i < BoardBits.Size; i++)
            {
                int bit = 0x80 >> i;
                if ((walls & bit) != 0)
                {
                    if (current != 0)
                    {
                        segs.Add((byte)current);
                        starts.Add((byte)first);
                        ends.Add((byte)last);
                    }

                    current = 0;
                    continue;
                }

                if (current == 0)
                {
                    first = bit;
                }

                current |= bit;
                last = bit;
            }

            if (current != 0)
            {
                segs.Add((byte)current);
                starts.Add((byte)first);
                ends.Add((byte)last);
            }

            return new Line
            {
                Segments = segs.ToArray(),
                SegStart = starts.ToArray(),
                SegEnd = ends.ToArray(),
                Locked = locked,
                Rotate = rotate,
                FillStart = fillStart,
                FillEnd = fillEnd
            };
        }

        bool IsRowLocked(int r)
        {
            int top = chainTop[r];
            if (top < 0)
            {
                return rows[r].Locked;
            }

            return rows[top].Locked || rows[top + 1].Locked;
        }

        public bool IsWall(int row, int column) => BoardBits.GetCell(Walls, row, column);
        public bool IsPinned(int row, int column) => BoardBits.GetCell(Pins, row, column);
        public bool IsRotating(int row) => rows[row].Rotate;

        /// <summary>The top row of the chain this row belongs to, or -1.</summary>
        public int ChainTopOf(int row) => chainTop[row];

        public int SegmentCount(Axis axis, int line) => (axis == Axis.Row ? rows : columns)[line].Segments.Length;

        /// <summary>Which part of a line a cell (index along the line) belongs to; -1 for a wall cell.</summary>
        public int SegmentOf(Axis axis, int line, int index)
        {
            var l = (axis == Axis.Row ? rows : columns)[line];
            int bit = 0x80 >> index;
            for (int s = 0; s < l.Segments.Length; s++)
            {
                if ((l.Segments[s] & bit) != 0)
                {
                    return s;
                }
            }

            return -1;
        }

        public bool CanMove(Move m)
        {
            if (m.Axis == Axis.Row)
            {
                if (IsRowLocked(m.Line))
                {
                    return false;
                }

                int r = chainTop[m.Line] >= 0 ? chainTop[m.Line] : m.Line;
                return m.Segment < rows[r].Segments.Length;
            }

            var c = columns[m.Line];
            return !c.Locked && m.Segment < c.Segments.Length;
        }

        public bool IsBroken(int lost) => LossRule != LossRule.Any && lost > LossCount;

        public bool IsSolved(ulong board, int lost)
        {
            if (board != Target)
            {
                return false;
            }

            return LossRule == LossRule.Exactly ? lost == LossCount : !IsBroken(lost);
        }

        /// <summary>Applies a move to a board. Returns false (and leaves outcome default) if the move is illegal.</summary>
        public bool TryApply(ulong board, Move m, out MoveOutcome outcome)
        {
            if (!CanMove(m))
            {
                outcome = default;
                return false;
            }

            ulong shifted;
            ulong lostCells;

            if (m.Axis == Axis.Row)
            {
                int top = chainTop[m.Line];
                if (top >= 0)
                {
                    shifted = ShiftChain(board, top, m.TowardStart, out lostCells);
                    ulong gated = ApplyGates(shifted, top);
                    gated = ApplyGates(gated, top + 1);
                    outcome = new MoveOutcome(m, board, shifted, gated, lostCells);
                    return true;
                }

                byte row = BoardBits.GetRow(board, m.Line);
                byte newRow = ShiftLine(row, rows[m.Line], m.Segment, m.TowardStart, out byte lost);
                shifted = BoardBits.SetRow(board, m.Line, newRow);
                lostCells = (ulong)lost << (m.Line * 8);
                outcome = new MoveOutcome(m, board, shifted, ApplyGates(shifted, m.Line), lostCells);
                return true;
            }

            var col = columns[m.Line];
            byte colBits = BoardBits.GetColumn(board, m.Line);
            byte newCol = ShiftLine(colBits, col, m.Segment, m.TowardStart, out byte lostCol);
            shifted = BoardBits.SetColumn(board, m.Line, newCol);
            lostCells = BoardBits.ColumnToBoard(m.Line, lostCol);

            ulong after = shifted;
            if (gatesOnColumnMoves)
            {
                byte seg = col.Segments[m.Segment];
                for (int r = 0; r < BoardBits.Size; r++)
                {
                    if ((seg & (0x80 >> r)) != 0)
                    {
                        after = ApplyGates(after, r);
                    }
                }
            }

            outcome = new MoveOutcome(m, board, shifted, after, lostCells);
            return true;
        }

        static byte ShiftLine(byte v, Line line, int s, bool towardStart, out byte lost)
        {
            int seg = line.Segments[s];
            int inSeg = v & seg;
            int shifted, outBit;
            if (towardStart)
            {
                outBit = inSeg & line.SegStart[s];
                shifted = (inSeg << 1) & seg;
                if (outBit != 0 && line.Rotate)
                {
                    shifted |= line.SegEnd[s];
                }

                if (line.FillEnd && (seg & 0x01) != 0)
                {
                    shifted |= 0x01;
                }
            }
            else
            {
                outBit = inSeg & line.SegEnd[s];
                shifted = (inSeg >> 1) & seg;
                if (outBit != 0 && line.Rotate)
                {
                    shifted |= line.SegStart[s];
                }

                if (line.FillStart && (seg & 0x80) != 0)
                {
                    shifted |= 0x80;
                }
            }

            lost = line.Rotate ? (byte)0 : (byte)outBit;
            return (byte)((v & ~seg) | shifted);
        }

        ulong ShiftChain(ulong board, int top, bool towardStart, out ulong lostCells)
        {
            int v = (BoardBits.GetRow(board, top) << 8) | BoardBits.GetRow(board, top + 1);
            bool rotate = rows[top].Rotate;
            lostCells = 0;
            if (towardStart)
            {
                bool outBit = (v & 0x8000) != 0;
                v = (v << 1) & 0xFFFF;
                if (outBit && rotate)
                {
                    v |= 0x0001;
                }
                else if (outBit)
                {
                    lostCells = BoardBits.CellMask(top, 0);
                }

                if (rows[top + 1].FillEnd)
                {
                    v |= 0x0001;
                }
            }
            else
            {
                bool outBit = (v & 0x0001) != 0;
                v >>= 1;
                if (outBit && rotate)
                {
                    v |= 0x8000;
                }
                else if (outBit)
                {
                    lostCells = BoardBits.CellMask(top + 1, BoardBits.Size - 1);
                }

                if (rows[top].FillStart)
                {
                    v |= 0x8000;
                }
            }

            board = BoardBits.SetRow(board, top, (byte)(v >> 8));
            return BoardBits.SetRow(board, top + 1, (byte)v);
        }

        ulong ApplyGates(ulong board, int r)
        {
            var gates = gatesByRow[r];
            if (gates.Length == 0)
            {
                return board;
            }

            int v = BoardBits.GetRow(board, r);
            foreach (var g in gates)
            {
                switch (g.Op)
                {
                    case GateOp.And:
                        v &= g.Mask;
                        break;
                    case GateOp.Or:
                        v |= g.Mask;
                        break;
                    case GateOp.Xor:
                        v ^= g.Mask;
                        break;
                    case GateOp.Not:
                        if ((v & g.Mask) != 0)
                        {
                            v = ~v;
                        }

                        break;
                }
            }

            v &= ~wallRows[r];
            v |= pinRows[r];
            return BoardBits.SetRow(board, r, (byte)v);
        }
    }
}
