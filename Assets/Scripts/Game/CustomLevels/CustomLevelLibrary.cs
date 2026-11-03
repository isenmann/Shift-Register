using System;
using System.Collections.Generic;
using ShiftRegister.Core;

namespace ShiftRegister.Game.CustomLevels
{
    /// <summary>One level from the editor or a share code.</summary>
    [Serializable]
    public sealed class CustomLevel
    {
        public LevelData level = new LevelData();
        /// <summary>How the level was proven solvable; par and solution mean nothing without a proof.</summary>
        public LevelProof proof;
        /// <summary>Fewest moves the player has solved it in; 0 = not solved yet.</summary>
        public int bestMoves;
        /// <summary>UTC ticks of the last edit.</summary>
        public long editedTicks;
    }

    /// <summary>
    /// The player's own levels. Kept apart from <see cref="Progress.PlayerProgress"/>, so they never
    /// count toward stars or unlocks. Serialisable with JsonUtility (see <see cref="CustomLevelStore"/>).
    /// </summary>
    [Serializable]
    public sealed class CustomLevelLibrary
    {
        public const string IdPrefix = "custom-";
        /// <summary>PlayerPrefs has a size cap on web; 50 levels stay far below it.</summary>
        public const int MaxLevels = 50;

        public int version = 1;
        public List<CustomLevel> levels = new List<CustomLevel>();

        public bool IsFull => levels.Count >= MaxLevels;

        public CustomLevel Get(string id) => levels.Find(l => l.level.id == id);

        /// <summary>Adds a copy under a new id. Returns null if the library is full.</summary>
        public CustomLevel Add(LevelData level, LevelProof proof, long nowTicks)
        {
            if (IsFull) return null;
            var copy = LevelEdit.Clone(level);
            copy.id = IdPrefix + Guid.NewGuid().ToString("N");
            var entry = new CustomLevel { level = copy, proof = proof, editedTicks = nowTicks };
            levels.Add(entry);
            return entry;
        }

        /// <summary>Stores an edited level under its id, clearing the best moves when the puzzle changed.</summary>
        public bool Update(LevelData level, LevelProof proof, long nowTicks)
        {
            var entry = Get(level.id);
            if (entry == null) return false;
            if (!LevelEdit.SamePuzzle(entry.level, level)) entry.bestMoves = 0;
            entry.level = LevelEdit.Clone(level);
            entry.proof = proof;
            entry.editedTicks = nowTicks;
            return true;
        }

        /// <summary>Copies a level under a new id, without the best moves. Returns null if full or not found.</summary>
        public CustomLevel Duplicate(string id, long nowTicks)
        {
            var entry = Get(id);
            return entry == null ? null : Add(entry.level, entry.proof, nowTicks);
        }

        public bool Remove(string id) => levels.RemoveAll(l => l.level.id == id) > 0;

        /// <summary>Records a solve; true if it is a new best.</summary>
        public bool RecordSolve(string id, int moves)
        {
            var entry = Get(id);
            if (entry == null || (entry.bestMoves != 0 && entry.bestMoves <= moves)) return false;
            entry.bestMoves = moves;
            return true;
        }

        /// <summary>
        /// Drops entries that are not valid, or that claim a proof they cannot back up, so damaged saved
        /// data can never reach the game. Returns how many were dropped.
        /// </summary>
        public int RemoveInvalid() => levels.RemoveAll(l =>
            l.level == null
            || !l.level.id.StartsWith(IdPrefix, StringComparison.Ordinal)
            || LevelEdit.AuthoredErrors(l.level).Count > 0
            || (l.proof != LevelProof.None && !HasWorkingSolution(l.level)));

        static bool HasWorkingSolution(LevelData d) =>
            Move.TryParseList(d.solution, out var moves) && Solver.Replays(LevelRules.Compile(d), moves);
    }
}
