using System;
using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.Progress;

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

        public CustomLevel Get(string id) => levels.Find(customLevel => customLevel.level.id == id);

        /// <summary>Adds a copy under a new id. Returns null if the library is full.</summary>
        public CustomLevel Add(LevelData level, LevelProof proof, long nowTicks)
        {
            if (IsFull)
            {
                return null;
            }

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
            if (entry == null)
            {
                return false;
            }

            if (!LevelEdit.SamePuzzle(entry.level, level))
            {
                entry.bestMoves = 0;
            }

            entry.level = LevelEdit.Clone(level);
            entry.proof = proof;
            entry.editedTicks = nowTicks;
            return true;
        }

        /// <summary>Copies a level under a new id, without the best moves. Returns null if full or not found.</summary>
        public CustomLevel Duplicate(string id, long nowTicks)
        {
            var entry = Get(id);
            if (entry == null)
            {
                return null;
            }

            var copy = Add(entry.level, entry.proof, nowTicks);
            if (copy != null)
            {
                string name = entry.level.name.Length > LevelEdit.MaxNameLength - 5
                    ? entry.level.name.Substring(0, LevelEdit.MaxNameLength - 5).TrimEnd()
                    : entry.level.name;
                copy.level.name = name + " COPY";
            }

            return copy;
        }

        public bool Remove(string id) => levels.RemoveAll(customLevel => customLevel.level.id == id) > 0;

        /// <summary>Records a solve; true if it is a new best.</summary>
        public bool RecordSolve(string id, int moves)
        {
            var entry = Get(id);
            if (entry == null || (entry.bestMoves != 0 && entry.bestMoves <= moves))
            {
                return false;
            }

            entry.bestMoves = moves;
            return true;
        }

        /// <summary>
        /// Adds the level in a share code. The code's solution has been replayed by the decoder, so the
        /// level arrives proven by play. Returns null with the error, or with None if the library is full.
        /// </summary>
        public CustomLevel Import(string code, long nowTicks, out ShareCodeError error)
        {
            error = ShareCode.TryDecode(code, out var level);
            if (error != ShareCodeError.None)
            {
                return null;
            }

            return Add(level, LevelProof.Replayed, nowTicks);
        }

        /// <summary>The player-facing words for what is wrong with a share code.</summary>
        public static string ImportMessage(ShareCodeError error)
        {
            switch (error)
            {
                case ShareCodeError.NotACode:
                    return "That is not a share code.";
                case ShareCodeError.UnknownVersion:
                    return "This code needs a newer version of the game.";
                case ShareCodeError.Damaged:
                    return "This code is damaged or cut off.";
                case ShareCodeError.InvalidLevel:
                    return "This code holds a level that breaks the rules.";
                case ShareCodeError.BadSolution:
                    return "This code's solution does not solve its level.";
                default:
                    return "";
            }
        }

        /// <summary>
        /// The player beat a level with these moves. On a level without a proof this proves it, and par
        /// becomes the move count; on one proven by play, fewer moves lower par. A par the solver found
        /// is the true minimum and stays.
        /// </summary>
        /// <param name="justProved">True if this win gave the level its first proof.</param>
        public LevelResult RecordWin(string id, IReadOnlyList<Move> moves, out bool justProved)
        {
            justProved = false;
            var entry = Get(id);
            return entry == null ? default : ApplyWin(entry, moves, out justProved);
        }

        /// <summary>
        /// <see cref="RecordWin"/> for an entry that need not be in a library, such as a random level
        /// that has not been saved.
        /// </summary>
        public static LevelResult ApplyWin(CustomLevel entry, IReadOnlyList<Move> moves, out bool justProved)
        {
            justProved = false;
            var level = entry.level;
            if (Solver.Replays(LevelRules.Compile(level), moves)
                && (entry.proof == LevelProof.None || (entry.proof == LevelProof.Replayed && moves.Count < level.par)))
            {
                justProved = entry.proof == LevelProof.None;
                level.par = moves.Count;
                level.solution = string.Join(" ", moves);
                entry.proof = LevelProof.Replayed;
            }

            int previous = entry.bestMoves;
            bool better = previous == 0 || moves.Count < previous;
            if (better)
            {
                entry.bestMoves = moves.Count;
            }

            return new LevelResult(moves.Count, level.par, Scoring.Stars(moves.Count, level.par), better, previous);
        }

        /// <summary>
        /// Keeps a random level: a copy under a new id, with its best moves. Returns null if the library is full.
        /// </summary>
        public CustomLevel SaveCopy(CustomLevel entry, long nowTicks)
        {
            var copy = Add(entry.level, entry.proof, nowTicks);
            if (copy != null)
            {
                copy.bestMoves = entry.bestMoves;
            }

            return copy;
        }

        /// <summary>
        /// Drops entries that are not valid, or that claim a proof they cannot back up, so damaged saved
        /// data can never reach the game. Returns how many were dropped.
        /// </summary>
        public int RemoveInvalid() => levels.RemoveAll(customLevel =>
            customLevel.level == null
            || !customLevel.level.id.StartsWith(IdPrefix, StringComparison.Ordinal)
            || LevelEdit.AuthoredErrors(customLevel.level).Count > 0
            || (customLevel.proof != LevelProof.None && !HasWorkingSolution(customLevel.level)));

        static bool HasWorkingSolution(LevelData level) =>
            Move.TryParseList(level.solution, out var moves) && Solver.Replays(LevelRules.Compile(level), moves);
    }
}
