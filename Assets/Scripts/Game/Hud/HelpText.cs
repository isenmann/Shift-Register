using System.Collections.Generic;
using System.Linq;
using ShiftRegister.Core;

namespace ShiftRegister.Game.Hud
{
    /// <summary>The words of the help panel: the controls, and the mechanics found on a level's board.</summary>
    public static class HelpText
    {
        public readonly struct Entry
        {
            public readonly string Title;
            public readonly string Text;

            public Entry(string title, string text)
            {
                Title = title;
                Text = text;
            }
        }

        public static readonly Entry[] Controls =
        {
            new Entry("Shift + arrows", "shift the row or column under the cursor"),
            new Entry("Arrows / WASD", "move the cursor"),
            new Entry("Click an arrow", "shift that row or column"),
            new Entry("Z · R", "undo · reset the level"),
            new Entry("T", "hint: shows one move, but the solve earns 1 star"),
            new Entry("Enter", "next level, once solved"),
            new Entry("M · Esc · H", "mute · menu · this help")
        };

        /// <summary>Phones and tablets: swiping instead of keys.</summary>
        public static readonly Entry[] TouchControls =
        {
            new Entry("Swipe a row or column", "shift it, one cell per cell dragged"),
            new Entry("Tap a cell", "select its row and column"),
            new Entry("UNDO · RESET", "take a move back · start the level over"),
            new Entry("HINT", "shows one move, but the solve earns 1 star"),
            new Entry("NEXT", "on the banner, once solved"),
            new Entry("MENU · HELP · SOUND", "chapters · this help · mute")
        };

        /// <summary>
        /// The goal, then one entry per mechanic on this board, in the order the chapters introduce them.
        /// </summary>
        public static List<Entry> Mechanics(LevelData level)
        {
            var list = new List<Entry>
            {
                new Entry("GOAL",
                "Shift the bits until the board matches the target picture. Bits pushed off an edge are lost.")
            };

            if (Any(level.walls))
            {
                list.Add(new Entry("WALLS",
                    "Hatched cells hold no bits. They split a row or column into parts that shift on their own."));
            }

            if (Any(level.pins))
            {
                list.Add(new Entry("PINS", "A pinned bit never moves, so its whole row and column are locked."));
            }

            if (level.rotateRows != null && level.rotateRows.Length > 0)
            {
                list.Add(new Entry("WRAP BELT",
                    "A row in a dashed belt wraps around: bits leaving one end come back at the other."));
            }

            if (level.chainTopRows != null && level.chainTopRows.Length > 0)
            {
                list.Add(new Entry("CARRY BELT",
                    "Two rows in one belt shift as one 16-bit register: bits carry over from one row into the other."));
            }

            if ((level.fillLeftRows != null && level.fillLeftRows.Length > 0)
                || (level.fillRightRows != null && level.fillRightRows.Length > 0))
            {
                list.Add(new Entry("1-IN EDGE",
                    "An end cell with an ink bar feeds in a 1 when its row shifts away from that edge."));
            }

            var gates = Gates(level);
            if (gates != null)
            {
                list.Add(gates.Value);
            }

            var bucket = Bucket(level);
            if (bucket != null)
            {
                list.Add(bucket.Value);
            }

            return list;
        }

        static Entry? Gates(LevelData level)
        {
            if (level.gates == null || level.gates.Length == 0)
            {
                return null;
            }

            var parts = new List<string>();
            var ops = level.gates.Select(gate => gate.op).Distinct().OrderBy(op => (int)op);
            foreach (var op in ops)
            {
                parts.Add(op switch
                {
                    GateOp.And => "& AND keeps only bits on its cells",
                    GateOp.Or => "| OR turns its cells on",
                    GateOp.Xor => "^ XOR toggles its cells",
                    _ => "~ NOT inverts the whole row if a bit sits on it"
                });
            }

            string when = level.gatesOnColumnMoves
                ? "They act after every move of their row, and of every column through it."
                : "They act after every move of their row.";
            return new Entry("GATES", string.Join("\n", parts) + "\n" + when);
        }

        static Entry? Bucket(LevelData level)
        {
            switch (level.lossRule)
            {
                case LossRule.AtMost when level.lossCount == 0:
                    return new Entry("BIT BUCKET", "Lose no bits at all: one lost bit breaks the level.");
                case LossRule.AtMost:
                    return new Entry("BIT BUCKET", $"Lose at most {level.lossCount} bits.");
                case LossRule.Exactly:
                    return new Entry("BIT BUCKET",
                        $"Lose exactly {level.lossCount} bits: the bucket is part of the goal.");
                default:
                    return null;
            }
        }

        static bool Any(int[] rows) => rows != null && rows.Any(row => row != 0);
    }
}
