using System;

namespace ShiftRegister.Core
{
    public static class Scoring
    {
        public const int MaxStars = 3;

        /// <summary>
        /// 3 stars at or under par, 2 within par + max(2, ⌈par/2⌉), otherwise 1 for solving.
        /// A level without a par (0) gives 1 star.
        /// </summary>
        public static int Stars(int moves, int par)
        {
            if (par <= 0) return 1;
            if (moves <= par) return 3;
            int slack = Math.Max(2, (par + 1) / 2);
            return moves <= par + slack ? 2 : 1;
        }
    }
}
