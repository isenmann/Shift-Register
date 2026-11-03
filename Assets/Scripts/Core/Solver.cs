using System;
using System.Collections.Generic;

namespace ShiftRegister.Core
{
    public enum SolveStatus
    {
        Solved,
        /// <summary>The whole reachable state space was searched without finding the target.</summary>
        Unsolvable,
        /// <summary>The state or depth limit was hit first; the level may or may not be solvable.</summary>
        LimitReached
    }

    public sealed class SolveResult
    {
        public SolveStatus Status { get; }
        /// <summary>Fewest moves to solve, or -1 if not solved.</summary>
        public int Par => Status == SolveStatus.Solved ? Solution.Count : -1;
        public IReadOnlyList<Move> Solution { get; }
        public int StatesVisited { get; }

        public SolveResult(SolveStatus status, IReadOnlyList<Move> solution, int statesVisited)
        {
            Status = status;
            Solution = solution ?? Array.Empty<Move>();
            StatesVisited = statesVisited;
        }

        public override string ToString() =>
            Status == SolveStatus.Solved
                ? $"Solved in {Par}: {string.Join(" ", Solution)} ({StatesVisited} states)"
                : $"{Status} ({StatesVisited} states)";
    }

    /// <summary>
    /// Breadth-first search over board states, so the first solution found is a shortest one (the par).
    /// A state is the board plus the bucket count, when a loss rule makes the count matter.
    /// </summary>
    public static class Solver
    {
        public const int DefaultMaxStates = 2_000_000;
        public const int DefaultMaxDepth = 40;

        readonly struct Key : IEquatable<Key>
        {
            readonly ulong bits;
            readonly int lost;
            public Key(ulong bits, int lost) { this.bits = bits; this.lost = lost; }
            public bool Equals(Key o) => bits == o.bits && lost == o.lost;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode()
            {
                ulong h = bits * 0x9E3779B97F4A7C15UL ^ (ulong)lost * 0xC2B2AE3D27D4EB4FUL;
                return (int)(h ^ (h >> 32));
            }
        }

        public static SolveResult Solve(LevelData data, int maxStates = DefaultMaxStates, int maxDepth = DefaultMaxDepth) =>
            Solve(LevelRules.Compile(data), maxStates, maxDepth);

        public static SolveResult Solve(LevelRules rules, int maxStates = DefaultMaxStates, int maxDepth = DefaultMaxDepth)
        {
            var job = Begin(rules, maxStates, maxDepth);
            while (!job.Step(int.MaxValue)) { }
            return job.Result;
        }

        /// <summary>
        /// Starts a search that runs in slices, for places that cannot block (the web build has no threads).
        /// Call <see cref="SolveJob.Step"/> until it returns true; to cancel, just drop the job.
        /// </summary>
        public static SolveJob Begin(LevelRules rules, int maxStates = DefaultMaxStates, int maxDepth = DefaultMaxDepth) =>
            new SolveJob(rules, maxStates, maxDepth);

        /// <summary>
        /// One breadth-first search. <see cref="Solve(LevelRules, int, int)"/> runs it in a single slice, so
        /// a sliced search cannot give a different answer.
        /// </summary>
        public sealed class SolveJob
        {
            readonly LevelRules rules;
            readonly int maxStates, maxDepth;
            readonly bool trackLost;
            readonly int targetCount;

            // Node storage in BFS order; the queue is just an index into these lists.
            List<ulong> bits;
            List<int> lost, parent, via, depth;
            HashSet<Key> seen;
            int head;
            bool cut;

            /// <summary>The answer, or null until <see cref="Step"/> has returned true.</summary>
            public SolveResult Result { get; private set; }
            public bool IsDone => Result != null;
            public int MaxStates => maxStates;
            /// <summary>States found so far.</summary>
            public int StatesVisited => Result != null ? Result.StatesVisited : bits.Count;

