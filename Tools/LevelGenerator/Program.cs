using System;
using System.Collections.Generic;
using System.Linq;
using ShiftRegister.Core;

static class P
{
    const Direction L = Direction.Left, Rt = Direction.Right, U = Direction.Up, D = Direction.Down;
    static Move R(int r, Direction d, int s = 0) => Move.Row(r, d, s);
    static Move C(int c, Direction d, int s = 0) => Move.Column(c, d, s);

    static LevelData Copy(LevelData d) => new LevelData
    {
        id = d.id, name = d.name,
        start = (int[])d.start.Clone(), target = (int[])d.target.Clone(),
        walls = (int[])d.walls.Clone(), pins = (int[])d.pins.Clone(),
        rotateRows = (int[])d.rotateRows.Clone(), chainTopRows = (int[])d.chainTopRows.Clone(),
        fillLeftRows = (int[])d.fillLeftRows.Clone(), fillRightRows = (int[])d.fillRightRows.Clone(),
        gates = d.gates.Select(g => new RowGate(g.row, g.op, g.mask)).ToArray(),
        gatesOnColumnMoves = d.gatesOnColumnMoves, lossRule = d.lossRule, lossCount = d.lossCount
    };

    /// <summary>Start = target after k random lossless moves (invertible for walls/pins/rotate/chains).</summary>
    static LevelData Scramble(LevelData tmpl, int k, int seed, int junk = 0)
    {
        var rng = new Random(seed);
        var d = Copy(tmpl);
        d.start = (int[])d.target.Clone();
        var rules = LevelRules.Compile(d);
        ulong b = rules.Target;
        Move? last = null;
        int done = 0, guard = 0;
        while (done < k && guard++ < 1000)
        {
            var m = rules.Moves[rng.Next(rules.Moves.Count)];
            if (last.HasValue && last.Value.Axis == m.Axis && last.Value.Line == m.Line && last.Value.Segment == m.Segment && last.Value.TowardStart != m.TowardStart) continue;
            if (!rules.TryApply(b, m, out var o) || o.LostCount > 0 || o.After == b) continue;
            b = o.After; last = m; done++;
        }
        // Junk bits for bucket goals: random empty, non-wall cells.
        int added = 0;
        while (added < junk)
        {
            int r = rng.Next(8), c = rng.Next(8);
            if (BoardBits.GetCell(b, r, c) || rules.IsWall(r, c)) continue;
            b = BoardBits.SetCell(b, r, c, true);
            added++;
        }
        d.start = BoardBits.ToRows(b);
        return d;
    }

    /// <summary>Target = start after the given forward moves (for gates and other irreversible rules).</summary>
    static LevelData Forward(LevelData tmpl, params Move[] moves)
    {
        var d = Copy(tmpl);
        d.target = (int[])d.start.Clone();
        var rules = LevelRules.Compile(d);
        ulong b = rules.Start;
        int lost = 0;
        foreach (var m in moves)
        {
            if (!rules.TryApply(b, m, out var o)) throw new Exception("illegal " + m);
            b = o.After;
            lost += o.LostCount;
        }
        d.target = BoardBits.ToRows(b);
        if (d.lossRule == LossRule.Exactly) d.lossCount = lost;
        if (d.lossRule == LossRule.AtMost && lost > d.lossCount) throw new Exception("forward design breaks the loss rule");
        return d;
    }

    static bool Uses(LevelData d, SolveResult r, Func<LevelRules, MoveOutcome, bool> pred)
    {
        var rules = LevelRules.Compile(d);
        ulong b = rules.Start;
        bool any = false;
        foreach (var m in r.Solution)
        {
            rules.TryApply(b, m, out var o);
            if (pred(rules, o)) any = true;
            b = o.After;
        }
        return any;
    }

