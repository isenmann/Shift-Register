using System.Diagnostics;
using ShiftRegister.Core;

namespace ShiftRegister.Game.CustomLevels
{
    /// <summary>
    /// The logic of the RANDOM screen: makes one level at a time in slices, so the screen keeps running
    /// on the web build, which has no threads. The finished level is in no library until it is saved.
    /// </summary>
    public sealed class RandomSession
    {
        const int NodesPerCheck = 64;

        LevelGenerator.GeneratorJob job;

        /// <summary>The difficulty last asked for; NEXT RANDOM uses it again.</summary>
        public RandomDifficulty Difficulty { get; private set; } = RandomDifficulty.Easy;
        public bool IsGenerating => job != null;
        /// <summary>The level from the latest <see cref="Tick"/> that finished; null if none, or if it failed.</summary>
        public CustomLevel Level { get; private set; }
        /// <summary>The last run gave up. Unlikely: every difficulty finds a level long before its limit.</summary>
        public bool Failed { get; private set; }

        public void Start(RandomDifficulty difficulty, int seed)
        {
            Difficulty = difficulty;
            Level = null;
            Failed = false;
            job = LevelGenerator.Begin(difficulty, seed);
        }

        /// <summary>False if nothing was being made.</summary>
        public bool Cancel()
        {
            if (job == null) return false;
            job = null;
            return true;
        }

        /// <summary>Works for about this long. Returns true on the call that finishes the level.</summary>
        public bool Tick(double budgetMs)
        {
            if (job == null) return false;
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalMilliseconds < budgetMs)
            {
                if (!job.Step(NodesPerCheck)) continue;
                var made = job.Result;
                job = null;
                Failed = made == null;
                if (made != null) Level = new CustomLevel { level = made.Level, proof = made.Proof };
                return true;
            }
            return false;
        }
    }
}
