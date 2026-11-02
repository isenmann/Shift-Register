using System.Collections.Generic;

namespace ShiftRegister.Core
{
    /// <summary>
    /// A level being played: current bits, bucket count, move counter and undo history.
    /// Moves are refused once the level is solved or a loss rule is broken; undo is always available.
    /// </summary>
    public sealed class Board
    {
        readonly struct Snapshot
        {
            public readonly ulong Bits;
            public readonly int Lost;
            public Snapshot(ulong bits, int lost) { Bits = bits; Lost = lost; }
        }

        readonly List<Snapshot> history = new List<Snapshot>();

        public LevelRules Rules { get; }
        public ulong Bits { get; private set; }
        /// <summary>Bits in the bit bucket.</summary>
        public int Lost { get; private set; }
        /// <summary>Moves made, not counting undone ones.</summary>
        public int MoveCount => history.Count;

        public bool IsSolved => Rules.IsSolved(Bits, Lost);
        public bool IsRuleBroken => Rules.IsBroken(Lost);
        public bool CanUndo => history.Count > 0;

        public Board(LevelRules rules)
        {
            Rules = rules;
            Bits = rules.Start;
        }

        public Board(LevelData data) : this(LevelRules.Compile(data)) { }

        public bool GetCell(int row, int column) => BoardBits.GetCell(Bits, row, column);
        public byte GetRow(int row) => BoardBits.GetRow(Bits, row);

        public bool CanMove(Move move) => !IsSolved && !IsRuleBroken && Rules.CanMove(move);

        public bool TryMove(Move move, out MoveOutcome outcome)
        {
            if (!CanMove(move) || !Rules.TryApply(Bits, move, out outcome))
            {
                outcome = default;
                return false;
            }
            history.Add(new Snapshot(Bits, Lost));
            Bits = outcome.After;
            Lost += outcome.LostCount;
            return true;
        }

        public bool Undo()
        {
            if (history.Count == 0) return false;
            var s = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            Bits = s.Bits;
            Lost = s.Lost;
            return true;
        }

        public void Reset()
        {
            history.Clear();
            Bits = Rules.Start;
            Lost = 0;
        }
    }
}
