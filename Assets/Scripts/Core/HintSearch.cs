using System.Collections.Generic;

namespace ShiftRegister.Core
{
    public enum HintStatus
    {
        /// <summary>Still looking: keep calling <see cref="HintSearch.Step"/>.</summary>
        Searching,
        /// <summary><see cref="HintSearch.Move"/> is a next move that leads to the solve.</summary>
        Move,
        /// <summary>The board is already solved.</summary>
        AlreadySolved,
        /// <summary>Proven: the level cannot be solved from this board, so a move has to be undone.</summary>
        DeadEnd,
        /// <summary>
        /// The search gave up, so nothing is known about this board. Undoing a move and asking again always
        /// gets somewhere, as the start of a proven level is on its stored solution.
        /// </summary>
        TooHard
    }

    /// <summary>
    /// Finds the next move for the board being played. On the level's stored solution the answer is instant,
    /// so hint after hint walks one path. Anywhere else a search looks for the way back to that path or to the
    /// solve, whichever comes first. It runs in slices, as the web build cannot block.
    /// </summary>
    public sealed class HintSearch
    {
        /// <summary>Smaller than the level tools' limit: a hint must not hold the game's memory.</summary>
        public const int MaxStates = 500_000;

        readonly LevelRules rules;
        readonly ulong board;
        readonly int lost;
        /// <summary>The stored solution, if it solves the level; empty otherwise.</summary>
        readonly IReadOnlyList<Move> stored = System.Array.Empty<Move>();
        /// <summary>The position before each stored move.</summary>
        readonly List<(ulong bits, int lost, int movesLeft)> path = new List<(ulong bits, int lost, int movesLeft)>();
        Solver.SolveJob job;

        public HintStatus Status { get; private set; } = HintStatus.Searching;
        /// <summary>The next move; meaningful only when <see cref="Status"/> is <see cref="HintStatus.Move"/>.</summary>
        public Move Move { get; private set; }
        /// <summary>Moves to the solve along the hinted path, this one included.</summary>
        public int MovesLeft { get; private set; }
        public bool IsDone => Status != HintStatus.Searching;

        public HintSearch(LevelRules rules, ulong board, int lost, string solution, int maxStates = MaxStates)
        {
            this.rules = rules;
            this.board = board;
            this.lost = lost;
            if (rules.IsSolved(board, lost))
            {
                Status = HintStatus.AlreadySolved;
                return;
            }

            if (Move.TryParseList(solution, out var moves) && Solver.Replays(rules, moves))
            {
                stored = moves;
                ulong bits = rules.Start;
                int lostSoFar = 0;
                foreach (var move in moves)
                {
                    path.Add((bits, lostSoFar, moves.Count - path.Count));
                    rules.TryApply(bits, move, out var outcome);
                    bits = outcome.After;
                    lostSoFar += outcome.LostCount;
                }
            }

            int next = PlaceOnPath(board, lost);
            if (next >= 0)
            {
                Answer(stored[next], stored.Count - next);
                return;
            }

            job = Solver.BeginFrom(rules, board, lost, path, maxStates);
            Collect();
        }

        public HintSearch(Board board, string solution, int maxStates = MaxStates)
            : this(board.Rules, board.Bits, board.Lost, solution, maxStates)
        {
        }

        /// <summary>Searches up to <paramref name="maxNodes"/> states. Returns true once there is an answer.</summary>
        public bool Step(int maxNodes)
        {
            if (!IsDone && job.Step(maxNodes))
            {
                Collect();
            }

            return IsDone;
        }

        /// <summary>
        /// The latest place on the stored path with this position, or -1. Latest, so a board that came back to
        /// the path by another way moves forward. The bucket only counts if a loss rule makes it matter.
        /// </summary>
        int PlaceOnPath(ulong bits, int lostSoFar)
        {
            bool trackLost = rules.LossRule != LossRule.Any;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (path[i].bits == bits && (!trackLost || path[i].lost == lostSoFar))
                {
                    return i;
                }
            }

            return -1;
        }

        void Collect()
        {
            if (!job.IsDone)
            {
                return;
            }

            var result = job.Result;
            job = null;
            if (result.Status != SolveStatus.Solved)
            {
                Status = result.Status == SolveStatus.Unsolvable ? HintStatus.DeadEnd : HintStatus.TooHard;
                return;
            }

            ulong bits = board;
            int lostSoFar = lost;
            foreach (var move in result.Solution)
            {
                rules.TryApply(bits, move, out var outcome);
                bits = outcome.After;
                lostSoFar += outcome.LostCount;
            }

            // The search ended on the path, or on the solve, which is not one of the path's positions.
            int place = PlaceOnPath(bits, lostSoFar);
            Answer(result.Solution[0], result.Solution.Count + (place >= 0 ? stored.Count - place : 0));
        }

        void Answer(Move move, int movesLeft)
        {
            Move = move;
            MovesLeft = movesLeft;
            Status = HintStatus.Move;
        }
    }
}
