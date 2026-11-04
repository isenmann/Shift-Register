using System;
using System.Collections.Generic;

namespace ShiftRegister.Core
{
    public enum RandomDifficulty
    {
        /// <summary>Plain board, a few moves. Par is the true minimum.</summary>
        Easy = 0,
        /// <summary>Walls, pins or wrapping rows. Par is the true minimum.</summary>
        Medium = 1,
        /// <summary>Longer, and may chain rows. Too deep to always search, so par can be an upper bound.</summary>
        Hard = 2
    }

    /// <summary>
    /// A finished random level. <see cref="Level"/> has no id; a library gives it one when it is saved.
    /// </summary>
    public sealed class GeneratedLevel
    {
        public LevelData Level { get; }
        public LevelProof Proof { get; }
        /// <summary>How many candidates it took, this one included.</summary>
        public int Attempts { get; }

        public GeneratedLevel(LevelData level, LevelProof proof, int attempts)
        {
            Level = level;
            Proof = proof;
            Attempts = attempts;
        }
    }

    /// <summary>
    /// Makes solvable levels at run time. A candidate is a symmetric target picture scrambled by moves that
    /// lose no bits, so undoing them in reverse always solves it. Rules that move no bit in a solution are
    /// just decoration, so those candidates are thrown away. Gates, 1-IN edges and loss rules are never
    /// used: they can make a move impossible to undo.
    /// </summary>
    public static class LevelGenerator
    {
        [Flags]
        internal enum Mechanic { None = 0, Walls = 1, Rotate = 2, Pin = 4, Chain = 8 }

        internal sealed class Tier
        {
            public string Name;
            public int Scramble, ParMin, ParMax, StampsMin, StampsMax;
            public Mechanic[] Mixes;
            /// <summary>
            /// A search that runs out proves the level is deep, so the scramble's solution can be its par.
            /// </summary>
            public bool AcceptUnsearched;
        }

        /// <summary>A search this size stays short on every platform; it is what bounds one attempt.</summary>
        internal const int MaxStates = 300_000;
        /// <summary>Far above what any difficulty needs; only here so a job always ends.</summary>
        internal const int MaxAttempts = 500;

        static readonly Tier Easy = new Tier
        {
            Name = "EASY",
            Scramble = 5,
            ParMin = 3,
            ParMax = 5,
            StampsMin = 3,
            StampsMax = 4,
            Mixes = new[] { Mechanic.None }
        };

        static readonly Tier Medium = new Tier
        {
            Name = "MEDIUM",
            Scramble = 7,
            ParMin = 5,
            ParMax = 7,
            StampsMin = 3,
            StampsMax = 5,
            Mixes = new[] { Mechanic.Walls, Mechanic.Rotate, Mechanic.Walls | Mechanic.Rotate,
            Mechanic.Pin | Mechanic.Walls, Mechanic.Pin | Mechanic.Rotate }
        };

        static readonly Tier Hard = new Tier
        {
            Name = "HARD",
            Scramble = 11,
            ParMin = 8,
            ParMax = 11,
            StampsMin = 4,
            StampsMax = 6,
            AcceptUnsearched = true,
            Mixes = new[] { Mechanic.Chain, Mechanic.Chain | Mechanic.Walls, Mechanic.Chain | Mechanic.Rotate,
            Mechanic.Walls | Mechanic.Rotate, Mechanic.Pin | Mechanic.Walls | Mechanic.Rotate }
        };

        const int SymmetryCount = 3;

