using System;
using System.Collections.Generic;

namespace ShiftRegister.Core
{
    /// <summary>The two pictures a level is painted on.</summary>
    public enum EditLayer { Start, Target }

    /// <summary>How a level was proven solvable.</summary>
    public enum LevelProof
    {
        /// <summary>Not proven; it cannot be played as a finished level or shared.</summary>
        None = 0,
        /// <summary>The solver searched it, so par is the true minimum.</summary>
        Searched = 1,
        /// <summary>A recorded solution replays, so par is an upper bound: the author's best.</summary>
        Replayed = 2
    }

    public enum LevelProblem
    {
        /// <summary>Start and target are the same picture, so there is nothing to solve.</summary>
        SameStartAndTarget,
        /// <summary>Pins and walls leave no legal move.</summary>
        NoMoves
    }

    /// <summary>
    /// A level being edited. Every edit leaves the level valid, so it can always be compiled and drawn:
    /// the last action wins and takes back whatever it conflicts with (a wall clears the bits, pin and
    /// gate cell under it and breaks a chain on its row). Any real change clears par and solution.
    /// Also holds undo and redo, as whole-level snapshots.
    /// </summary>
    public sealed class LevelEdit
    {
        public const int MaxNameLength = 16;
        /// <summary>The loss count has 6 bits in a share code.</summary>
        public const int MaxLossCount = 63;
        const int MaxHistory = 200;
        const int Last = BoardBits.Size - 1;

        readonly List<LevelData> undo = new List<LevelData>();
        readonly List<LevelData> redo = new List<LevelData>();
        LevelData strokeBefore;

        /// <summary>The level as it stands. Treat it as read-only; change it through this class.</summary>
        public LevelData Level { get; private set; }

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        /// <summary>Raised after every real change, undo and redo included.</summary>
        public event Action Changed;

        /// <param name="level">Copied, not kept. Must pass <see cref="AuthoredErrors"/>.</param>
        public LevelEdit(LevelData level)
        {
            var errors = AuthoredErrors(level);
            if (errors.Count > 0) throw new LevelValidationException(level?.id ?? "?", errors);
            Level = Clone(level);
        }

        /// <summary>A new level with an empty board.</summary>
        public static LevelEdit Blank(string name) => new LevelEdit(new LevelData { name = SanitizeName(name) });

        // ----- Strokes and history -----

        /// <summary>Groups the edits until <see cref="EndStroke"/> into one undo step (one mouse drag).</summary>
        public void BeginStroke()
        {
            if (strokeBefore == null) strokeBefore = Clone(Level);
        }

        public void EndStroke()
        {
            if (strokeBefore == null) return;
            var before = strokeBefore;
            strokeBefore = null;
            if (!SameLevel(before, Level)) Remember(before);
        }

        public bool Undo() => Swap(undo, redo);
        public bool Redo() => Swap(redo, undo);

        bool Swap(List<LevelData> from, List<LevelData> to)
        {
            EndStroke();
            if (from.Count == 0) return false;
            to.Add(Level);
            Level = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Changed?.Invoke();
            return true;
        }

        void Remember(LevelData before)
        {
            undo.Add(before);
            if (undo.Count > MaxHistory) undo.RemoveAt(0);
            redo.Clear();
        }

        void Edit(Action change)
        {
            var before = Clone(Level);
            change();
            if (SameLevel(before, Level)) return;

            Level.par = 0;
            Level.solution = "";
            if (strokeBefore == null) Remember(before);
            Changed?.Invoke();
        }

        /// <summary>
        /// Stores a proof's result. The level's rules do not change, so this is not an undo step, but
        /// history is kept: undoing to an older level brings back that level's own par (none).
        /// </summary>
        public void SetSolution(IReadOnlyList<Move> solution)
        {
            Level.par = solution.Count;
            Level.solution = string.Join(" ", solution);
            Changed?.Invoke();
        }

        // ----- Cells -----

        public bool GetBit(EditLayer layer, int row, int column) => (Rows(layer)[row] & Mask(column)) != 0;

        public void SetBit(EditLayer layer, int row, int column, bool on) => Edit(() =>
        {
            int m = Mask(column);
            if (on)
            {
                if ((Level.walls[row] & m) == 0) Rows(layer)[row] |= m;
                return;
            }
            Rows(layer)[row] &= ~m;
            Level.pins[row] &= ~m; // a pin has to be on in both pictures
        });

