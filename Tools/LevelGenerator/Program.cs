using System;
using System.Collections.Generic;
using System.Linq;
using ShiftRegister.Core;

static class Program
{
    const Direction Left = Direction.Left, Right = Direction.Right, Up = Direction.Up, Down = Direction.Down;
    static Move RowMove(int row, Direction direction, int segment = 0) => Move.Row(row, direction, segment);
    static Move ColumnMove(int column, Direction direction, int segment = 0) => Move.Column(column, direction, segment);

    static LevelData Copy(LevelData level) => new LevelData
    {
        id = level.id,
        name = level.name,
        start = (int[])level.start.Clone(),
        target = (int[])level.target.Clone(),
        walls = (int[])level.walls.Clone(),
        pins = (int[])level.pins.Clone(),
        rotateRows = (int[])level.rotateRows.Clone(),
        chainTopRows = (int[])level.chainTopRows.Clone(),
        fillLeftRows = (int[])level.fillLeftRows.Clone(),
        fillRightRows = (int[])level.fillRightRows.Clone(),
        gates = level.gates.Select(gate => new RowGate(gate.row, gate.op, gate.mask)).ToArray(),
        gatesOnColumnMoves = level.gatesOnColumnMoves,
        lossRule = level.lossRule,
        lossCount = level.lossCount
    };

    /// <summary>
    /// Start = target after moveCount random lossless moves (invertible for walls/pins/rotate/chains).
    /// </summary>
    static LevelData Scramble(LevelData template, int moveCount, int seed, int junk = 0)
    {
        var random = new Random(seed);
        var level = Copy(template);
        level.start = (int[])level.target.Clone();
        var rules = LevelRules.Compile(level);
        ulong board = rules.Target;
        Move? last = null;
        int done = 0, guard = 0;
        while (done < moveCount && guard++ < 1000)
        {
            var move = rules.Moves[random.Next(rules.Moves.Count)];
            if (last.HasValue && last.Value.Axis == move.Axis && last.Value.Line == move.Line
                && last.Value.Segment == move.Segment && last.Value.TowardStart != move.TowardStart)
            {
                continue;
            }

            if (!rules.TryApply(board, move, out var outcome) || outcome.LostCount > 0 || outcome.After == board)
            {
                continue;
            }

            board = outcome.After;
            last = move;
            done++;
        }
        // Junk bits for bucket goals: random empty, non-wall cells.
        int added = 0;
        while (added < junk)
        {
            int row = random.Next(8), column = random.Next(8);
            if (BoardBits.GetCell(board, row, column) || rules.IsWall(row, column))
            {
                continue;
            }

            board = BoardBits.SetCell(board, row, column, true);
            added++;
        }

        level.start = BoardBits.ToRows(board);
        return level;
    }

    /// <summary>Target = start after the given forward moves (for gates and other irreversible rules).</summary>
    static LevelData Forward(LevelData template, params Move[] moves)
    {
        var level = Copy(template);
        level.target = (int[])level.start.Clone();
        var rules = LevelRules.Compile(level);
        ulong board = rules.Start;
        int lost = 0;
        foreach (var move in moves)
        {
            if (!rules.TryApply(board, move, out var outcome))
            {
                throw new Exception("illegal " + move);
            }

            board = outcome.After;
            lost += outcome.LostCount;
        }

        level.target = BoardBits.ToRows(board);
        if (level.lossRule == LossRule.Exactly)
        {
            level.lossCount = lost;
        }

        if (level.lossRule == LossRule.AtMost && lost > level.lossCount)
        {
            throw new Exception("forward design breaks the loss rule");
        }

        return level;
    }

    static bool Uses(LevelData level, SolveResult result, Func<LevelRules, MoveOutcome, bool> predicate)
    {
        var rules = LevelRules.Compile(level);
        ulong board = rules.Start;
        bool any = false;
        foreach (var move in result.Solution)
        {
            rules.TryApply(board, move, out var outcome);
            if (predicate(rules, outcome))
            {
                any = true;
            }

            board = outcome.After;
        }

        return any;
    }

    // Mechanic checks on a solution step.
    static bool SplitLine(LevelRules rules, MoveOutcome outcome) =>
        rules.SegmentCount(outcome.Move.Axis, outcome.Move.Line) > 1;
    static bool Wraps(LevelRules rules, MoveOutcome outcome)
    {
        if (outcome.Move.Axis != Axis.Row || !rules.IsRotating(outcome.Move.Line))
        {
            return false;
        }

        return BoardBits.PopCount(outcome.Before) == BoardBits.PopCount(outcome.AfterShift)
            && outcome.Before != outcome.AfterShift &&
               PlainShiftLoses(outcome);
    }

