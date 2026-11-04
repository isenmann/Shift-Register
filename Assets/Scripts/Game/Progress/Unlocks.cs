using System.Collections.Generic;

namespace ShiftRegister.Game.Progress
{
    /// <summary>
    /// Levels open in play order, but a player may skip ahead: a level is unlocked while at most
    /// <see cref="Skips"/> levels before it are unsolved.
    /// </summary>
    public static class Unlocks
    {
        public const int Skips = 1;

        /// <param name="solvedInOrder">Solved flag of every level in play order.</param>
        /// <param name="index">Position of the level in play order.</param>
        public static bool IsUnlocked(IReadOnlyList<bool> solvedInOrder, int index, int skips = Skips)
        {
            int unsolved = 0;
            for (int i = 0; i < index && i < solvedInOrder.Count; i++)
            {
                if (!solvedInOrder[i] && ++unsolved > skips)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