        public void SetWall(int row, int column, bool on) => Edit(() =>
        {
            int m = Mask(column);
            if (!on)
            {
                Level.walls[row] &= ~m;
                return;
            }
            Level.walls[row] |= m;
            Level.start[row] &= ~m;
            Level.target[row] &= ~m;
            Level.pins[row] &= ~m;
            RemoveGateCells(row, m);
            if (column == 0) Level.fillLeftRows = Without(Level.fillLeftRows, row);
            if (column == Last) Level.fillRightRows = Without(Level.fillRightRows, row);
            BreakChainOn(row);
        });

        public void SetPin(int row, int column, bool on) => Edit(() =>
        {
            int m = Mask(column);
            if (!on)
            {
                Level.pins[row] &= ~m;
                return;
            }
            Level.walls[row] &= ~m;
            Level.pins[row] |= m;
            Level.start[row] |= m;
            Level.target[row] |= m;
        });

        /// <summary>Removes everything on a cell: bits in both pictures, wall, pin and gate cell.</summary>
        public void Erase(int row, int column) => Edit(() =>
        {
            int m = Mask(column);
            Level.start[row] &= ~m;
            Level.target[row] &= ~m;
            Level.walls[row] &= ~m;
            Level.pins[row] &= ~m;
            RemoveGateCells(row, m);
        });

        // ----- Row options -----

        public bool IsRotating(int row) => Contains(Level.rotateRows, row);

        /// <summary>A chained pair rotates together. Rotating takes back the 1-IN edges of its rows.</summary>
        public void SetRotate(int row, bool on) => Edit(() =>
        {
            int partner = ChainPartner(row);
            SetRotateRow(row, on);
            if (partner >= 0) SetRotateRow(partner, on);
        });

        /// <summary>The top row of the chain this row is in, or -1.</summary>
        public int ChainTopOf(int row)
        {
            if (Contains(Level.chainTopRows, row)) return row;
            return row > 0 && Contains(Level.chainTopRows, row - 1) ? row - 1 : -1;
        }

        /// <summary>
        /// Chains <paramref name="top"/> with the row below it. Takes back other chains on these rows,
        /// walls on them and 1-IN edges inside the chain; if one row rotates, both do.
        /// </summary>
        public void SetChain(int top, bool on) => Edit(() =>
        {
            if (top < 0 || top >= Last) throw new ArgumentOutOfRangeException(nameof(top));
            int bottom = top + 1;
            if (!on)
            {
                Level.chainTopRows = Without(Level.chainTopRows, top);
                return;
            }
            BreakChainOn(top);
            BreakChainOn(bottom);
            Level.walls[top] = 0;
            Level.walls[bottom] = 0;
            Level.fillRightRows = Without(Level.fillRightRows, top);
            Level.fillLeftRows = Without(Level.fillLeftRows, bottom);
            if (IsRotating(top) != IsRotating(bottom))
            {
                SetRotateRow(top, true);
                SetRotateRow(bottom, true);
            }
            Level.chainTopRows = With(Level.chainTopRows, top);
        });

        public bool HasFill(int row, bool left) => Contains(left ? Level.fillLeftRows : Level.fillRightRows, row);

        /// <summary>A 1-IN edge takes back its end cell's wall and gate cell, rotation, and a chain it would sit inside.</summary>
        public void SetFill(int row, bool left, bool on) => Edit(() =>
        {
            if (!on)
            {
                if (left) Level.fillLeftRows = Without(Level.fillLeftRows, row);
                else Level.fillRightRows = Without(Level.fillRightRows, row);
                return;
            }
            int top = ChainTopOf(row);
            if (top >= 0 && (left ? row == top + 1 : row == top)) BreakChainOn(row);
            int partner = ChainPartner(row);
            SetRotateRow(row, false);
            if (partner >= 0) SetRotateRow(partner, false);

            int m = Mask(left ? 0 : Last);
            Level.walls[row] &= ~m;
            RemoveGateCells(row, m);
            if (left) Level.fillLeftRows = With(Level.fillLeftRows, row);
            else Level.fillRightRows = With(Level.fillRightRows, row);
        });

        /// <summary>The row's gate, or null. A row has at most one.</summary>
        public RowGate GateOn(int row) => Array.Find(Level.gates, g => g.row == row);