    static bool PlainShiftLoses(MoveOutcome outcome)
    {
        int line = outcome.Move.Line;
        int value = BoardBits.GetRow(outcome.Before, line);
        return outcome.Move.TowardStart ? (value & 0x80) != 0 : (value & 0x01) != 0;
    }

    static bool Carries(LevelRules rules, MoveOutcome outcome)
    {
        if (outcome.Move.Axis != Axis.Row)
        {
            return false;
        }

        int top = rules.ChainTopOf(outcome.Move.Line);
        if (top < 0)
        {
            return false;
        }

        return outcome.Move.TowardStart ? (BoardBits.GetRow(outcome.Before, top + 1) & 0x80) != 0
                                  : (BoardBits.GetRow(outcome.Before, top) & 0x01) != 0;
    }

    static bool Column(LevelRules rules, MoveOutcome outcome) => outcome.Move.Axis == Axis.Column;

    static readonly List<LevelData> picked = new List<LevelData>();

    static void Find(string id, string name, LevelData template, int moveCount, int parMin, int parMax,
        Func<LevelData, SolveResult, bool> require = null, int junk = 0, int seeds = 300, int maxStates = 2_500_000)
    {
        template.id = id;
        template.name = name;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var level = Scramble(template, moveCount, seed * 7919 + id.GetHashCode(), junk);
            if (level.lossRule == LossRule.Exactly)
            {
                level.lossCount = junk;
            }

            if (LevelRules.Validate(level).Count > 0)
            {
                continue;
            }

            var result = Solver.Solve(level, maxStates, 30);
            if (result.Status != SolveStatus.Solved || result.Par < parMin || result.Par > parMax)
            {
                continue;
            }

            if (require != null && !require(level, result))
            {
                continue;
            }

            Report(level, result, $"seed {seed}");
            picked.Add(level);
            return;
        }

