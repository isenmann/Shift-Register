using System;
using ShiftRegister.Core;

namespace ShiftRegister.Game.Hud
{
    /// <summary>The "now → target" tag next to each row: OK, or how a plain shift would get there.</summary>
    public static class RowHint
    {
        public const string Ok = "OK";

        /// <summary>
        /// Hints are only honest on plain rows: no walls, rotation, chains, gates or 1-IN edges,
        /// where a shift really is ×2 or ÷2.
        /// </summary>
        public static bool ArithmeticHintsApply(LevelRules rules)
        {
            if (rules.Walls != 0 || rules.HasCreators || rules.HasDestroyers)
            {
                return false;
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                if (rules.IsRotating(row) || rules.ChainTopOf(row) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// What makes a row special, for the tag column: "carry", "wrap", the gate ("AND", "OR", "XOR",
        /// "NOT", "+" when a row has several) or "1-IN"; "" for a plain row.
        /// </summary>
        public static string Label(LevelRules rules, int row)
        {
            if (rules.ChainTopOf(row) >= 0)
            {
                return "carry";
            }

            if (rules.IsRotating(row))
            {
                return "wrap";
            }

            string gate = null;
            int gates = 0;
            foreach (var rowGate in rules.Data.gates ?? Array.Empty<RowGate>())
            {
                if (rowGate.row != row)
                {
                    continue;
                }

                gates++;
                gate ??= rowGate.op.ToString().ToUpperInvariant();
            }

            if (gate != null)
            {
                return gates > 1 ? gate + "+" : gate;
            }

            foreach (int fillRow in rules.Data.fillLeftRows ?? Array.Empty<int>())
            {
                if (fillRow == row)
                {
                    return "1-IN";
                }
            }

            foreach (int fillRow in rules.Data.fillRightRows ?? Array.Empty<int>())
            {
                if (fillRow == row)
                {
                    return "1-IN";
                }
            }

            return "";
        }

        /// <summary>"OK" when equal; "need ×4" / "need ÷2" when that many plain shifts get there; else "".</summary>
        public static string For(int now, int target, bool arithmetic)
        {
            if (now == target)
            {
                return Ok;
            }

            if (!arithmetic || now == 0)
            {
                return "";
            }

            for (int shift = 1; shift < BoardBits.Size; shift++)
            {
                // Only exact arithmetic: no bit may fall off the edge on the way.
                if ((now << shift) == target)
                {
                    return $"need ×{1 << shift}";
                }

                if ((now >> shift) == target && (now & ((1 << shift) - 1)) == 0)
                {
                    return $"need ÷{1 << shift}";
                }
            }

            return "";
        }
    }
}
