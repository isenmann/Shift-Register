using System;
using System.Collections.Generic;
using ShiftRegister.Core;

namespace ShiftRegister.Game.Progress
{
    /// <summary>What a solve earned, for the solved banner.</summary>
    public readonly struct LevelResult
    {
        public readonly int Moves;
        public readonly int Par;
        public readonly int Stars;
        public readonly bool IsNewBest;
        /// <summary>Best moves before this solve, or 0 if the level was never solved.</summary>
        public readonly int PreviousBest;

        public LevelResult(int moves, int par, int stars, bool isNewBest, int previousBest)
        {
            Moves = moves;
            Par = par;
            Stars = stars;
            IsNewBest = isNewBest;
            PreviousBest = previousBest;
        }
    }

    /// <summary>
    /// Saved progress, keyed by level id so reordering the catalog keeps it.
    /// Serialisable with JsonUtility (see <see cref="ProgressStore"/>).
    /// </summary>
    [Serializable]
    public sealed class PlayerProgress
    {
        [Serializable]
        public sealed class LevelRecord
        {
            public string id = "";
            public int bestMoves;
            public int stars;
        }

        public int version = 1;
        public string lastLevelId = "";
        public List<LevelRecord> levels = new List<LevelRecord>();
        /// <summary>Levels whose help panel already opened by itself (each chapter's first level).</summary>
        public List<string> helpSeen = new List<string>();

        /// <summary>True the first time it is asked for a level id, then false.</summary>
        public bool MarkHelpSeen(string id)
        {
            if (helpSeen == null) helpSeen = new List<string>();
            if (helpSeen.Contains(id)) return false;
            helpSeen.Add(id);
            return true;
        }

        public LevelRecord Get(string id) => levels.Find(r => r.id == id);

        public bool IsSolved(string id) => Get(id) != null;

        public int StarsFor(string id) => Get(id)?.stars ?? 0;

        public int TotalStars()
        {
            int total = 0;
            foreach (var r in levels) total += r.stars;
            return total;
        }

        /// <summary>Records a solve; keeps the fewest moves and the most stars.</summary>
        public LevelResult Record(string id, int moves, int par)
        {
            int stars = Scoring.Stars(moves, par);
            var record = Get(id);
            if (record == null)
            {
                levels.Add(new LevelRecord { id = id, bestMoves = moves, stars = stars });
                return new LevelResult(moves, par, stars, true, 0);
            }

            int previous = record.bestMoves;
            bool better = moves < previous;
            if (better) record.bestMoves = moves;
            record.stars = Math.Max(record.stars, stars);
            return new LevelResult(moves, par, stars, better, previous);
        }
    }
}
