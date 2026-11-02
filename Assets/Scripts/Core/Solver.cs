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
            if (rules.IsSolved(rules.Start, 0))
                return new SolveResult(SolveStatus.Solved, Array.Empty<Move>(), 1);

            bool trackLost = rules.LossRule != LossRule.Any;
            int targetCount = BoardBits.PopCount(rules.Target);
            var moves = rules.Moves;

            // Node storage in BFS order; the queue is just an index into these lists.
            var bits = new List<ulong> { rules.Start };
            var lost = new List<int> { 0 };
            var parent = new List<int> { -1 };
            var via = new List<int> { -1 };
            var depth = new List<int> { 0 };
            var seen = new HashSet<Key> { new Key(rules.Start, 0) };
            bool cut = false;

            for (int head = 0; head < bits.Count; head++)
            {
                if (depth[head] >= maxDepth)
                {
                    cut = true;
                    continue;
                }

                ulong board = bits[head];
                int lostSoFar = lost[head];
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
                    parent.Add(head);
                    via.Add(i);
                    depth.Add(depth[head] + 1);

                    if (rules.IsSolved(o.After, newLost))
                        return new SolveResult(SolveStatus.Solved, BuildPath(bits.Count - 1, parent, via, moves), bits.Count);

                    if (bits.Count >= maxStates)
                        return new SolveResult(SolveStatus.LimitReached, null, bits.Count);
                }
            }

            return new SolveResult(cut ? SolveStatus.LimitReached : SolveStatus.Unsolvable, null, bits.Count);
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
