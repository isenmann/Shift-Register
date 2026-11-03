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
            if (rules.Walls != 0 || rules.HasCreators || rules.HasDestroyers) return false;
            for (int r = 0; r < BoardBits.Size; r++)
                if (rules.IsRotating(r) || rules.ChainTopOf(r) >= 0) return false;
            return true;
        }

        /// <summary>
        /// What makes a row special, for the tag column: "carry", "wrap", the gate ("AND", "OR", "XOR",
        /// "NOT", "+" when a row has several) or "1-IN"; "" for a plain row.
        /// </summary>
        public static string Label(LevelRules rules, int row)
        {
            if (rules.ChainTopOf(row) >= 0) return "carry";
            if (rules.IsRotating(row)) return "wrap";

            string gate = null;
            int gates = 0;
            foreach (var g in rules.Data.gates ?? Array.Empty<RowGate>())
            {
                if (g.row != row) continue;
                gates++;
                gate ??= g.op.ToString().ToUpperInvariant();
            }
            if (gate != null) return gates > 1 ? gate + "+" : gate;

            foreach (int r in rules.Data.fillLeftRows ?? Array.Empty<int>()) if (r == row) return "1-IN";
            foreach (int r in rules.Data.fillRightRows ?? Array.Empty<int>()) if (r == row) return "1-IN";
            return "";
        }

        /// <summary>"OK" when equal; "need ×4" / "need ÷2" when that many plain shifts get there; else "".</summary>
        public static string For(int now, int target, bool arithmetic)
        {
            if (now == target) return Ok;
            if (!arithmetic || now == 0) return "";
            for (int k = 1; k < BoardBits.Size; k++)
            {
                // Only exact arithmetic: no bit may fall off the edge on the way.
                if ((now << k) == target) return $"need ×{1 << k}";
                if ((now >> k) == target && (now & ((1 << k) - 1)) == 0) return $"need ÷{1 << k}";
            }
            return "";
        }
    }
}