        static Tier TierOf(RandomDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RandomDifficulty.Easy:
                    return Easy;
                case RandomDifficulty.Medium:
                    return Medium;
                case RandomDifficulty.Hard:
                    return Hard;
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        /// <summary>
        /// Starts making a level, in slices, for places that cannot block (the web build has no threads).
        /// Call <see cref="GeneratorJob.Step"/> until it returns true; to cancel, just drop the job.
        /// </summary>
        public static GeneratorJob Begin(RandomDifficulty difficulty, int seed) =>
            new GeneratorJob(TierOf(difficulty), seed);

        /// <summary>Makes a level in one go. Null in the unlikely case every attempt failed.</summary>
        public static GeneratedLevel Generate(RandomDifficulty difficulty, int seed)
        {
            var job = Begin(difficulty, seed);
            while (!job.Step(int.MaxValue))
            {
            }

            return job.Result;
        }

        // ----- Building a candidate -----

        /// <summary>
        /// The target picture and rules, with start still equal to target. Every row gets at most one rule, as
        /// the rules limit each other (a chained row cannot have walls, a pin locks its row). Null if it did
        /// not fit the board.
        /// </summary>
        static LevelData BuildRules(Random random, Tier tier, Mechanic mix)
        {
            var level = new LevelData();
            int symmetry = random.Next(SymmetryCount);
            Stamp(level.target, random, symmetry, random.Next(tier.StampsMin, tier.StampsMax + 1));
            level.start = (int[])level.target.Clone();

            var taken = new bool[BoardBits.Size];

            if ((mix & Mechanic.Chain) != 0)
            {
                int top = random.Next(BoardBits.Size - 1);
                taken[top] = taken[top + 1] = true;
                level.chainTopRows = new[] { top };
            }

            if ((mix & Mechanic.Rotate) != 0)
            {
                var rotate = new List<int>();
                int wanted = random.Next(1, 3);
                foreach (int row in ShuffledRows(random))
                {
                    if (rotate.Count == wanted)
                    {
                        break;
                    }

                    if (taken[row])
                    {
                        continue;
                    }

                    taken[row] = true;
                    rotate.Add(row);
                }

                level.rotateRows = rotate.ToArray();
            }

            if ((mix & Mechanic.Pin) != 0 && !PlacePin(level, random, taken))
            {
                return null;
            }

            if ((mix & Mechanic.Walls) != 0 && !PlaceWalls(level, random, symmetry, taken))
            {
                return null;
            }

            return LevelEdit.AuthoredErrors(level).Count == 0 ? level : null;
        }

        /// <summary>A cell and its mirror images under one of the three symmetries the pictures use.</summary>
        static IEnumerable<(int row, int column)> Images(int symmetry, int row, int column)
        {
            int last = BoardBits.Size - 1;
            yield return (row, column);
            switch (symmetry)
            {
                case 0: // left-right
                    yield return (row, last - column);
                    break;
                case 1: // left-right and up-down
                    yield return (row, last - column);
                    yield return (last - row, column);
                    yield return (last - row, last - column);
                    break;
                default: // half a turn
                    yield return (last - row, last - column);
                    break;
            }
        }

        /// <summary>Short strokes rather than single cells, so the target reads as a shape and not as noise.</summary>
        static void Stamp(int[] rows, Random random, int symmetry, int strokes)
        {
            for (int i = 0; i < strokes; i++)
            {
                bool across = random.Next(2) == 0;
                int length = random.Next(2, 5);
                int row = random.Next(BoardBits.Size - (across ? 0 : length - 1));
                int column = random.Next(BoardBits.Size - (across ? length - 1 : 0));
                for (int step = 0; step < length; step++)
                {
                    foreach (var (imageRow, imageColumn) in Images(symmetry, across ? row : row + step, across
                        ? column + step
                        : column))
                    {
                        rows[imageRow] |= 0x80 >> imageColumn;
                    }
                }
            }
        }

        static bool PlacePin(LevelData level, Random random, bool[] taken)
        {
            foreach (int row in ShuffledRows(random))
            {
                if (taken[row] || level.target[row] == 0)
                {
                    continue;
                }

                int column = random.Next(BoardBits.Size);
                for (int attempt = 0; attempt < BoardBits.Size; attempt++, column = (column + 1) % BoardBits.Size)
                {
                    int bit = 0x80 >> column;
                    if ((level.target[row] & bit) == 0)
                    {
                        continue;
                    }

                    level.pins[row] |= bit;
                    taken[row] = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Walls follow the picture's symmetry, and only stand on free rows and empty cells.</summary>
        static bool PlaceWalls(LevelData level, Random random, int symmetry, bool[] taken)
        {
            int wanted = random.Next(1, 3), placed = 0;
            for (int tries = 0; tries < 40 && placed < wanted; tries++)
            {
                int row = random.Next(BoardBits.Size), column = random.Next(BoardBits.Size);
                bool fits = true;
                foreach (var (imageRow, imageColumn) in Images(symmetry, row, column))
                {
                    if (taken[imageRow] || (level.target[imageRow] & (0x80 >> imageColumn)) != 0)
                    {
                        fits = false;
                    }
                }

                if (!fits)
                {
                    continue;
                }

                foreach (var (imageRow, imageColumn) in Images(symmetry, row, column))
                {
                    level.walls[imageRow] |= 0x80 >> imageColumn;
                }

                placed++;
            }

            return placed > 0;
        }

        static int[] ShuffledRows(Random random)
        {
            var rows = new int[BoardBits.Size];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = i;
            }

            for (int i = rows.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (rows[i], rows[j]) = (rows[j], rows[i]);
            }

            return rows;
        }

        // ----- Scrambling -----

        /// <summary>
        /// Plays <paramref name="count"/> moves from the target, losing no bits and never returning to a board
        /// it already passed. Returns the moves that undo them in order, or null if it got stuck.
        /// </summary>
        static List<Move> Scramble(LevelRules rules, Random random, int count, out ulong board)
        {
            board = rules.Target;
            var moves = rules.Moves;
            var undo = new List<Move>(count);
            if (moves.Count == 0)
            {
                return null;
            }

            var seen = new HashSet<ulong> { board };
            for (int guard = 0; undo.Count < count && guard < count * 50; guard++)
            {
                var move = moves[random.Next(moves.Count)];
                if (!rules.TryApply(board, move, out var outcome) || outcome.LostCount > 0 || !seen.Add(outcome.After))
                {
                    continue;
                }

                board = outcome.After;
                undo.Add(Opposite(move));
            }

            if (undo.Count < count)
            {
                return null;
            }

            undo.Reverse();
            return undo;
        }

        static Move Opposite(Move move)
        {
            Direction back;
            switch (move.Direction)
            {
                case Direction.Left:
                    back = Direction.Right;
                    break;
                case Direction.Right:
                    back = Direction.Left;
                    break;
                case Direction.Up:
                    back = Direction.Down;
                    break;
                default:
                    back = Direction.Up;
                    break;
            }

            return new Move(back, move.Line, move.Segment);
        }

        // ----- Is every rule doing something? -----

        /// <summary>
        /// Whether a solution relies on walls, wraps and chains; a pin blocks lines just by standing there.
        /// </summary>
        static bool UsesEverything(LevelRules rules, IReadOnlyList<Move> solution, Mechanic mix)
        {
            bool split = false, wrap = false, carry = false;
            ulong board = rules.Start;
            foreach (var move in solution)
            {
                if (!rules.TryApply(board, move, out var outcome))
                {
                    return false;
                }

                if (rules.SegmentCount(move.Axis, move.Line) > 1)
                {
                    split = true;
                }

                if (move.Axis == Axis.Row)
                {
                    int row = BoardBits.GetRow(board, move.Line);
                    // Rotating rows have no walls here, so the bit that wraps is the one at the row's edge.
                    if (rules.IsRotating(move.Line) && (row & (move.TowardStart ? 0x80 : 0x01)) != 0)
                    {
                        wrap = true;
                    }

                    int top = rules.ChainTopOf(move.Line);
                    if (top >= 0)
                    {
                        int crossing = move.TowardStart ? BoardBits.GetRow(board, top + 1) & 0x80
                            : BoardBits.GetRow(board, top) & 0x01;
                        if (crossing != 0)
                        {
                            carry = true;
                        }
                    }
                }

                board = outcome.After;
            }

            return ((mix & Mechanic.Walls) == 0 || split)
                && ((mix & Mechanic.Rotate) == 0 || wrap)
                && ((mix & Mechanic.Chain) == 0 || carry);
        }

        /// <summary>
        /// One level being made. Tries candidates until one passes or <see cref="MaxAttempts"/> run out.
        /// </summary>
        public sealed class GeneratorJob
        {
            readonly Tier tier;
            readonly Random random;

            // The candidate being searched; null between attempts.
            LevelData candidate;
            LevelRules rules;
            List<Move> scrambleSolution;
            Mechanic mix;
            Solver.SolveJob search;

            /// <summary>
            /// The level, or null until <see cref="Step"/> has returned true; still null if every attempt failed.
            /// </summary>
            public GeneratedLevel Result { get; private set; }
            public bool IsDone { get; private set; }
            /// <summary>Candidates tried so far.</summary>
            public int Attempts { get; private set; }

            internal GeneratorJob(Tier tier, int seed)
            {
                this.tier = tier;
                random = new Random(seed);
            }

            /// <summary>
            /// Expands up to <paramref name="maxNodes"/> search states. Returns true once the job is done.
            /// </summary>
            public bool Step(int maxNodes)
            {
                if (IsDone)
                {
                    return true;
                }

                while (search == null)
                {
                    if (Attempts >= MaxAttempts)
                    {
                        IsDone = true;
                        return true;
                    }

                    NextCandidate();
                }

                if (!search.Step(maxNodes))
                {
                    return false;
                }

                Judge(search.Result);
                search = null;
                return IsDone;
            }

            /// <summary>
            /// Starts the search of a new candidate, or leaves <see cref="search"/> null if it is no good.
            /// </summary>
            void NextCandidate()
            {
                Attempts++;
                mix = tier.Mixes[random.Next(tier.Mixes.Length)];
                candidate = BuildRules(random, tier, mix);
                if (candidate == null)
                {
                    return;
                }

                // The rules are fixed now, only the start is still the target.
                rules = LevelRules.Compile(candidate);
                scrambleSolution = Scramble(rules, random, tier.Scramble, out ulong start);
                if (scrambleSolution == null)
                {
                    return;
                }

                candidate.start = BoardBits.ToRows(start);
                if (LevelEdit.AuthoredErrors(candidate).Count > 0)
                {
                    return;
                }

                rules = LevelRules.Compile(candidate);
                if (!UsesEverything(rules, scrambleSolution, mix))
                {
                    return;
                }

                search = Solver.Begin(rules, MaxStates, tier.Scramble);
            }

            void Judge(SolveResult found)
            {
                if (found.Status == SolveStatus.Solved)
                {
                    if (found.Par < tier.ParMin || found.Par > tier.ParMax)
                    {
                        return;
                    }

                    if (!UsesEverything(rules, found.Solution, mix))
                    {
                        return;
                    }

                    Finish(found.Solution, LevelProof.Searched);
                }
                else if (found.Status == SolveStatus.LimitReached && tier.AcceptUnsearched
                && Solver.Replays(rules, scrambleSolution))
                {
                    Finish(scrambleSolution, LevelProof.Replayed);
                }
            }

            void Finish(IReadOnlyList<Move> solution, LevelProof proof)
            {
                candidate.par = solution.Count;
                candidate.solution = string.Join(" ", solution);
                candidate.name = $"{tier.Name} {random.Next(0x1000, 0x10000):X4}";
                Result = new GeneratedLevel(candidate, proof, Attempts);
                IsDone = true;
            }
        }
    }
}
