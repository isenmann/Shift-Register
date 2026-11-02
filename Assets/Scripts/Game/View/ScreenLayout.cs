using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// World layout taken from the 1280×720 gameplay mockup: one world unit is one cell (46 mockup px).
    /// The camera always shows the whole reference frame, so the HUD can use the same reference resolution.
    /// </summary>
    public static class ScreenLayout
    {
        public const float ReferenceWidthPx = 1280f;
        public const float ReferenceHeightPx = 720f;
        /// <summary>Mockup pixels per world unit (one cell).</summary>
        public const float PxPerUnit = 46f;

        public const float CellSize = 1f;
        /// <summary>Distance between neighbouring cell centres (46 px cell + 5 px gap).</summary>
        public const float Pitch = 51f / PxPerUnit;
        public const float BoardSize = (8 * 46f + 7 * 5f) / PxPerUnit;

        /// <summary>The reference frame in world units, centred on the origin.</summary>
        public static Vector2 FrameSize => new Vector2(ReferenceWidthPx, ReferenceHeightPx) / PxPerUnit;

        /// <summary>Board centre: the mockup's board spans x 104–507 px, y 136–539 px.</summary>
        public static Vector2 BoardCenter => FromMockup(305.5f, 337.5f);

        /// <summary>Converts mockup pixel coordinates (origin top-left, y down) to world units.</summary>
        public static Vector2 FromMockup(float x, float y) =>
            new Vector2(x - ReferenceWidthPx / 2f, ReferenceHeightPx / 2f - y) / PxPerUnit;
    }
}
