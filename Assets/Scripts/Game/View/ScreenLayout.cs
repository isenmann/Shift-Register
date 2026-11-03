using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// World layout taken from the 1280×720 gameplay mockup: one world unit is 46 mockup px, and the
    /// board's local unit is one cell. The camera always shows the whole reference frame, so the HUD
    /// can use the same reference resolution. Layout A draws the board <see cref="BoardScale"/> larger.
    /// </summary>
    public static class ScreenLayout
    {
        public const float ReferenceWidthPx = 1280f;
        public const float ReferenceHeightPx = 720f;
        /// <summary>Mockup pixels per world unit (one cell).</summary>
        public const float PxPerUnit = 46f;

        public const float CellSize = 1f;
        /// <summary>Gap between cells in board-local mockup px.</summary>
        public const float GapPx = 7f;
        /// <summary>Distance between neighbouring cell centres in board-local units (46 px cell + 7 px gap).</summary>
        public const float Pitch = (46f + GapPx) / PxPerUnit;
        public const float BoardSize = (8 * 46f + 7 * GapPx) / PxPerUnit;
        /// <summary>Layout A shows cells at 52 px (and gaps at about 8 px): the board's scale in the world.</summary>
        public const float BoardScale = 52f / 46f;

        /// <summary>The reference frame in world units, centred on the origin.</summary>
        public static Vector2 FrameSize => new Vector2(ReferenceWidthPx, ReferenceHeightPx) / PxPerUnit;

        /// <summary>Board centre: layout A's cells span x 360–832 px, y 124–596 px.</summary>
        public static Vector2 BoardCenter => FromMockup(596f, 360f);

        /// <summary>Converts mockup pixel coordinates (origin top-left, y down) to world units.</summary>
        public static Vector2 FromMockup(float x, float y) =>
            new Vector2(x - ReferenceWidthPx / 2f, ReferenceHeightPx / 2f - y) / PxPerUnit;
    }
}