        /// <summary>Adds a cell to the row's gate, creating the gate with this op or changing it to this op.</summary>
        public void SetGateCell(int row, int column, GateOp op) => Edit(() =>
        {
            int m = Mask(column);
            Level.walls[row] &= ~m;
            if (column == 0) Level.fillLeftRows = Without(Level.fillLeftRows, row);
            if (column == Last) Level.fillRightRows = Without(Level.fillRightRows, row);

            var gate = GateOn(row);
            if (gate == null) AddGate(new RowGate(row, op, m));
            else
            {
                gate.op = op;
                gate.mask |= m;
            }
        });

        /// <summary>Removes a cell from the row's gate; the last cell removes the gate.</summary>
        public void ClearGateCell(int row, int column) => Edit(() => RemoveGateCells(row, Mask(column)));

        /// <summary>No effect if the row has no gate.</summary>
        public void SetGateOp(int row, GateOp op) => Edit(() =>
        {
            var gate = GateOn(row);
            if (gate != null) gate.op = op;
        });

        public void RemoveGate(int row) => Edit(() => Level.gates = Array.FindAll(Level.gates, g => g.row != row));

        // ----- Level settings -----

        public void SetGatesOnColumnMoves(bool on) => Edit(() => Level.gatesOnColumnMoves = on);

        /// <summary>The count is clamped to 0..<see cref="MaxLossCount"/>; with any loss allowed it is 0.</summary>
        public void SetLoss(LossRule rule, int count) => Edit(() =>
        {
            Level.lossRule = rule;
            Level.lossCount = rule == LossRule.Any ? 0 : Math.Max(0, Math.Min(MaxLossCount, count));
        });

        /// <summary>The name is not part of the puzzle, so a new name keeps par and solution.</summary>
        public void SetName(string name)
        {
            string clean = SanitizeName(name);
            if (clean == Level.name) return;
            if (strokeBefore == null) Remember(Clone(Level));
            Level.name = clean;
            Changed?.Invoke();
        }

        // ----- Checks -----

        /// <summary>What stands between this valid level and a puzzle. Empty when it is worth solving.</summary>
        public List<LevelProblem> Problems()
        {
            var problems = new List<LevelProblem>();
            if (SameRows(Level.start, Level.target)) problems.Add(LevelProblem.SameStartAndTarget);
            if (LevelRules.Compile(Level).Moves.Count == 0) problems.Add(LevelProblem.NoMoves);
            return problems;
        }

        /// <summary>
        /// Everything <see cref="LevelRules.Validate"/> rejects, plus what only authored levels must keep
        /// to: the view draws one rule per cell, so a row has one gate and a gate cell is no 1-IN cell.
        /// </summary>
        public static List<string> AuthoredErrors(LevelData d)
        {
            var errors = LevelRules.Validate(d);
            if (errors.Count > 0) return errors;

            var seen = new bool[BoardBits.Size];
            foreach (var g in d.gates ?? Array.Empty<RowGate>())
            {
                if (seen[g.row]) errors.Add($"row {g.row} has more than one gate.");
                seen[g.row] = true;
                if (Contains(d.fillLeftRows, g.row) && (g.mask & 0x80) != 0) errors.Add($"gate on row {g.row} covers its 1-IN left cell.");
                if (Contains(d.fillRightRows, g.row) && (g.mask & 0x01) != 0) errors.Add($"gate on row {g.row} covers its 1-IN right cell.");
                if (g.mask == 0) errors.Add($"gate on row {g.row} has no cells.");
            }
            if (d.lossCount > MaxLossCount) errors.Add($"lossCount {d.lossCount} is above {MaxLossCount}.");
            return errors;
        }

