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

    /// <summary>A finished random level. <see cref="Level"/> has no id; a library gives it one when it is saved.</summary>
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
        /// <summary>A search this size stays short on every platform; it is what bounds one attempt.</summary>
        internal const int MaxStates = 300_000;
        /// <summary>Far above what any difficulty needs; only here so a job always ends.</summary>
        internal const int MaxAttempts = 500;

        [Flags]
        internal enum Mechanic { None = 0, Walls = 1, Rotate = 2, Pin = 4, Chain = 8 }

        internal sealed class Tier
        {
            public string Name;
            public int Scramble, ParMin, ParMax, StampsMin, StampsMax;
            public Mechanic[] Mixes;
            /// <summary>A search that runs out proves the level is deep, so the scramble's solution can be its par.</summary>
            public bool AcceptUnsearched;
        }

        static readonly Tier Easy = new Tier
        {
            Name = "EASY", Scramble = 5, ParMin = 3, ParMax = 5, StampsMin = 3, StampsMax = 4,
            Mixes = new[] { Mechanic.None }
        };

        static readonly Tier Medium = new Tier
        {
            Name = "MEDIUM", Scramble = 7, ParMin = 5, ParMax = 7, StampsMin = 3, StampsMax = 5,
            Mixes = new[] { Mechanic.Walls, Mechanic.Rotate, Mechanic.Walls | Mechanic.Rotate, Mechanic.Pin | Mechanic.Walls, Mechanic.Pin | Mechanic.Rotate }
        };

        static readonly Tier Hard = new Tier
        {
            Name = "HARD", Scramble = 11, ParMin = 8, ParMax = 11, StampsMin = 4, StampsMax = 6, AcceptUnsearched = true,
            Mixes = new[] { Mechanic.Chain, Mechanic.Chain | Mechanic.Walls, Mechanic.Chain | Mechanic.Rotate, Mechanic.Walls | Mechanic.Rotate, Mechanic.Pin | Mechanic.Walls | Mechanic.Rotate }
        };

        static Tier TierOf(RandomDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RandomDifficulty.Easy: return Easy;
                case RandomDifficulty.Medium: return Medium;
                case RandomDifficulty.Hard: return Hard;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        /// <summary>
        /// Starts making a level, in slices, for places that cannot block (the web build has no threads).
        /// Call <see cref="GeneratorJob.Step"/> until it returns true; to cancel, just drop the job.
        /// </summary>
        public static GeneratorJob Begin(RandomDifficulty difficulty, int seed) => new GeneratorJob(TierOf(difficulty), seed);

        /// <summary>Makes a level in one go. Null in the unlikely case every attempt failed.</summary>
        public static GeneratedLevel Generate(RandomDifficulty difficulty, int seed)
        {
            var job = Begin(difficulty, seed);
            while (!job.Step(int.MaxValue)) { }
            return job.Result;
        }

        /// <summary>One level being made. Tries candidates until one passes or <see cref="MaxAttempts"/> run out.</summary>
        public sealed class GeneratorJob
        {
            readonly Tier tier;
            readonly Random rng;

            // The candidate being searched; null between attempts.
            LevelData candidate;
            LevelRules rules;
            List<Move> scrambleSolution;
            Mechanic mix;
            Solver.SolveJob search;

            /// <summary>The level, or null until <see cref="Step"/> has returned true; still null if every attempt failed.</summary>
            public GeneratedLevel Result { get; private set; }
            public bool IsDone { get; private set; }
            /// <summary>Candidates tried so far.</summary>
            public int Attempts { get; private set; }

            internal GeneratorJob(Tier tier, int seed)
            {
                this.tier = tier;
                rng = new Random(seed);
            }

            /// <summary>Expands up to <paramref name="maxNodes"/> search states. Returns true once the job is done.</summary>
            public bool Step(int maxNodes)
            {
                if (IsDone) return true;
                while (search == null)
                {
                    if (Attempts >= MaxAttempts)
                    {
                        IsDone = true;
                        return true;
                    }
                    NextCandidate();
                }

                if (!search.Step(maxNodes)) return false;
                Judge(search.Result);
                search = null;
                return IsDone;
            }

            /// <summary>Starts the search of a new candidate, or leaves <see cref="search"/> null if it is no good.</summary>
            void NextCandidate()
            {
                Attempts++;
                mix = tier.Mixes[rng.Next(tier.Mixes.Length)];
                candidate = BuildRules(rng, tier, mix);
                if (candidate == null) return;

                // The rules are fixed now, only the start is still the target.
                rules = LevelRules.Compile(candidate);
                scrambleSolution = Scramble(rules, rng, tier.Scramble, out ulong start);
                if (scrambleSolution == null) return;
                candidate.start = BoardBits.ToRows(start);
                if (LevelEdit.AuthoredErrors(candidate).Count > 0) return;

                rules = LevelRules.Compile(candidate);
                if (!UsesEverything(rules, scrambleSolution, mix)) return;
                search = Solver.Begin(rules, MaxStates, tier.Scramble);
            }

            void Judge(SolveResult found)
            {
                if (found.Status == SolveStatus.Solved)
                {
                    if (found.Par < tier.ParMin || found.Par > tier.ParMax) return;
                    if (!UsesEverything(rules, found.Solution, mix)) return;
                    Finish(found.Solution, LevelProof.Searched);
                }
                else if (found.Status == SolveStatus.LimitReached && tier.AcceptUnsearched && Solver.Replays(rules, scrambleSolution))
                {
                    Finish(scrambleSolution, LevelProof.Replayed);
                }
            }

            void Finish(IReadOnlyList<Move> solution, LevelProof proof)
            {
                candidate.par = solution.Count;
                candidate.solution = string.Join(" ", solution);
                candidate.name = $"{tier.Name} {rng.Next(0x1000, 0x10000):X4}";
                Result = new GeneratedLevel(candidate, proof, Attempts);
                IsDone = true;
            }
        }

        // ----- Building a candidate -----

        /// <summary>
        /// The target picture and rules, with start still equal to target. Every row gets at most one rule, as
        /// the rules limit each other (a chained row cannot have walls, a pin locks its row). Null if it did
        /// not fit the board.
        /// </summary>
        static LevelData BuildRules(Random rng, Tier tier, Mechanic mix)
        {
            var d = new LevelData();
            int symmetry = rng.Next(SymmetryCount);
            Stamp(d.target, rng, symmetry, rng.Next(tier.StampsMin, tier.StampsMax + 1));
            d.start = (int[])d.target.Clone();

            var taken = new bool[BoardBits.Size];

            if ((mix & Mechanic.Chain) != 0)
            {
                int top = rng.Next(BoardBits.Size - 1);
                taken[top] = taken[top + 1] = true;
                d.chainTopRows = new[] { top };
            }

            if ((mix & Mechanic.Rotate) != 0)
            {
                var rotate = new List<int>();
                int wanted = rng.Next(1, 3);
                foreach (int r in ShuffledRows(rng))
                {
                    if (rotate.Count == wanted) break;
                    if (taken[r]) continue;
                    taken[r] = true;
                    rotate.Add(r);
                }
                d.rotateRows = rotate.ToArray();
            }

            if ((mix & Mechanic.Pin) != 0 && !PlacePin(d, rng, taken)) return null;
            if ((mix & Mechanic.Walls) != 0 && !PlaceWalls(d, rng, symmetry, taken)) return null;
            return LevelEdit.AuthoredErrors(d).Count == 0 ? d : null;
        }

        const int SymmetryCount = 3;

        /// <summary>A cell and its mirror images under one of the three symmetries the pictures use.</summary>
        static IEnumerable<(int r, int c)> Images(int symmetry, int r, int c)
        {
            int last = BoardBits.Size - 1;
            yield return (r, c);
            switch (symmetry)
            {
                case 0: // left-right
                    yield return (r, last - c);
                    break;
                case 1: // left-right and up-down
                    yield return (r, last - c);
                    yield return (last - r, c);
                    yield return (last - r, last - c);
                    break;
                default: // half a turn
                    yield return (last - r, last - c);
                    break;
            }
        }

        /// <summary>Short strokes rather than single cells, so the target reads as a shape and not as noise.</summary>
        static void Stamp(int[] rows, Random rng, int symmetry, int strokes)
        {
            for (int i = 0; i < strokes; i++)
            {
                bool across = rng.Next(2) == 0;
                int length = rng.Next(2, 5);
                int r = rng.Next(BoardBits.Size - (across ? 0 : length - 1));
                int c = rng.Next(BoardBits.Size - (across ? length - 1 : 0));
                for (int k = 0; k < length; k++)
                    foreach (var (ir, ic) in Images(symmetry, across ? r : r + k, across ? c + k : c))
                        rows[ir] |= 0x80 >> ic;
            }
        }

        static bool PlacePin(LevelData d, Random rng, bool[] taken)
        {
            foreach (int r in ShuffledRows(rng))
            {
                if (taken[r] || d.target[r] == 0) continue;
                int c = rng.Next(BoardBits.Size);
                for (int k = 0; k < BoardBits.Size; k++, c = (c + 1) % BoardBits.Size)
                {
                    int bit = 0x80 >> c;
                    if ((d.target[r] & bit) == 0) continue;
                    d.pins[r] |= bit;
                    taken[r] = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Walls follow the picture's symmetry, and only stand on free rows and empty cells.</summary>
        static bool PlaceWalls(LevelData d, Random rng, int symmetry, bool[] taken)
        {
            int wanted = rng.Next(1, 3), placed = 0;
            for (int tries = 0; tries < 40 && placed < wanted; tries++)
            {
                int r = rng.Next(BoardBits.Size), c = rng.Next(BoardBits.Size);
                bool fits = true;
                foreach (var (ir, ic) in Images(symmetry, r, c))
                    if (taken[ir] || (d.target[ir] & (0x80 >> ic)) != 0) fits = false;
                if (!fits) continue;
                foreach (var (ir, ic) in Images(symmetry, r, c))
                    d.walls[ir] |= 0x80 >> ic;
                placed++;
            }
            return placed > 0;
        }

        static int[] ShuffledRows(Random rng)
        {
            var rows = new int[BoardBits.Size];
            for (int i = 0; i < rows.Length; i++) rows[i] = i;
            for (int i = rows.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (rows[i], rows[j]) = (rows[j], rows[i]);
            }
            return rows;
        }

        // ----- Scrambling -----

        /// <summary>
        /// Plays <paramref name="count"/> moves from the target, losing no bits and never returning to a board
        /// it already passed. Returns the moves that undo them in order, or null if it got stuck.
        /// </summary>
        static List<Move> Scramble(LevelRules rules, Random rng, int count, out ulong board)
        {
            board = rules.Target;
            var moves = rules.Moves;
            var undo = new List<Move>(count);
            if (moves.Count == 0) return null;

            var seen = new HashSet<ulong> { board };
            for (int guard = 0; undo.Count < count && guard < count * 50; guard++)
            {
                var m = moves[rng.Next(moves.Count)];
                if (!rules.TryApply(board, m, out var o) || o.LostCount > 0 || !seen.Add(o.After)) continue;
                board = o.After;
                undo.Add(Opposite(m));
            }
            if (undo.Count < count) return null;
            undo.Reverse();
            return undo;
        }

        static Move Opposite(Move m)
        {
            Direction back;
            switch (m.Direction)
            {
                case Direction.Left: back = Direction.Right; break;
                case Direction.Right: back = Direction.Left; break;
                case Direction.Up: back = Direction.Down; break;
                default: back = Direction.Up; break;
            }
            return new Move(back, m.Line, m.Segment);
        }

        // ----- Is every rule doing something? -----

        /// <summary>Whether a solution relies on walls, wraps and chains; a pin blocks lines just by standing there.</summary>
        static bool UsesEverything(LevelRules rules, IReadOnlyList<Move> solution, Mechanic mix)
        {
            bool split = false, wrap = false, carry = false;
            ulong board = rules.Start;
            foreach (var m in solution)
            {
                if (!rules.TryApply(board, m, out var o)) return false;
                if (rules.SegmentCount(m.Axis, m.Line) > 1) split = true;
                if (m.Axis == Axis.Row)
                {
                    int row = BoardBits.GetRow(board, m.Line);
                    // Rotating rows have no walls here, so the bit that wraps is the one at the row's edge.
                    if (rules.IsRotating(m.Line) && (row & (m.TowardStart ? 0x80 : 0x01)) != 0) wrap = true;
                    int top = rules.ChainTopOf(m.Line);
                    if (top >= 0)
                    {
                        int crossing = m.TowardStart ? BoardBits.GetRow(board, top + 1) & 0x80 : BoardBits.GetRow(board, top) & 0x01;
                        if (crossing != 0) carry = true;
                    }
                }
                board = o.After;
            }
            return ((mix & Mechanic.Walls) == 0 || split)
                && ((mix & Mechanic.Rotate) == 0 || wrap)
                && ((mix & Mechanic.Chain) == 0 || carry);
        }
    }
}