            internal SolveJob(LevelRules rules, int maxStates, int maxDepth)
            {
                this.rules = rules;
                this.maxStates = maxStates;
                this.maxDepth = maxDepth;
                trackLost = rules.LossRule != LossRule.Any;
                targetCount = BoardBits.PopCount(rules.Target);
                bits = new List<ulong> { rules.Start };
                lost = new List<int> { 0 };
                parent = new List<int> { -1 };
                via = new List<int> { -1 };
                depth = new List<int> { 0 };
                seen = new HashSet<Key> { new Key(rules.Start, 0) };

                if (rules.IsSolved(rules.Start, 0))
                    Finish(SolveStatus.Solved, Array.Empty<Move>());
            }

            /// <summary>Expands up to <paramref name="maxNodes"/> states. Returns true once <see cref="Result"/> is set.</summary>
            public bool Step(int maxNodes)
            {
                if (Result != null) return true;
                var moves = rules.Moves;
                for (int n = 0; n < maxNodes; n++)
                {
                    if (head >= bits.Count)
                    {
                        Finish(cut ? SolveStatus.LimitReached : SolveStatus.Unsolvable, null);
                        return true;
                    }

                    int node = head++;
                    if (depth[node] >= maxDepth)
                    {
                        cut = true;
                        continue;
                    }

                    ulong board = bits[node];
                    int lostSoFar = lost[node];
                    for (int i = 0; i < moves.Count; i++)
                    {
                        if (!rules.TryApply(board, moves[i], out var o)) continue;
                        int newLost = lostSoFar + o.LostCount;
                        if (rules.IsBroken(newLost)) continue;
                        if (CannotReachTarget(rules, o.After, newLost, targetCount)) continue;

                        var key = new Key(o.After, trackLost ? newLost : 0);
                        if (!seen.Add(key)) continue;

                        bits.Add(o.After);
                        lost.Add(newLost);
                        parent.Add(node);
                        via.Add(i);
                        depth.Add(depth[node] + 1);

                        if (rules.IsSolved(o.After, newLost))
                        {
                            Finish(SolveStatus.Solved, BuildPath(bits.Count - 1, parent, via, moves));
                            return true;
                        }

                        if (bits.Count >= maxStates)
                        {
                            Finish(SolveStatus.LimitReached, null);
                            return true;
                        }
                    }
                }

                // The last node of this slice may have been the last one of the search.
                if (head < bits.Count) return false;
                Finish(cut ? SolveStatus.LimitReached : SolveStatus.Unsolvable, null);
                return true;
            }

            void Finish(SolveStatus status, IReadOnlyList<Move> solution)
            {
                Result = new SolveResult(status, solution, bits.Count);
                // A search can hold hundreds of megabytes; the result needs none of it.
                bits = null;
                lost = parent = via = depth = null;
                seen = null;
            }
        }

        /// <summary>
        /// True if playing exactly these moves solves the level, under the same rules as
        /// <see cref="Board"/>: every move legal, no move after a broken loss rule or after the solve.
        /// This proves a level solvable without searching it. An empty list never counts.
        /// </summary>
        public static bool Replays(LevelRules rules, IReadOnlyList<Move> solution)
        {
            if (solution == null || solution.Count == 0) return false;
            var board = new Board(rules);
            foreach (var move in solution)
                if (!board.TryMove(move, out _)) return false;
            return board.IsSolved;
        }

        /// <summary>Prunes states that provably cannot reach the target.</summary>
        static bool CannotReachTarget(LevelRules rules, ulong board, int lost, int targetCount)
        {
            if (rules.HasCreators) return false;
            int surplus = BoardBits.PopCount(board) - targetCount;
            if (surplus < 0) return true; // nothing can make bits back
            // With no gates at all, the only way to drop surplus bits is the bucket.
            return !rules.HasDestroyers && rules.LossRule != LossRule.Any && lost + surplus > rules.LossCount;
        }

        static IReadOnlyList<Move> BuildPath(int node, List<int> parent, List<int> via, IReadOnlyList<Move> moves)
        {
            var path = new List<Move>();
            for (int n = node; parent[n] >= 0; n = parent[n]) path.Add(moves[via[n]]);
            path.Reverse();
            return path;
        }
    }
}