        /// <summary>
        /// Keeps letters, digits, spaces and a few marks, up to <see cref="MaxNameLength"/>. Names come from
        /// share codes too, so nothing that TextMeshPro could read as a rich text tag may pass.
        /// </summary>
        public static string SanitizeName(string name)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char ch in name ?? "")
            {
                if (kept.Length == MaxNameLength) break;
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')
                    || ch == ' ' || ch == '-' || ch == '.' || ch == '!' || ch == '?' || ch == '\'';
                if (ok) kept.Append(ch);
            }
            return kept.ToString().Trim();
        }

        // ----- Copies -----

        public static LevelData Clone(LevelData d) => new LevelData
        {
            id = d.id,
            name = d.name,
            start = (int[])d.start.Clone(),
            target = (int[])d.target.Clone(),
            walls = (int[])d.walls.Clone(),
            pins = (int[])d.pins.Clone(),
            rotateRows = (int[])(d.rotateRows ?? Array.Empty<int>()).Clone(),
            chainTopRows = (int[])(d.chainTopRows ?? Array.Empty<int>()).Clone(),
            fillLeftRows = (int[])(d.fillLeftRows ?? Array.Empty<int>()).Clone(),
            fillRightRows = (int[])(d.fillRightRows ?? Array.Empty<int>()).Clone(),
            gates = Array.ConvertAll(d.gates ?? Array.Empty<RowGate>(), g => new RowGate(g.row, g.op, g.mask)),
            gatesOnColumnMoves = d.gatesOnColumnMoves,
            lossRule = d.lossRule,
            lossCount = d.lossCount,
            par = d.par,
            solution = d.solution
        };

        /// <summary>True if both have the same name and rules; par and solution do not count.</summary>
        public static bool SameLevel(LevelData a, LevelData b) => a.name == b.name && SamePuzzle(a, b);

        /// <summary>True if both have the same rules, whatever their names, par and solution.</summary>
        public static bool SamePuzzle(LevelData a, LevelData b)
        {
            if (a.gatesOnColumnMoves != b.gatesOnColumnMoves) return false;
            if (a.lossRule != b.lossRule || a.lossCount != b.lossCount) return false;
            if (!SameRows(a.start, b.start) || !SameRows(a.target, b.target)) return false;
            if (!SameRows(a.walls, b.walls) || !SameRows(a.pins, b.pins)) return false;
            if (!SameSet(a.rotateRows, b.rotateRows) || !SameSet(a.chainTopRows, b.chainTopRows)) return false;
            if (!SameSet(a.fillLeftRows, b.fillLeftRows) || !SameSet(a.fillRightRows, b.fillRightRows)) return false;

            var ga = a.gates ?? Array.Empty<RowGate>();
            var gb = b.gates ?? Array.Empty<RowGate>();
            if (ga.Length != gb.Length) return false;
            for (int i = 0; i < ga.Length; i++)
                if (ga[i].row != gb[i].row || ga[i].op != gb[i].op || ga[i].mask != gb[i].mask) return false;
            return true;
        }

        // ----- Helpers -----

        int[] Rows(EditLayer layer) => layer == EditLayer.Start ? Level.start : Level.target;

        static int Mask(int column) => 0x80 >> column;

        int ChainPartner(int row)
        {
            int top = ChainTopOf(row);
            return top < 0 ? -1 : row == top ? top + 1 : top;
        }

        void BreakChainOn(int row)
        {
            int top = ChainTopOf(row);
            if (top >= 0) Level.chainTopRows = Without(Level.chainTopRows, top);
        }

        void SetRotateRow(int row, bool on)
        {
            Level.rotateRows = on ? With(Level.rotateRows, row) : Without(Level.rotateRows, row);
            if (!on) return;
            Level.fillLeftRows = Without(Level.fillLeftRows, row);
            Level.fillRightRows = Without(Level.fillRightRows, row);
        }

        void RemoveGateCells(int row, int mask)
        {
            var gate = GateOn(row);
            if (gate == null) return;
            gate.mask &= ~mask;
            if (gate.mask == 0) Level.gates = Array.FindAll(Level.gates, g => g.row != row);
        }

        void AddGate(RowGate gate)
        {
            var list = new List<RowGate>(Level.gates) { gate };
            list.Sort((a, b) => a.row.CompareTo(b.row));
            Level.gates = list.ToArray();
        }

        static bool Contains(int[] rows, int row) => rows != null && Array.IndexOf(rows, row) >= 0;

        static int[] With(int[] rows, int row)
        {
            if (Contains(rows, row)) return rows;
            var list = new List<int>(rows ?? Array.Empty<int>()) { row };
            list.Sort();
            return list.ToArray();
        }

        static int[] Without(int[] rows, int row) => Array.FindAll(rows ?? Array.Empty<int>(), r => r != row);

        static bool SameRows(int[] a, int[] b)
        {
            for (int i = 0; i < BoardBits.Size; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static bool SameSet(int[] a, int[] b)
        {
            a = a ?? Array.Empty<int>();
            b = b ?? Array.Empty<int>();
            if (a.Length != b.Length) return false;
            foreach (int r in a) if (!Contains(b, r)) return false;
            return true;
        }
    }
}
