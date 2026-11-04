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

        public static List<string> Validate(LevelData level)
        {
            var errors = new List<string>();
            if (level == null)
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
                for (int row = 0; row < values.Length; row++)
                {
                    if (values[row] < 0 || values[row] > 255)
                    {
                        errors.Add($"{field}[{row}] = {values[row]} is not a byte.");
                        ok = false;
                    }
                }

                return ok;
            }

            bool RowIndexOk(int row, string field)
            {
                if (row >= 0 && row < BoardBits.Size)
                {
                    return true;
                }

                errors.Add($"{field} has row index {row} outside 0..7.");
                return false;
            }

            bool startOk = RowsOk(level.start, nameof(level.start));
            bool targetOk = RowsOk(level.target, nameof(level.target));
            bool wallsOk = RowsOk(level.walls, nameof(level.walls));
            bool pinsOk = RowsOk(level.pins, nameof(level.pins));
            if (!(startOk && targetOk && wallsOk && pinsOk))
            {
                return errors;
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                if ((level.start[row] & level.walls[row]) != 0)
                {
                    errors.Add($"start row {row} has bits on wall cells.");
                }

                if ((level.target[row] & level.walls[row]) != 0)
                {
                    errors.Add($"target row {row} has bits on wall cells.");
                }

                if ((level.pins[row] & level.walls[row]) != 0)
                {
                    errors.Add($"pins row {row} overlap walls.");
                }

                if ((level.pins[row] & ~level.start[row]) != 0)
                {
                    errors.Add($"pins row {row} must also be bits in start.");
                }

                if ((level.pins[row] & ~level.target[row]) != 0)
                {
                    errors.Add($"pins row {row} must also be bits in target.");
                }
            }

            var rotate = new bool[BoardBits.Size];
            foreach (int row in level.rotateRows ?? Array.Empty<int>())
            {
                if (RowIndexOk(row, nameof(level.rotateRows)))
                {
                    rotate[row] = true;
                }
            }

            var fillLeft = new bool[BoardBits.Size];
            foreach (int row in level.fillLeftRows ?? Array.Empty<int>())
            {
                if (!RowIndexOk(row, nameof(level.fillLeftRows)))
                {
                    continue;
                }

                fillLeft[row] = true;
                if ((level.walls[row] & 0x80) != 0)
                {
                    errors.Add($"row {row} has a 1-IN left edge but its leftmost cell is a wall.");
                }

                if (rotate[row])
                {
                    errors.Add($"row {row} cannot both rotate and have a 1-IN edge.");
                }
            }

            var fillRight = new bool[BoardBits.Size];
            foreach (int row in level.fillRightRows ?? Array.Empty<int>())
            {
                if (!RowIndexOk(row, nameof(level.fillRightRows)))
                {
                    continue;
                }

                fillRight[row] = true;
                if ((level.walls[row] & 0x01) != 0)
                {
                    errors.Add($"row {row} has a 1-IN right edge but its rightmost cell is a wall.");
                }

                if (rotate[row])
                {
                    errors.Add($"row {row} cannot both rotate and have a 1-IN edge.");
                }
            }

            var inChain = new bool[BoardBits.Size];
            foreach (int top in level.chainTopRows ?? Array.Empty<int>())
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
                if (level.walls[top] != 0 || level.walls[bottom] != 0)
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

            foreach (var gate in level.gates ?? Array.Empty<RowGate>())
            {
                if (gate == null)
                {
                    errors.Add("gates contains a null entry.");
                    continue;
                }

                if (!RowIndexOk(gate.row, nameof(level.gates)))
                {
                    continue;
                }

                if (!Enum.IsDefined(typeof(GateOp), gate.op))
                {
                    errors.Add($"gate on row {gate.row} has unknown op {(int)gate.op}.");
                }

                if (gate.mask < 0 || gate.mask > 255)
                {
                    errors.Add($"gate on row {gate.row} has mask {gate.mask}, not a byte.");
                }
                else if ((gate.mask & level.walls[gate.row]) != 0)
                {
                    errors.Add($"gate on row {gate.row} covers wall cells.");
                }
            }

            if (level.lossCount < 0)
            {
                errors.Add("lossCount cannot be negative.");
            }

            if (!Enum.IsDefined(typeof(LossRule), level.lossRule))
            {
                errors.Add($"unknown lossRule {(int)level.lossRule}.");
            }

            if (level.par < 0)
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

        LevelRules(LevelData level)
        {
            Data = level;
            Start = BoardBits.FromRows(level.start);
            Target = BoardBits.FromRows(level.target);
            Walls = BoardBits.FromRows(level.walls);
            Pins = BoardBits.FromRows(level.pins);
            LossRule = level.lossRule;
            LossCount = level.lossCount;
            gatesOnColumnMoves = level.gatesOnColumnMoves;

            var rotate = new bool[BoardBits.Size];
            var fillLeft = new bool[BoardBits.Size];
            var fillRight = new bool[BoardBits.Size];
            foreach (int row in level.rotateRows ?? Array.Empty<int>())
            {
                rotate[row] = true;
            }

            foreach (int row in level.fillLeftRows ?? Array.Empty<int>())
            {
                fillLeft[row] = true;
            }

            foreach (int row in level.fillRightRows ?? Array.Empty<int>())
            {
                fillRight[row] = true;
            }

            for (int i = 0; i < BoardBits.Size; i++)
            {
                wallRows[i] = (byte)level.walls[i];
                pinRows[i] = (byte)level.pins[i];
                chainTop[i] = -1;
            }

            foreach (int top in level.chainTopRows ?? Array.Empty<int>())
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
            foreach (var gate in level.gates ?? Array.Empty<RowGate>())
            {
                (perRow[gate.row] ??= new List<Gate>()).Add(new Gate(gate.op, (byte)gate.mask));
                switch (gate.op)
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
            for (int row = 0; row < BoardBits.Size; row++)
            {
                if (chainTop[row] >= 0 && chainTop[row] != row)
                {
                    continue; // a chain is listed once, by its top row
                }

                if (IsRowLocked(row))
                {
                    continue;
                }

                for (int segment = 0; segment < rows[row].Segments.Length; segment++)
                {
                    list.Add(Move.Row(row, Direction.Left, segment));
                    list.Add(Move.Row(row, Direction.Right, segment));
                }
            }

            for (int column = 0; column < BoardBits.Size; column++)
            {
                if (columns[column].Locked)
                {
                    continue;
                }

                for (int segment = 0; segment < columns[column].Segments.Length; segment++)
                {
                    list.Add(Move.Column(column, Direction.Up, segment));
                    list.Add(Move.Column(column, Direction.Down, segment));
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

        bool IsRowLocked(int row)
        {
            int top = chainTop[row];
            if (top < 0)
            {
                return rows[row].Locked;
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
            var lineInfo = (axis == Axis.Row ? rows : columns)[line];
            int bit = 0x80 >> index;
            for (int segment = 0; segment < lineInfo.Segments.Length; segment++)
            {
                if ((lineInfo.Segments[segment] & bit) != 0)
                {
                    return segment;
                }
            }

            return -1;
        }

        public bool CanMove(Move move)
        {
            if (move.Axis == Axis.Row)
            {
                if (IsRowLocked(move.Line))
                {
                    return false;
                }

                int row = chainTop[move.Line] >= 0 ? chainTop[move.Line] : move.Line;
                return move.Segment < rows[row].Segments.Length;
            }

            var line = columns[move.Line];
            return !line.Locked && move.Segment < line.Segments.Length;
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
        public bool TryApply(ulong board, Move move, out MoveOutcome outcome)
        {
            if (!CanMove(move))
            {
                outcome = default;
                return false;
            }

            ulong shifted;
            ulong lostCells;

            if (move.Axis == Axis.Row)
            {
                int top = chainTop[move.Line];
                if (top >= 0)
                {
                    shifted = ShiftChain(board, top, move.TowardStart, out lostCells);
                    ulong gated = ApplyGates(shifted, top);
                    gated = ApplyGates(gated, top + 1);
                    outcome = new MoveOutcome(move, board, shifted, gated, lostCells);
                    return true;
                }

                byte row = BoardBits.GetRow(board, move.Line);
                byte newRow = ShiftLine(row, rows[move.Line], move.Segment, move.TowardStart, out byte lost);
                shifted = BoardBits.SetRow(board, move.Line, newRow);
                lostCells = (ulong)lost << (move.Line * 8);
                outcome = new MoveOutcome(move, board, shifted, ApplyGates(shifted, move.Line), lostCells);
                return true;
            }

            var columnLine = columns[move.Line];
            byte colBits = BoardBits.GetColumn(board, move.Line);
            byte newCol = ShiftLine(colBits, columnLine, move.Segment, move.TowardStart, out byte lostCol);
            shifted = BoardBits.SetColumn(board, move.Line, newCol);
            lostCells = BoardBits.ColumnToBoard(move.Line, lostCol);

            ulong after = shifted;
            if (gatesOnColumnMoves)
            {
                byte segment = columnLine.Segments[move.Segment];
                for (int row = 0; row < BoardBits.Size; row++)
                {
                    if ((segment & (0x80 >> row)) != 0)
                    {
                        after = ApplyGates(after, row);
                    }
                }
            }

            outcome = new MoveOutcome(move, board, shifted, after, lostCells);
            return true;
        }

        static byte ShiftLine(byte value, Line line, int segment, bool towardStart, out byte lost)
        {
            int seg = line.Segments[segment];
            int inSeg = value & seg;
            int shifted, outBit;
            if (towardStart)
            {
                outBit = inSeg & line.SegStart[segment];
                shifted = (inSeg << 1) & seg;
                if (outBit != 0 && line.Rotate)
                {
                    shifted |= line.SegEnd[segment];
                }

                if (line.FillEnd && (seg & 0x01) != 0)
                {
                    shifted |= 0x01;
                }
            }
            else
            {
                outBit = inSeg & line.SegEnd[segment];
                shifted = (inSeg >> 1) & seg;
                if (outBit != 0 && line.Rotate)
                {
                    shifted |= line.SegStart[segment];
                }

                if (line.FillStart && (seg & 0x80) != 0)
                {
                    shifted |= 0x80;
                }
            }

            lost = line.Rotate ? (byte)0 : (byte)outBit;
            return (byte)((value & ~seg) | shifted);
        }

        ulong ShiftChain(ulong board, int top, bool towardStart, out ulong lostCells)
        {
            int value = (BoardBits.GetRow(board, top) << 8) | BoardBits.GetRow(board, top + 1);
            bool rotate = rows[top].Rotate;
            lostCells = 0;
            if (towardStart)
            {
                bool outBit = (value & 0x8000) != 0;
                value = (value << 1) & 0xFFFF;
                if (outBit && rotate)
                {
                    value |= 0x0001;
                }
                else if (outBit)
                {
                    lostCells = BoardBits.CellMask(top, 0);
                }

                if (rows[top + 1].FillEnd)
                {
                    value |= 0x0001;
                }
            }
            else
            {
                bool outBit = (value & 0x0001) != 0;
                value >>= 1;
                if (outBit && rotate)
                {
                    value |= 0x8000;
                }
                else if (outBit)
                {
                    lostCells = BoardBits.CellMask(top + 1, BoardBits.Size - 1);
                }

                if (rows[top].FillStart)
                {
                    value |= 0x8000;
                }
            }

            board = BoardBits.SetRow(board, top, (byte)(value >> 8));
            return BoardBits.SetRow(board, top + 1, (byte)value);
        }

        ulong ApplyGates(ulong board, int row)
        {
            var gates = gatesByRow[row];
            if (gates.Length == 0)
            {
                return board;
            }

            int value = BoardBits.GetRow(board, row);
            foreach (var gate in gates)
            {
                switch (gate.Op)
                {
                    case GateOp.And:
                        value &= gate.Mask;
                        break;
                    case GateOp.Or:
                        value |= gate.Mask;
                        break;
                    case GateOp.Xor:
                        value ^= gate.Mask;
                        break;
                    case GateOp.Not:
                        if ((value & gate.Mask) != 0)
                        {
                            value = ~value;
                        }

                        break;
                }
            }

            value &= ~wallRows[row];
            value |= pinRows[row];
            return BoardBits.SetRow(board, row, (byte)value);
        }
    }
}
