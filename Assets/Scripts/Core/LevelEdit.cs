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

        /// <summary>A level with its proof, so undo also brings back what was proven about it.</summary>
        sealed class Snapshot
        {
            public readonly LevelData Level;
            public readonly LevelProof Proof;
            public Snapshot(LevelData level, LevelProof proof)
            {
                Level = level;
                Proof = proof;
            }
        }

        readonly List<Snapshot> undo = new List<Snapshot>();
        readonly List<Snapshot> redo = new List<Snapshot>();
        Snapshot strokeBefore;

        /// <summary>The level as it stands. Treat it as read-only; change it through this class.</summary>
        public LevelData Level { get; private set; }

        /// <summary>How par and solution were proven; None after any change to the puzzle.</summary>
        public LevelProof Proof { get; private set; }

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        /// <summary>Raised after every real change, undo and redo included.</summary>
        public event Action Changed;

        /// <param name="level">Copied, not kept. Must pass <see cref="AuthoredErrors"/>.</param>
        public LevelEdit(LevelData level, LevelProof proof = LevelProof.None)
        {
            var errors = AuthoredErrors(level);
            if (errors.Count > 0)
            {
                throw new LevelValidationException(level?.id ?? "?", errors);
            }

            Level = Clone(level);
            Proof = proof;
        }

        /// <summary>A new level with an empty board.</summary>
        public static LevelEdit Blank(string name) => new LevelEdit(new LevelData { name = SanitizeName(name) });

        // ----- Strokes and history -----

        /// <summary>Groups the edits until <see cref="EndStroke"/> into one undo step (one mouse drag).</summary>
        public void BeginStroke()
        {
            if (strokeBefore == null)
            {
                strokeBefore = Now();
            }
        }

        public void EndStroke()
        {
            if (strokeBefore == null)
            {
                return;
            }

            var before = strokeBefore;
            strokeBefore = null;
            if (!SameLevel(before.Level, Level))
            {
                Remember(before);
            }
        }

        public bool Undo() => Swap(undo, redo);
        public bool Redo() => Swap(redo, undo);

        bool Swap(List<Snapshot> from, List<Snapshot> to)
        {
            EndStroke();
            if (from.Count == 0)
            {
                return false;
            }

            to.Add(new Snapshot(Level, Proof));
            var back = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Level = back.Level;
            Proof = back.Proof;
            Changed?.Invoke();
            return true;
        }

        Snapshot Now() => new Snapshot(Clone(Level), Proof);

        void Remember(Snapshot before)
        {
            undo.Add(before);
            if (undo.Count > MaxHistory)
            {
                undo.RemoveAt(0);
            }

            redo.Clear();
        }

        void Edit(Action change)
        {
            var before = Now();
            change();
            if (SameLevel(before.Level, Level))
            {
                return;
            }

            Level.par = 0;
            Level.solution = "";
            Proof = LevelProof.None;
            if (strokeBefore == null)
            {
                Remember(before);
            }

            Changed?.Invoke();
        }

        /// <summary>Stores a proof's result. The puzzle does not change, so this is not an undo step.</summary>
        public void SetSolution(IReadOnlyList<Move> solution, LevelProof proof)
        {
            Level.par = solution.Count;
            Level.solution = string.Join(" ", solution);
            Proof = proof;
            Changed?.Invoke();
        }

        // ----- Cells -----

        public bool GetBit(EditLayer layer, int row, int column) => (Rows(layer)[row] & Mask(column)) != 0;
        public bool IsWall(int row, int column) => (Level.walls[row] & Mask(column)) != 0;
        public bool IsPin(int row, int column) => (Level.pins[row] & Mask(column)) != 0;

        public void SetBit(EditLayer layer, int row, int column, bool on) => Edit(() =>
        {
            int mask = Mask(column);
            if (on)
            {
                if ((Level.walls[row] & mask) == 0)
                {
                    Rows(layer)[row] |= mask;
                }

                return;
            }

            Rows(layer)[row] &= ~mask;
            Level.pins[row] &= ~mask; // a pin has to be on in both pictures
        });

        public void SetWall(int row, int column, bool on) => Edit(() =>
        {
            int mask = Mask(column);
            if (!on)
            {
                Level.walls[row] &= ~mask;
                return;
            }

            Level.walls[row] |= mask;
            Level.start[row] &= ~mask;
            Level.target[row] &= ~mask;
            Level.pins[row] &= ~mask;
            RemoveGateCells(row, mask);
            if (column == 0)
            {
                Level.fillLeftRows = Without(Level.fillLeftRows, row);
            }

            if (column == Last)
            {
                Level.fillRightRows = Without(Level.fillRightRows, row);
            }

            BreakChainOn(row);
        });

        public void SetPin(int row, int column, bool on) => Edit(() =>
        {
            int mask = Mask(column);
            if (!on)
            {
                Level.pins[row] &= ~mask;
                return;
            }

            Level.walls[row] &= ~mask;
            Level.pins[row] |= mask;
            Level.start[row] |= mask;
            Level.target[row] |= mask;
        });

        /// <summary>Removes everything on a cell: bits in both pictures, wall, pin and gate cell.</summary>
        public void Erase(int row, int column) => Edit(() =>
        {
            int mask = Mask(column);
            Level.start[row] &= ~mask;
            Level.target[row] &= ~mask;
            Level.walls[row] &= ~mask;
            Level.pins[row] &= ~mask;
            RemoveGateCells(row, mask);
        });

        // ----- Row options -----

        public bool IsRotating(int row) => Contains(Level.rotateRows, row);

        /// <summary>A chained pair rotates together. Rotating takes back the 1-IN edges of its rows.</summary>
        public void SetRotate(int row, bool on) => Edit(() =>
        {
            int partner = ChainPartner(row);
            SetRotateRow(row, on);
            if (partner >= 0)
            {
                SetRotateRow(partner, on);
            }
        });

        /// <summary>The top row of the chain this row is in, or -1.</summary>
        public int ChainTopOf(int row)
        {
            if (Contains(Level.chainTopRows, row))
            {
                return row;
            }

            return row > 0 && Contains(Level.chainTopRows, row - 1) ? row - 1 : -1;
        }

        /// <summary>
        /// Chains <paramref name="top"/> with the row below it. Takes back other chains on these rows,
        /// walls on them and 1-IN edges inside the chain; if one row rotates, both do.
        /// </summary>
        public void SetChain(int top, bool on) => Edit(() =>
        {
            if (top < 0 || top >= Last)
            {
                throw new ArgumentOutOfRangeException(nameof(top));
            }

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
                if (left)
                {
                    Level.fillLeftRows = Without(Level.fillLeftRows, row);
                }
                else
                {
                    Level.fillRightRows = Without(Level.fillRightRows, row);
                }

                return;
            }

            int top = ChainTopOf(row);
            if (top >= 0 && (left ? row == top + 1 : row == top))
            {
                BreakChainOn(row);
            }

            int partner = ChainPartner(row);
            SetRotateRow(row, false);
            if (partner >= 0)
            {
                SetRotateRow(partner, false);
            }

            int mask = Mask(left ? 0 : Last);
            Level.walls[row] &= ~mask;
            RemoveGateCells(row, mask);
            if (left)
            {
                Level.fillLeftRows = With(Level.fillLeftRows, row);
            }
            else
            {
                Level.fillRightRows = With(Level.fillRightRows, row);
            }
        });

        /// <summary>The row's gate, or null. A row has at most one.</summary>
        public RowGate GateOn(int row) => Array.Find(Level.gates, gate => gate.row == row);

        /// <summary>Adds a cell to the row's gate, creating the gate with this op or changing it to this op.</summary>
        public void SetGateCell(int row, int column, GateOp op) => Edit(() =>
        {
            int mask = Mask(column);
            Level.walls[row] &= ~mask;
            if (column == 0)
            {
                Level.fillLeftRows = Without(Level.fillLeftRows, row);
            }

            if (column == Last)
            {
                Level.fillRightRows = Without(Level.fillRightRows, row);
            }

            var gate = GateOn(row);
            if (gate == null)
            {
                AddGate(new RowGate(row, op, mask));
            }
            else
            {
                gate.op = op;
                gate.mask |= mask;
            }
        });

        /// <summary>Removes a cell from the row's gate; the last cell removes the gate.</summary>
        public void ClearGateCell(int row, int column) => Edit(() => RemoveGateCells(row, Mask(column)));

        /// <summary>No effect if the row has no gate.</summary>
        public void SetGateOp(int row, GateOp op) => Edit(() =>
        {
            var gate = GateOn(row);
            if (gate != null)
            {
                gate.op = op;
            }
        });

        public void RemoveGate(int row) => Edit(() => Level.gates = Array.FindAll(Level.gates, gate => gate.row != row));

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
            if (clean == Level.name)
            {
                return;
            }

            if (strokeBefore == null)
            {
                Remember(Now());
            }

            Level.name = clean;
            Changed?.Invoke();
        }

        // ----- Checks -----

        /// <summary>What stands between this valid level and a puzzle. Empty when it is worth solving.</summary>
        public List<LevelProblem> Problems()
        {
            var problems = new List<LevelProblem>();
            if (SameRows(Level.start, Level.target))
            {
                problems.Add(LevelProblem.SameStartAndTarget);
            }

            if (LevelRules.Compile(Level).Moves.Count == 0)
            {
                problems.Add(LevelProblem.NoMoves);
            }

            return problems;
        }

        /// <summary>
        /// Everything <see cref="LevelRules.Validate"/> rejects, plus what only authored levels must keep
        /// to: the view draws one rule per cell, so a row has one gate and a gate cell is no 1-IN cell.
        /// </summary>
        public static List<string> AuthoredErrors(LevelData level)
        {
            var errors = LevelRules.Validate(level);
            if (errors.Count > 0)
            {
                return errors;
            }

            var seen = new bool[BoardBits.Size];
            foreach (var gate in level.gates ?? Array.Empty<RowGate>())
            {
                if (seen[gate.row])
                {
                    errors.Add($"row {gate.row} has more than one gate.");
                }

                seen[gate.row] = true;
                if (Contains(level.fillLeftRows, gate.row) && (gate.mask & 0x80) != 0)
                {
                    errors.Add($"gate on row {gate.row} covers its 1-IN left cell.");
                }

                if (Contains(level.fillRightRows, gate.row) && (gate.mask & 0x01) != 0)
                {
                    errors.Add($"gate on row {gate.row} covers its 1-IN right cell.");
                }

                if (gate.mask == 0)
                {
                    errors.Add($"gate on row {gate.row} has no cells.");
                }
            }

            if (level.lossCount > MaxLossCount)
            {
                errors.Add($"lossCount {level.lossCount} is above {MaxLossCount}.");
            }

            return errors;
        }

        /// <summary>
        /// Keeps letters, digits, spaces and a few marks, up to <see cref="MaxNameLength"/>. Names come from
        /// share codes too, so nothing that TextMeshPro could read as a rich text tag may pass.
        /// </summary>
        public static string SanitizeName(string name)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char character in name ?? "")
            {
                if (kept.Length == MaxNameLength)
                {
                    break;
                }

                if (IsNameChar(character))
                {
                    kept.Append(character);
                }
            }

            return kept.ToString().Trim();
        }

        /// <summary>The characters a level name may hold.</summary>
        public static bool IsNameChar(char character) =>
            (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')
            || character == ' ' || character == '-' || character == '.' || character == '!' || character == '?' || character == '\'';

        // ----- Copies -----

        public static LevelData Clone(LevelData level) => new LevelData
        {
            id = level.id,
            name = level.name,
            start = (int[])level.start.Clone(),
            target = (int[])level.target.Clone(),
            walls = (int[])level.walls.Clone(),
            pins = (int[])level.pins.Clone(),
            rotateRows = (int[])(level.rotateRows ?? Array.Empty<int>()).Clone(),
            chainTopRows = (int[])(level.chainTopRows ?? Array.Empty<int>()).Clone(),
            fillLeftRows = (int[])(level.fillLeftRows ?? Array.Empty<int>()).Clone(),
            fillRightRows = (int[])(level.fillRightRows ?? Array.Empty<int>()).Clone(),
            gates = Array.ConvertAll(level.gates ?? Array.Empty<RowGate>(), gate => new RowGate(gate.row, gate.op, gate.mask)),
            gatesOnColumnMoves = level.gatesOnColumnMoves,
            lossRule = level.lossRule,
            lossCount = level.lossCount,
            par = level.par,
            solution = level.solution
        };

        /// <summary>True if both have the same name and rules; par and solution do not count.</summary>
        public static bool SameLevel(LevelData first, LevelData second) => first.name == second.name && SamePuzzle(first, second);

        /// <summary>True if both have the same rules, whatever their names, par and solution.</summary>
        public static bool SamePuzzle(LevelData first, LevelData second)
        {
            if (first.gatesOnColumnMoves != second.gatesOnColumnMoves)
            {
                return false;
            }

            if (first.lossRule != second.lossRule || first.lossCount != second.lossCount)
            {
                return false;
            }

            if (!SameRows(first.start, second.start) || !SameRows(first.target, second.target))
            {
                return false;
            }

            if (!SameRows(first.walls, second.walls) || !SameRows(first.pins, second.pins))
            {
                return false;
            }

            if (!SameSet(first.rotateRows, second.rotateRows) || !SameSet(first.chainTopRows, second.chainTopRows))
            {
                return false;
            }

            if (!SameSet(first.fillLeftRows, second.fillLeftRows) || !SameSet(first.fillRightRows, second.fillRightRows))
            {
                return false;
            }

            var firstGates = first.gates ?? Array.Empty<RowGate>();
            var secondGates = second.gates ?? Array.Empty<RowGate>();
            if (firstGates.Length != secondGates.Length)
            {
                return false;
            }

            for (int i = 0; i < firstGates.Length; i++)
            {
                if (firstGates[i].row != secondGates[i].row || firstGates[i].op != secondGates[i].op || firstGates[i].mask != secondGates[i].mask)
                {
                    return false;
                }
            }

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
            if (top >= 0)
            {
                Level.chainTopRows = Without(Level.chainTopRows, top);
            }
        }

        void SetRotateRow(int row, bool on)
        {
            Level.rotateRows = on ? With(Level.rotateRows, row) : Without(Level.rotateRows, row);
            if (!on)
            {
                return;
            }

            Level.fillLeftRows = Without(Level.fillLeftRows, row);
            Level.fillRightRows = Without(Level.fillRightRows, row);
        }

        void RemoveGateCells(int row, int mask)
        {
            var gate = GateOn(row);
            if (gate == null)
            {
                return;
            }

            gate.mask &= ~mask;
            if (gate.mask == 0)
            {
                Level.gates = Array.FindAll(Level.gates, other => other.row != row);
            }
        }

        void AddGate(RowGate gate)
        {
            var list = new List<RowGate>(Level.gates) { gate };
            list.Sort((first, second) => first.row.CompareTo(second.row));
            Level.gates = list.ToArray();
        }

        static bool Contains(int[] rows, int row) => rows != null && Array.IndexOf(rows, row) >= 0;

        static int[] With(int[] rows, int row)
        {
            if (Contains(rows, row))
            {
                return rows;
            }

            var list = new List<int>(rows ?? Array.Empty<int>()) { row };
            list.Sort();
            return list.ToArray();
        }

        static int[] Without(int[] rows, int row) => Array.FindAll(rows ?? Array.Empty<int>(), other => other != row);

        static bool SameRows(int[] first, int[] second)
        {
            for (int i = 0; i < BoardBits.Size; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        static bool SameSet(int[] first, int[] second)
        {
            first = first ?? Array.Empty<int>();
            second = second ?? Array.Empty<int>();
            if (first.Length != second.Length)
            {
                return false;
            }

            foreach (int row in first)
            {
                if (!Contains(second, row))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