    // Mechanic checks on a solution step.
    static bool SplitLine(LevelRules rules, MoveOutcome o) => rules.SegmentCount(o.Move.Axis, o.Move.Line) > 1;
    static bool Wraps(LevelRules rules, MoveOutcome o)
    {
        if (o.Move.Axis != Axis.Row || !rules.IsRotating(o.Move.Line)) return false;
        return BoardBits.PopCount(o.Before) == BoardBits.PopCount(o.AfterShift) && o.Before != o.AfterShift &&
               PlainShiftLoses(o);
    }
    static bool PlainShiftLoses(MoveOutcome o)
    {
        int line = o.Move.Line;
        int v = BoardBits.GetRow(o.Before, line);
        return o.Move.TowardStart ? (v & 0x80) != 0 : (v & 0x01) != 0;
    }
    static bool Carries(LevelRules rules, MoveOutcome o)
    {
        if (o.Move.Axis != Axis.Row) return false;
        int top = rules.ChainTopOf(o.Move.Line);
        if (top < 0) return false;
        return o.Move.TowardStart ? (BoardBits.GetRow(o.Before, top + 1) & 0x80) != 0
                                  : (BoardBits.GetRow(o.Before, top) & 0x01) != 0;
    }
    static bool Column(LevelRules rules, MoveOutcome o) => o.Move.Axis == Axis.Column;

    static readonly List<LevelData> picked = new List<LevelData>();

