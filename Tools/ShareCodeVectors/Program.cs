using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ShiftRegister.Core;

/// <summary>
/// Writes test vectors for Web/public/sharecode.js: share codes with what ShareCode.TryDecode says about them.
/// The codes are the campaign levels, random levels using every mechanic, those codes with damaged bytes and a
/// correct checksum (so they reach the rule check and the replay), and some text that is no code at all.
/// Usage, from this folder: dotnet run [output file] [damaged codes] [random levels] [seed]
/// </summary>
static class Program
{
    const string NameChars = "ABCXYZabcxyz0189 -.!?'";

    static Random random;

    static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "../../Web/test/vectors.json";
        int damagedCount = args.Length > 1 ? int.Parse(args[1]) : 300;
        int randomCount = args.Length > 2 ? int.Parse(args[2]) : 60;
        random = new Random(args.Length > 3 ? int.Parse(args[3]) : 2026);

        var pool = new List<string>();
        pool.AddRange(CampaignCodes("../../Assets/Levels"));
        int campaign = pool.Count;
        while (pool.Count < campaign + randomCount)
        {
            string code = RandomCode();
            if (code != null)
            {
                pool.Add(code);
            }
        }

        var codes = new List<string>(pool);
        for (int i = 0; i < damagedCount; i++)
        {
            codes.Add(Damage(pool[random.Next(pool.Count)]));
        }

        codes.AddRange(TextCases(pool[0]));
        codes.AddRange(HiddenBitCases(pool));
        codes.AddRange(HandmadeCases());

        var counts = new Dictionary<string, int>();
        var text = new StringBuilder("[\n");
        for (int i = 0; i < codes.Count; i++)
        {
            var error = ShareCode.TryDecode(codes[i], out var level);
            counts[error.ToString()] = counts.GetValueOrDefault(error.ToString()) + 1;
            text.Append(Vector(codes[i], error, level)).Append(i < codes.Count - 1 ? ",\n" : "\n");
        }

        text.Append("]\n");
        File.WriteAllText(output, text.ToString());
        Console.WriteLine($"{codes.Count} vectors ({campaign} campaign levels): "
            + string.Join(", ", counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}")));
        return 0;
    }

