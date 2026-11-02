using System;

namespace ShiftRegister.Core
{
    public enum GateOp
    {
        /// <summary>Only bits on the mask survive.</summary>
        And = 0,
        /// <summary>Mask cells are always on.</summary>
        Or = 1,
        /// <summary>Bits on the mask toggle.</summary>
        Xor = 2,
        /// <summary>NOT pad: if a bit sits on any mask cell, the whole row inverts.</summary>
        Not = 3
    }

    public enum LossRule
    {
        /// <summary>Losing bits is allowed.</summary>
        Any = 0,
        /// <summary>Lose at most <see cref="LevelData.lossCount"/> bits ("lose 0 bits" is AtMost 0).</summary>
        AtMost = 1,
        /// <summary>Lose exactly <see cref="LevelData.lossCount"/> bits (bucket goal).</summary>
        Exactly = 2
    }

    [Serializable]
    public sealed class RowGate
    {
        public int row;
        public GateOp op;
        /// <summary>Cells of the row the gate covers, as a row byte (leftmost cell = 128).</summary>
        public int mask;

        public RowGate() { }

        public RowGate(int row, GateOp op, int mask)
        {
            this.row = row;
            this.op = op;
            this.mask = mask;
        }
    }

    /// <summary>
    /// Authorable level definition. Rows are numbers 0..255 with the leftmost cell = 128,
    /// row indices are 0-based (row 0 is the top row). Works with Unity serialization and JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class LevelData
    {
        public string id = "";
        public string name = "";

        public int[] start = new int[BoardBits.Size];
        public int[] target = new int[BoardBits.Size];

        /// <summary>Wall cells per row. Walls hold no bits and split rows and columns into parts that shift on their own.</summary>
        public int[] walls = new int[BoardBits.Size];

        /// <summary>Pinned bits per row. They never move, so their whole row and column are locked. Must be on in start and target.</summary>
        public int[] pins = new int[BoardBits.Size];

        /// <summary>Rows that wrap around instead of dropping bits.</summary>
        public int[] rotateRows = Array.Empty<int>();

        /// <summary>Each entry r chains rows r and r+1 into one 16-bit register (r is the high byte).</summary>
        public int[] chainTopRows = Array.Empty<int>();

        /// <summary>Rows whose left edge is a 1-IN edge: shifting the row right feeds a 1 into its leftmost cell.</summary>
        public int[] fillLeftRows = Array.Empty<int>();

        /// <summary>Rows whose right edge is a 1-IN edge: shifting the row left feeds a 1 into its rightmost cell.</summary>
        public int[] fillRightRows = Array.Empty<int>();

        /// <summary>Gates applied, in list order, to a row after every move of that row.</summary>
        public RowGate[] gates = Array.Empty<RowGate>();

        /// <summary>If true, gates also apply to every row a column move passes through.</summary>
        public bool gatesOnColumnMoves;

        public LossRule lossRule = LossRule.Any;
        public int lossCount;

        /// <summary>Designer par; 0 means use the solver's optimum.</summary>
        public int par;
    }
}
