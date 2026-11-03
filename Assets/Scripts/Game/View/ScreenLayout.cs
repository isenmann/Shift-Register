using System;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// World layout taken from the mockups: one world unit is 46 mockup px, and the board's local unit
    /// is one cell. The camera always shows the whole reference frame, 1280×720 in landscape or
    /// 720×1280 in portrait (mobile only), so the HUD canvases use the same reference resolution.
    /// </summary>
    public static class ScreenLayout
    {
        /// <summary>Mockup pixels per world unit (one cell).</summary>
        public const float PxPerUnit = 46f;

        public const float CellSize = 1f;
        /// <summary>Gap between cells in board-local mockup px.</summary>
        public const float GapPx = 7f;
        /// <summary>Distance between neighbouring cell centres in board-local units (46 px cell + 7 px gap).</summary>
        public const float Pitch = (46f + GapPx) / PxPerUnit;
        public const float BoardSize = (8 * 46f + 7 * GapPx) / PxPerUnit;

        /// <summary>Landscape (layout A3b): 52 px cells, centred at x 596, y 360 of 1280×720.</summary>
        public const float LandscapeBoardScale = 52f / 46f;
        public static readonly Vector2 LandscapeBoardCenterPx = new Vector2(596f, 360f);
        /// <summary>Portrait (P2): 58 px cells, left of the row tags and below the info cards, of 720×1280.</summary>
        public const float PortraitBoardScale = 58f / 46f;
        public static readonly Vector2 PortraitBoardCenterPx = new Vector2(287f, 693f);

        /// <summary>True on a phone or tablet held upright (<see cref="DeviceMode.IsMobile"/> only).</summary>
        public static bool Portrait { get; private set; }

        /// <summary>Raised when <see cref="Portrait"/> flips: rebuild or rearrange what depends on it.</summary>
        public static event Action OrientationChanged;

        /// <summary>Called every frame by <see cref="CameraFit"/> with the screen's size.</summary>
        public static void SetScreen(int width, int height)
        {
            bool portrait = Application.isPlaying && DeviceMode.IsMobile && height > width;
            if (portrait == Portrait) return;
            Portrait = portrait;
            OrientationChanged?.Invoke();
        }

        public static float FrameWidthPx => Portrait ? 720f : 1280f;
        public static float FrameHeightPx => Portrait ? 1280f : 720f;

        /// <summary>The reference frame in world units, centred on the origin.</summary>
        public static Vector2 FrameSize => new Vector2(FrameWidthPx, FrameHeightPx) / PxPerUnit;

        public static float BoardScale => Portrait ? PortraitBoardScale : LandscapeBoardScale;
        public static Vector2 BoardCenterPx => Portrait ? PortraitBoardCenterPx : LandscapeBoardCenterPx;
        public static Vector2 BoardCenter => FromMockup(BoardCenterPx.x, BoardCenterPx.y);

        /// <summary>Where lost bits land: the bit bucket card's centre.</summary>
        public static Vector2 BucketCenter => Portrait ? FromMockup(360f, 290f) : FromMockup(1144f, 199f);

        /// <summary>Converts mockup pixel coordinates (origin top-left, y down) to world units.</summary>
        public static Vector2 FromMockup(float x, float y) =>
            new Vector2(x - FrameWidthPx / 2f, FrameHeightPx / 2f - y) / PxPerUnit;
    }
}