    static IEnumerable<string> CampaignCodes(string folder)
    {
        var options = new JsonSerializerOptions { IncludeFields = true };
        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(name => name))
        {
            var level = JsonSerializer.Deserialize<LevelData>(File.ReadAllText(file), options);
            if (ShareCode.CanEncode(level, out _))
            {
                yield return ShareCode.Encode(level);
            }
        }
    }

    /// <summary>A random level whose target is where some random moves end up. Null if it came out invalid.</summary>
    static string RandomCode()
    {
        var level = new LevelData { name = RandomName() };
        for (int row = 0; row < BoardBits.Size; row++)
        {
            level.walls[row] = Chance(25) ? 1 << random.Next(8) : 0;
            level.start[row] = random.Next(256) & ~level.walls[row];
            level.pins[row] = Chance(8) ? level.start[row] & (1 << random.Next(8)) : 0;
        }

        level.rotateRows = RandomRows(20);
        level.fillLeftRows = RandomRows(12);
        level.fillRightRows = RandomRows(12);
        level.chainTopRows = Chance(35) ? new[] { random.Next(7) } : Array.Empty<int>();
        if (level.chainTopRows.Length > 0)
        {
            level.walls[level.chainTopRows[0]] = level.walls[level.chainTopRows[0] + 1] = 0;
        }

        var gates = new List<RowGate>();
        foreach (int row in RandomRows(25))
        {
            gates.Add(new RowGate(row, (GateOp)random.Next(4), (1 + random.Next(255)) & ~level.walls[row]));
        }

        level.gates = gates.ToArray();
        level.gatesOnColumnMoves = Chance(40);
        level.target = (int[])level.start.Clone();
        if (LevelEdit.AuthoredErrors(level).Count > 0)
        {
            return null;
        }

        var rules = LevelRules.Compile(level);
        if (rules.Moves.Count == 0)
        {
            return null;
        }

        ulong board = rules.Start;
        int lost = 0;
        var moves = new List<Move>();
        int moveCount = Chance(5) ? 255 : 1 + random.Next(30);
        for (int i = 0; i < moveCount; i++)
        {
            var move = rules.Moves[random.Next(rules.Moves.Count)];
            if (rules.TryApply(board, move, out var outcome))
            {
                board = outcome.After;
                lost += outcome.LostCount;
                moves.Add(move);
            }
        }

        level.target = BoardBits.ToRows(board);
        level.solution = string.Join(" ", moves);
        level.lossRule = (LossRule)random.Next(3);
        level.lossCount = level.lossRule == LossRule.Any ? 0 : lost + random.Next(2) * (level.lossRule == LossRule.AtMost ? 1 : 0);
        return lost <= LevelEdit.MaxLossCount && ShareCode.CanEncode(level, out _) ? ShareCode.Encode(level) : null;
    }

    static int[] RandomRows(int percent) =>
        Enumerable.Range(0, BoardBits.Size).Where(_ => Chance(percent)).ToArray();

    static bool Chance(int percent) => random.Next(100) < percent;

    static string RandomName()
    {
        var name = new StringBuilder();
        for (int i = random.Next(LevelEdit.MaxNameLength + 1); i > 0; i--)
        {
            name.Append(NameChars[random.Next(NameChars.Length)]);
        }

        return name.ToString();
    }

    /// <summary>Changes bytes of a code and repairs its checksum.</summary>
    static string Damage(string code)
    {
        var bytes = FromBase64Url(code.Substring(ShareCode.Prefix.Length)).ToList();
        bytes.RemoveRange(bytes.Count - 2, 2);
        int edits = 1 + random.Next(3);
        for (int i = 0; i < edits && bytes.Count > 0; i++)
        {
            int at = random.Next(bytes.Count);
            switch (random.Next(5))
            {
                case 0:
                    bytes[at] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    bytes[at] = (byte)random.Next(256);
                    break;
                case 2:
                    bytes.RemoveAt(at);
                    break;
                case 3:
                    bytes.Insert(at, (byte)random.Next(256));
                    break;
                default:
                    // The first bytes are the flags and the loss rule: they change how the rest is read.
                    bytes[random.Next(Math.Min(2, bytes.Count))] ^= (byte)(1 << random.Next(8));
                    break;
            }
        }

        int crc = Crc16(bytes);
        bytes.Add((byte)(crc >> 8));
        bytes.Add((byte)crc);
        return ShareCode.Prefix + ToBase64Url(bytes.ToArray());
    }

    static IEnumerable<string> TextCases(string good)
    {
        string body = good.Substring(ShareCode.Prefix.Length);
        yield return "";
        yield return "   ";
        yield return "SR1";
        yield return "SR1-";
        yield return "SR1-A";
        yield return "SR1-AA";
        yield return "hello bits";
        yield return "sr1-" + body;
        yield return "SR2-" + body;
        yield return "SR9-";
        yield return "SRx-" + body;
        yield return "SR\u0663-" + body; // a digit, though not an ASCII one
        yield return " " + good.Substring(0, 6) + "\n\t" + good.Substring(6) + "\u00A0\u2003";
        yield return good + "\uFEFF"; // looks like whitespace to some, but not to C#
        yield return good + "=";
        yield return good + "+";
        yield return good.Substring(0, good.Length - 1);
        yield return good.Substring(0, good.Length - 4);
        yield return good.Replace('-', '+');
        yield return good + good;
    }

    /// <summary>
    /// The last character of a code can hide unused bits, so other characters there spell the same bytes.
    /// Only codes whose length leaves such bits can show this.
    /// </summary>
    static IEnumerable<string> HiddenBitCases(List<string> pool)
    {
        foreach (int remainder in new[] { 2, 3 })
        {
            string good = pool.First(code => (code.Length - ShareCode.Prefix.Length) % 4 == remainder);
            foreach (char last in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_")
            {
                yield return good.Substring(0, good.Length - 1) + last;
            }
        }
    }

    /// <summary>The narrow paths that random levels rarely walk. The verdicts still come from TryDecode.</summary>
    static IEnumerable<string> HandmadeCases()
    {
        const int Pins = 2, Rotate = 4, Chain = 8, FillLeft = 16, FillRight = 32, Gates = 64;
        int[] goRight = { RowMove(1, 0) }; // from the 128 cell of the top row to its neighbour
        int[] Top(int value) => new[] { value, 0, 0, 0, 0, 0, 0, 0 };

        // Rule breakers.
        yield return Code(Rotate | Chain, 0, Top(128), Top(64), new[] { 1, 1 }, goRight); // only one chained row wraps
        yield return Code(Chain | FillRight, 0, Top(128), Top(64), new[] { 1, 1 }, goRight); // 1-IN inside a chain
        yield return Code(Chain | FillLeft, 0, Top(128), Top(64), new[] { 1, 2 }, goRight); // 1-IN inside a chain
        yield return Code(Chain, 0, Top(128), Top(64), new[] { 128 }, goRight); // a chain below the last row
        yield return Code(Chain, 0, Top(128), Top(64), new[] { 3 }, goRight); // two chains sharing a row
        yield return Code(0, 3, Top(128), Top(64), new int[0], goRight); // no such loss rule
        yield return Code(Gates, 0, Top(128), Top(64), new[] { 9 }, goRight); // too many gates, checked while reading
        yield return Code(Gates, 0, Top(128), Top(64), new[] { 1, 4 << 3, 1 }, goRight); // no such gate
        yield return Code(Gates, 0, Top(128), Top(64), new[] { 1, 0, 0 }, goRight); // a gate without cells
        yield return Code(Gates, 0, Top(128), Top(64), new[] { 2, 0, 1, 0, 2 }, goRight); // two gates on one row

        // Chains: all four ways out of a 16-bit register, and moving it by its lower row.
        yield return Code(Chain | FillLeft, 0, new[] { 0, 64, 0, 0, 0, 0, 0, 0 }, new[] { 128, 32, 0, 0, 0, 0, 0, 0 },
            new[] { 1, 1 }, goRight);
        yield return Code(Chain | FillRight, 0, Top(64), new[] { 128, 1, 0, 0, 0, 0, 0, 0 }, new[] { 1, 2 },
            new[] { RowMove(0, 0) });
        yield return Code(Rotate | Chain, 0, new[] { 0, 1, 0, 0, 0, 0, 0, 0 }, Top(128), new[] { 3, 1 }, goRight);
        yield return Code(Rotate | Chain, 0, Top(128), new[] { 0, 1, 0, 0, 0, 0, 0, 0 }, new[] { 3, 1 },
            new[] { RowMove(0, 0) });
        yield return Code(Chain, 0, Top(1), new[] { 0, 128, 0, 0, 0, 0, 0, 0 }, new[] { 1 }, new[] { RowMove(1, 1) });

        // The board refuses moves once the level is solved: this row comes around to its target twice.
        yield return Code(Rotate, 0, Top(128), Top(64), new[] { 1 }, Enumerable.Repeat(goRight[0], 9).ToArray());
        // A pin locks its column: down and back up would otherwise restore it.
        int[] pinStart = { 128, 0, 0, 0, 0, 0, 0, 1 }, pinTarget = { 128, 0, 0, 0, 0, 0, 0, 2 };
        yield return Code(Pins, 0, pinStart, pinTarget, new[] { 1, 128 },
            new[] { ColumnMove(3, 0), ColumnMove(2, 0), RowMove(0, 7) });
        // Other moves that must be refused, and a level without moves or one already solved.
        yield return Code(0, 0, Top(128), Top(64), new int[0], new[] { RowMove(1, 0) | 1 });
        yield return Code(0, 0, Top(128), Top(128), new int[0], goRight);
        yield return Code(0, 0, Top(128), Top(64), new int[0], new int[0]);
        // Names: tags, characters from outside ASCII and too many letters.
        yield return Code(0, 0, Top(128), Top(64), new int[0], goRight, "<b>a</b> & \u00E9 a very long name");
    }

    static int RowMove(int direction, int row) => (direction << 6) | (row << 3);

    /// <summary>Column moves: 2 is up and 3 is down.</summary>
    static int ColumnMove(int direction, int column) => (direction << 6) | (column << 3);

    /// <summary>Writes a code byte by byte, so it can break rules that ShareCode.Encode would refuse.</summary>
    /// <param name="extra">The bytes between target and solution, in the order ShareCode.Encode writes them.</param>
    static string Code(int flags, int loss, int[] start, int[] target, int[] extra, int[] moves, string name = "T")
    {
        var bytes = new List<byte> { (byte)flags, (byte)loss };
        foreach (var rows in new[] { start, target })
        {
            bytes.Add((byte)Enumerable.Range(0, BoardBits.Size).Sum(row => rows[row] != 0 ? 1 << row : 0));
            bytes.AddRange(rows.Where(value => value != 0).Select(value => (byte)value));
        }

        bytes.AddRange(extra.Select(value => (byte)value));
        bytes.Add((byte)moves.Length);
        bytes.AddRange(moves.Select(move => (byte)move));
        bytes.Add((byte)name.Length);
        bytes.AddRange(name.Select(character => (byte)character));
        int crc = Crc16(bytes);
        bytes.Add((byte)(crc >> 8));
        bytes.Add((byte)crc);
        return ShareCode.Prefix + ToBase64Url(bytes.ToArray());
    }

    static string Vector(string code, ShareCodeError error, LevelData level)
    {
        var vector = new Dictionary<string, object> { ["code"] = code, ["error"] = error.ToString() };
        if (level != null)
        {
            vector["level"] = new Dictionary<string, object>
            {
                ["name"] = level.name,
                ["par"] = level.par,
                ["start"] = level.start,
                ["target"] = level.target,
                ["walls"] = level.walls,
                ["pins"] = level.pins,
                ["rotateRows"] = level.rotateRows,
                ["chainTopRows"] = level.chainTopRows,
                ["fillLeftRows"] = level.fillLeftRows,
                ["fillRightRows"] = level.fillRightRows,
                ["gates"] = level.gates.Select(gate => new Dictionary<string, int>
                {
                    ["row"] = gate.row,
                    ["op"] = (int)gate.op,
                    ["mask"] = gate.mask
                }),
                ["gatesOnColumnMoves"] = level.gatesOnColumnMoves,
                ["lossRule"] = (int)level.lossRule,
                ["lossCount"] = level.lossCount
            };
        }

        return JsonSerializer.Serialize(vector);
    }

    static byte[] FromBase64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The same CRC-16/CCITT-FALSE as ShareCode, where it is private.</summary>
    static int Crc16(IReadOnlyList<byte> data)
    {
        int crc = 0xFFFF;
        foreach (byte value in data)
        {
            crc ^= value << 8;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
        }

        return crc;
    }
}