        Console.WriteLine($"!! {id} {name}: no seed found");
    }

    static void Check(LevelData level, string note = "", int maxStates = 4_000_000)
    {
        var errors = LevelRules.Validate(level);
        if (errors.Count > 0)
        { Console.WriteLine($"!! {level.id} INVALID: {string.Join("; ", errors)}"); return; }
        var result = Solver.Solve(level, maxStates, 30);
        Report(level, result, note);
        if (result.Status == SolveStatus.Solved)
        {
            picked.Add(level);
        }
    }

    static void Report(LevelData level, SolveResult result, string note)
    {
        var rules = LevelRules.Compile(level);
        Console.WriteLine($"== {level.id} {level.name} [{note}] {result}  rule={level.lossRule}/{level.lossCount}");
        var startRows = BoardBits.ToPicture(rules.Start).Split('\n');
        var targetRows = BoardBits.ToPicture(rules.Target).Split('\n');
        for (int i = 0; i < 8; i++)
        {
            string startRow = Mark(startRows[i], rules, i), targetRow = Mark(targetRows[i], rules, i);
            Console.WriteLine($"   {startRow}   {(i == 3 ? "->" : "  ")}   {targetRow}");
        }
    }

    static string Mark(string row, LevelRules rules, int rowIndex)
    {
        var cells = row.ToCharArray();
        for (int column = 0; column < 8; column++)
        {
            if (rules.IsWall(rowIndex, column))
            {
                cells[column] = 'X';
            }
            else if (rules.IsPinned(rowIndex, column))
            {
                cells[column] = 'P';
            }
        }

        return new string(cells);
    }

    static int Bits(string row)
    {
        int bits = 0;
        for (int i = 0; i < 8; i++)
        {
            if (row[i] != '.')
            {
                bits |= 0x80 >> i;
            }
        }

        return bits;
    }

    static int[] Rows(params string[] rows) => rows.Select(Bits).ToArray();

    static void Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : "";
        if (only == "" || only == "3")
        {
            Chapter3();
        }

        if (only == "" || only == "4")
        {
            Chapter4();
        }

        if (only == "" || only == "5")
        {
            Chapter5();
        }

        if (only == "" || only == "6")
        {
            Chapter6();
        }

        System.IO.File.WriteAllText($"picked{only}.txt", string.Join("\n", picked.Select(level =>
            $"{level.id}|{level.name}|{string.Join(",", level.start)}|{string.Join(",", level.target)}|" +
            $"{string.Join(",", level.walls)}|{string.Join(",", level.pins)}|" +
            $"{string.Join(",", level.rotateRows)}|{string.Join(",", level.chainTopRows)}|" +
            $"{string.Join(",", level.fillLeftRows)}|{string.Join(",", level.fillRightRows)}|" +
            $"{string.Join(";", level.gates.Select(gate => $"{gate.row}:{(int)gate.op}:{gate.mask}"))}|" +
            $"{(level.gatesOnColumnMoves ? 1 : 0)}|{(int)level.lossRule}|{level.lossCount}")));
    }

    /// <summary>'#' bit, 'X' wall, 'P' pinned bit, '.' empty.</summary>
    static LevelData Picture(params string[] rows)
    {
        var level = new LevelData();
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                int bit = 0x80 >> column;
                switch (rows[row][column])
                {
                    case '#':
                        level.target[row] |= bit;
                        break;
                    case 'P':
                        level.target[row] |= bit;
                        level.pins[row] |= bit;
                        break;
                    case 'X':
                        level.walls[row] |= bit;
                        break;
                }
            }
        }

        level.start = (int[])level.target.Clone();
        return level;
    }

    static Func<LevelData, SolveResult, bool> Needs(params Func<LevelRules, MoveOutcome, bool>[] predicates) =>
        (level, result) => predicates.All(predicate => Uses(level, result, predicate));

    static void Chapter3()
    {
        Find("3-1", "Box", Picture(
            "........",
            ".######.",
            ".#....#.",
            ".#X##.#.",
            ".#.##X#.",
            ".#....#.",
            ".######.",
            "........"), 4, 3, 5, Needs(SplitLine));
        Find("3-2", "Cross", Picture(
            "#..XX..#",
            ".#....#.",
            "..#..#..",
            "X..##...",
            "...##..X",
            "..#..#..",
            ".#....#.",
            "#..XX..#"), 5, 4, 6, Needs(SplitLine));
        Find("3-3", "Key", Picture(
            "........",
            ".##.....",
            "#..#....",
            "#..P####",
            "#..#.#.#",
            ".##.....",
            "........",
            "........"), 5, 4, 6);
        Find("3-4", "Window", Picture(
            "........",
            ".######.",
            ".#X##X#.",
            ".######.",
            ".#.##.#.",
            ".P####P.",
            "........",
            "........"), 6, 4, 7, Needs(SplitLine));
        var fortress = Picture(
            "#.#..#.#",
            "########",
            "#..XX..#",
            "#.####.#",
            "#.####.#",
            "#......#",
            "P######P",
            "#.#..#.#");
        fortress.lossRule = LossRule.AtMost;
        Find("3-5", "Fortress", fortress, 7, 5, 8, Needs(SplitLine));
    }

    static void Chapter4()
    {
        var stripes = Picture("#.#.#.#.", ".#.#.#.#", "#.#.#.#.", ".#.#.#.#", "#.#.#.#.", ".#.#.#.#", "#.#.#.#.",
            ".#.#.#.#");
        stripes.rotateRows = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        Find("4-1", "Stripes", stripes, 4, 3, 5, Needs(Wraps));

        var diamond = Picture("...##...", "..#..#..", ".#....#.", "#......#", "#......#", ".#....#.", "..#..#..",
            "...##...");
        diamond.rotateRows = new[] { 1, 2, 5, 6 };
        Find("4-2", "Diamond", diamond, 5, 4, 6, Needs(Wraps));

        var spiral = Picture("########", ".......#", "######.#", "#....#.#", "#.##.#.#", "#......#", "########",
            "........");
        spiral.rotateRows = new[] { 2, 4, 5 };
        Find("4-3", "Spiral", spiral, 5, 4, 6, Needs(Wraps));

        var gears = Picture(
            "..####..",
            ".#.XX.#.",
            "#.#..#.#",
            "#..##..#",
            "#..##..#",
            "#.#..#.#",
            ".#.XX.#.",
            "..####..");
        gears.rotateRows = new[] { 1, 2, 5, 6 };
        Find("4-4", "Gears", gears, 6, 4, 7, Needs(Wraps, SplitLine));

        var orbit = Picture(
            "P......P",
            ".##..##.",
            ".#.##.#.",
            "..#..#..",
            "..#..#..",
            ".#.##.#.",
            ".##..##.",
            "........");
        orbit.rotateRows = new[] { 1, 3, 5 };
        orbit.lossRule = LossRule.AtMost;
        Find("4-5", "Orbit", orbit, 7, 5, 8, Needs(Wraps, Column));
    }

    static void Chapter5()
    {
        var bridge = Picture("........", "........", "########", "#......#", "#......#", "........", "........",
            "........");
        bridge.chainTopRows = new[] { 3 };
        Find("5-1", "Bridge", bridge, 4, 3, 5, Needs(Carries));

        var overflow = Picture("........", "........", "........", "########", "########", "........", "........",
            "........");
        overflow.chainTopRows = new[] { 3 };
        overflow.lossRule = LossRule.Exactly;
        Find("5-2", "Overflow", overflow, 3, 3, 6, Needs(Carries), junk: 2);

        var ladder = Picture(".#....#.", ".######.", ".#....#.", ".#....#.", ".######.", ".#....#.", ".######.",
            ".#....#.");
        ladder.chainTopRows = new[] { 1, 4 };
        Find("5-3", "Ladder", ladder, 5, 4, 7, Needs(Carries));

        var snake = Picture("........", "########", ".......#", "########", "#.......", "########", "........",
            "........");
        snake.chainTopRows = new[] { 1, 4 };
        snake.rotateRows = new[] { 1, 2 };
        snake.lossRule = LossRule.Exactly;
        Find("5-4", "Snake", snake, 5, 4, 7, Needs(Carries), junk: 1);

        var y2026 = Picture("........", "........", "........", ".....###", "###.#.#.", "........", "........",
            ".......P");
        y2026.chainTopRows = new[] { 3 };
        y2026.lossRule = LossRule.Exactly;
        Find("5-5", "2026", y2026, 6, 5, 9, Needs(Carries, Column), junk: 3);
    }

    static void Chapter6()
    {
        // 6-1 Print: OR masks are the only way to make bits.
        var print = new LevelData
        {
            id = "6-1",
            name = "Print",
            start = new[] { 0, 0, 0, 0x80, 0, 0x01, 0, 0 },
            gates = new[] { new RowGate(3, GateOp.Or, 0x80), new RowGate(5, GateOp.Or, 0x01) }
        };
        Check(Forward(print, RowMove(3, Right), RowMove(3, Right), RowMove(3, Right), RowMove(5, Left),
            RowMove(5, Left)), "forward");

        Check(new LevelData
        {
            id = "6-2",
            name = "Four Gates",
            start = new[] { 0, 192, 0, 48, 0, 255, 2, 0 },
            target = new[] { 0, 240, 0, 0, 0, 60, 254, 0 },
            gates = new[] { new RowGate(1, GateOp.Or, 128), new RowGate(3, GateOp.Xor, 24),
            new RowGate(5, GateOp.And, 60), new RowGate(6, GateOp.Not, 1) }
        }, "existing");

        // 6-3 Cancel: XOR rows toggle; matching bits cancel, gaps turn into bits.
        var cancel = new LevelData
        {
            id = "6-3",
            name = "Cancel",
            start = new[] { 0, 0x66, 0, 0x3C, 0x18, 0x3C, 0, 0 },
            gates = new[] { new RowGate(1, GateOp.Xor, 0x18), new RowGate(3, GateOp.Xor, 0x42),
            new RowGate(5, GateOp.Xor, 0x24) }
        };
        Check(Forward(cancel, RowMove(1, Right), ColumnMove(3, Up), RowMove(3, Left), RowMove(5, Right),
            RowMove(1, Left)), "forward");

        // 6-4 Invert: NOT pads flip a row; 1-IN edges feed new bits.
        var invert = new LevelData
        {
            id = "6-4",
            name = "Invert",
            start = new[] { 0, 0x06, 0, 0x60, 0, 0x0C, 0, 0 },
            fillLeftRows = new[] { 5 },
            gates = new[] { new RowGate(1, GateOp.Not, 0x01), new RowGate(3, GateOp.Not, 0x80) }
        };
        Check(Forward(invert, RowMove(1, Right), RowMove(3, Left), RowMove(5, Right), RowMove(5, Right),
            ColumnMove(4, Down)), "forward");

        // 6-5 Circuit: gates also fire when a column passes through their row.
        var circuit = new LevelData
        {
            id = "6-5",
            name = "Circuit",
            gatesOnColumnMoves = true,
            start = new[] { 0x18, 0, 0x81, 0, 0x3C, 0, 0x18, 0 },
            gates = new[] { new RowGate(1, GateOp.Or, 0x24), new RowGate(3, GateOp.Xor, 0x18),
            new RowGate(5, GateOp.And, 0x7E) }
        };
        Check(Forward(circuit, ColumnMove(3, Down), RowMove(2, Right), ColumnMove(4, Down), RowMove(4, Left),
            ColumnMove(2, Down), RowMove(6, Left)), "forward");
    }

}