    static void Find(string id, string name, LevelData tmpl, int k, int parMin, int parMax,
        Func<LevelData, SolveResult, bool> require = null, int junk = 0, int seeds = 300, int maxStates = 2_500_000)
    {
        tmpl.id = id; tmpl.name = name;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var d = Scramble(tmpl, k, seed * 7919 + id.GetHashCode(), junk);
            if (d.lossRule == LossRule.Exactly) d.lossCount = junk;
            if (LevelRules.Validate(d).Count > 0) continue;
            var r = Solver.Solve(d, maxStates, 30);
            if (r.Status != SolveStatus.Solved || r.Par < parMin || r.Par > parMax) continue;
            if (require != null && !require(d, r)) continue;
            Report(d, r, $"seed {seed}");
            picked.Add(d);
            return;
        }
        Console.WriteLine($"!! {id} {name}: no seed found");
    }

    static void Check(LevelData d, string note = "", int maxStates = 4_000_000)
    {
        var errs = LevelRules.Validate(d);
        if (errs.Count > 0) { Console.WriteLine($"!! {d.id} INVALID: {string.Join("; ", errs)}"); return; }
        var r = Solver.Solve(d, maxStates, 30);
        Report(d, r, note);
        if (r.Status == SolveStatus.Solved) picked.Add(d);
    }

    static void Report(LevelData d, SolveResult r, string note)
    {
        var rules = LevelRules.Compile(d);
        Console.WriteLine($"== {d.id} {d.name} [{note}] {r}  rule={d.lossRule}/{d.lossCount}");
        var s = BoardBits.ToPicture(rules.Start).Split('\n');
        var t = BoardBits.ToPicture(rules.Target).Split('\n');
        for (int i = 0; i < 8; i++)
        {
            string a = Mark(s[i], rules, i), b = Mark(t[i], rules, i);
            Console.WriteLine($"   {a}   {(i == 3 ? "->" : "  ")}   {b}");
        }
    }

    static string Mark(string row, LevelRules rules, int r)
    {
        var ch = row.ToCharArray();
        for (int c = 0; c < 8; c++)
        {
            if (rules.IsWall(r, c)) ch[c] = 'X';
            else if (rules.IsPinned(r, c)) ch[c] = 'P';
        }
        return new string(ch);
    }

    static int Bits(string row) { int v = 0; for (int i = 0; i < 8; i++) if (row[i] != '.') v |= 0x80 >> i; return v; }
    static int[] Rows(params string[] rows) => rows.Select(Bits).ToArray();

    static void Main(string[] args)
    {
        string only = args.Length > 0 ? args[0] : "";
        if (only == "" || only == "3") Chapter3();
        if (only == "" || only == "4") Chapter4();
        if (only == "" || only == "5") Chapter5();
        if (only == "" || only == "6") Chapter6();
        System.IO.File.WriteAllText($"picked{only}.txt", string.Join("\n", picked.Select(d =>
            $"{d.id}|{d.name}|{string.Join(",", d.start)}|{string.Join(",", d.target)}|{string.Join(",", d.walls)}|{string.Join(",", d.pins)}|" +
            $"{string.Join(",", d.rotateRows)}|{string.Join(",", d.chainTopRows)}|{string.Join(",", d.fillLeftRows)}|{string.Join(",", d.fillRightRows)}|" +
            $"{string.Join(";", d.gates.Select(g => $"{g.row}:{(int)g.op}:{g.mask}"))}|{(d.gatesOnColumnMoves ? 1 : 0)}|{(int)d.lossRule}|{d.lossCount}")));
    }

    /// <summary>'#' bit, 'X' wall, 'P' pinned bit, '.' empty.</summary>
    static LevelData Pic(params string[] rows)
    {
        var d = new LevelData();
        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
        {
            int bit = 0x80 >> c;
            switch (rows[r][c])
            {
                case '#': d.target[r] |= bit; break;
                case 'P': d.target[r] |= bit; d.pins[r] |= bit; break;
                case 'X': d.walls[r] |= bit; break;
            }
        }
        d.start = (int[])d.target.Clone();
        return d;
    }

    static Func<LevelData, SolveResult, bool> Needs(params Func<LevelRules, MoveOutcome, bool>[] preds) =>
        (d, r) => preds.All(p => Uses(d, r, p));

    static void Chapter3()
    {
        Find("3-1", "Box", Pic(
            "........",
            ".######.",
            ".#....#.",
            ".#X##.#.",
            ".#.##X#.",
            ".#....#.",
            ".######.",
            "........"), 4, 3, 5, Needs(SplitLine));
        Find("3-2", "Cross", Pic(
            "#..XX..#",
            ".#....#.",
            "..#..#..",
            "X..##...",
            "...##..X",
            "..#..#..",
            ".#....#.",
            "#..XX..#"), 5, 4, 6, Needs(SplitLine));
        Find("3-3", "Key", Pic(
            "........",
            ".##.....",
            "#..#....",
            "#..P####",
            "#..#.#.#",
            ".##.....",
            "........",
            "........"), 5, 4, 6);
        Find("3-4", "Window", Pic(
            "........",
            ".######.",
            ".#X##X#.",
            ".######.",
            ".#.##.#.",
            ".P####P.",
            "........",
            "........"), 6, 4, 7, Needs(SplitLine));
        var fortress = Pic(
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
        var stripes = Pic("#.#.#.#.", ".#.#.#.#", "#.#.#.#.", ".#.#.#.#", "#.#.#.#.", ".#.#.#.#", "#.#.#.#.", ".#.#.#.#");
        stripes.rotateRows = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        Find("4-1", "Stripes", stripes, 4, 3, 5, Needs(Wraps));

        var diamond = Pic("...##...", "..#..#..", ".#....#.", "#......#", "#......#", ".#....#.", "..#..#..", "...##...");
        diamond.rotateRows = new[] { 1, 2, 5, 6 };
        Find("4-2", "Diamond", diamond, 5, 4, 6, Needs(Wraps));

        var spiral = Pic("########", ".......#", "######.#", "#....#.#", "#.##.#.#", "#......#", "########", "........");
        spiral.rotateRows = new[] { 2, 4, 5 };
        Find("4-3", "Spiral", spiral, 5, 4, 6, Needs(Wraps));

        var gears = Pic(
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

        var orbit = Pic(
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
        var bridge = Pic("........", "........", "########", "#......#", "#......#", "........", "........", "........");
        bridge.chainTopRows = new[] { 3 };
        Find("5-1", "Bridge", bridge, 4, 3, 5, Needs(Carries));

        var overflow = Pic("........", "........", "........", "########", "########", "........", "........", "........");
        overflow.chainTopRows = new[] { 3 };
        overflow.lossRule = LossRule.Exactly;
        Find("5-2", "Overflow", overflow, 3, 3, 6, Needs(Carries), junk: 2);

        var ladder = Pic(".#....#.", ".######.", ".#....#.", ".#....#.", ".######.", ".#....#.", ".######.", ".#....#.");
        ladder.chainTopRows = new[] { 1, 4 };
        Find("5-3", "Ladder", ladder, 5, 4, 7, Needs(Carries));

        var snake = Pic("........", "########", ".......#", "########", "#.......", "########", "........", "........");
        snake.chainTopRows = new[] { 1, 4 };
        snake.rotateRows = new[] { 1, 2 };
        snake.lossRule = LossRule.Exactly;
        Find("5-4", "Snake", snake, 5, 4, 7, Needs(Carries), junk: 1);

        var y2026 = Pic("........", "........", "........", ".....###", "###.#.#.", "........", "........", ".......P");
        y2026.chainTopRows = new[] { 3 };
        y2026.lossRule = LossRule.Exactly;
        Find("5-5", "2026", y2026, 6, 5, 9, Needs(Carries, Column), junk: 3);
    }

    static void Chapter6()
    {
        // 6-1 Print: OR masks are the only way to make bits.
        var print = new LevelData { id = "6-1", name = "Print",
            start = new[] { 0, 0, 0, 0x80, 0, 0x01, 0, 0 },
            gates = new[] { new RowGate(3, GateOp.Or, 0x80), new RowGate(5, GateOp.Or, 0x01) } };
        Check(Forward(print, R(3, Rt), R(3, Rt), R(3, Rt), R(5, L), R(5, L)), "forward");

        Check(new LevelData { id = "6-2", name = "Four Gates",
            start = new[] { 0, 192, 0, 48, 0, 255, 2, 0 }, target = new[] { 0, 240, 0, 0, 0, 60, 254, 0 },
            gates = new[] { new RowGate(1, GateOp.Or, 128), new RowGate(3, GateOp.Xor, 24), new RowGate(5, GateOp.And, 60), new RowGate(6, GateOp.Not, 1) } }, "existing");

        // 6-3 Cancel: XOR rows toggle; matching bits cancel, gaps turn into bits.
        var cancel = new LevelData { id = "6-3", name = "Cancel",
            start = new[] { 0, 0x66, 0, 0x3C, 0x18, 0x3C, 0, 0 },
            gates = new[] { new RowGate(1, GateOp.Xor, 0x18), new RowGate(3, GateOp.Xor, 0x42), new RowGate(5, GateOp.Xor, 0x24) } };
        Check(Forward(cancel, R(1, Rt), C(3, U), R(3, L), R(5, Rt), R(1, L)), "forward");

        // 6-4 Invert: NOT pads flip a row; 1-IN edges feed new bits.
        var invert = new LevelData { id = "6-4", name = "Invert",
            start = new[] { 0, 0x06, 0, 0x60, 0, 0x0C, 0, 0 },
            fillLeftRows = new[] { 5 },
            gates = new[] { new RowGate(1, GateOp.Not, 0x01), new RowGate(3, GateOp.Not, 0x80) } };
        Check(Forward(invert, R(1, Rt), R(3, L), R(5, Rt), R(5, Rt), C(4, D)), "forward");

        // 6-5 Circuit: gates also fire when a column passes through their row.
        var circuit = new LevelData { id = "6-5", name = "Circuit", gatesOnColumnMoves = true,
            start = new[] { 0x18, 0, 0x81, 0, 0x3C, 0, 0x18, 0 },
            gates = new[] { new RowGate(1, GateOp.Or, 0x24), new RowGate(3, GateOp.Xor, 0x18), new RowGate(5, GateOp.And, 0x7E) } };
        Check(Forward(circuit, C(3, D), R(2, Rt), C(4, D), R(4, L), C(2, D), R(6, L)), "forward");
    }

}
