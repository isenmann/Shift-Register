using ShiftRegister.Core;

namespace ShiftRegister.Tests
{
    /// <summary>Levels from the concept doc and helpers for writing rows as strings.</summary>
    static class TestLevels
    {
        /// <summary>"00110100" → 52. '1' is a bit; any other character is empty.</summary>
        public static int Bits(string row)
        {
            int v = 0;
            for (int i = 0; i < 8; i++)
            {
                if (row[i] == '1')
                {
                    v |= 0x80 >> i;
                }
            }

            return v;
        }

        /// <summary>"01#00110" → 32: the '#' cells.</summary>
        public static int Walls(string row)
        {
            int v = 0;
            for (int i = 0; i < 8; i++)
            {
                if (row[i] == '#')
                {
                    v |= 0x80 >> i;
                }
            }

            return v;
        }

        /// <summary>A level with a single interesting row (row 3), target left empty.</summary>
        public static LevelData OneRow(string row, string walls = null)
        {
            var d = new LevelData { id = "one-row" };
            d.start[3] = Bits(row);
            if (walls != null)
            {
                d.walls[3] = Walls(walls);
            }

            return d;
        }

        /// <summary>2-3 Heart: rows 5-7 need one, two and three left shifts.</summary>
        public static LevelData Heart() => new LevelData
        {
            id = "2-3",
            name = "Heart",
            start = new[] { 0, 102, 255, 255, 63, 15, 3, 0 },
            target = new[] { 0, 102, 255, 255, 126, 60, 24, 0 }
        };

        /// <summary>1-5 Domino: lose 0 bits, solved backwards in 6 moves.</summary>
        public static LevelData Domino() => new LevelData
        {
            id = "1-5",
            name = "Domino",
            start = new[] { 0b00010001, 0, 0, 0b00111000, 0b00010000, 0b00010000, 0, 0b00000001 },
            target = new[] { 0b00000010, 0b00000001, 0b00010000, 0b00111000, 0b00010000, 0, 0, 0b00000010 },
            lossRule = LossRule.AtMost,
            lossCount = 0
        };

        /// <summary>6-2 Four Gates: OR, XOR, AND and NOT rows, par 5.</summary>
        public static LevelData FourGates() => new LevelData
        {
            id = "6-2",
            name = "Four Gates",
            start = new[] { 0, 192, 0, 48, 0, 255, 2, 0 },
            target = new[] { 0, 240, 0, 0, 0, 60, 254, 0 },
            gates = new[]
            {
                new RowGate(1, GateOp.Or, 0b10000000),
                new RowGate(3, GateOp.Xor, 0b00011000),
                new RowGate(5, GateOp.And, 0b00111100),
                new RowGate(6, GateOp.Not, 0b00000001)
            }
        };
    }
}
