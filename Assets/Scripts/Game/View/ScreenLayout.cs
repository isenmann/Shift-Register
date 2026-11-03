using System;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// World layout taken from the mockups: one world unit is 46 mockup px, and the board's local unit
    /// is one cell. The layouts are drawn on a base frame, 1280×720 in landscape or 720×1280 in
    /// portrait (mobile only); the visible frame keeps the 720 short side and stretches its long side
    /// with the screen (up to <see cref="MaxLongPx"/>), so 20:9 phones and wide windows get
    /// <see cref="ExtraPx"/> more room instead of empty bars. Centred content stays put; the HUD and
    /// menus move their outer groups out by half the extra on each side.
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

        public const float ShortPx = 720f, BaseLongPx = 1280f;
        /// <summary>The frame stretches to about 21.5:9 at most; beyond that the screen shows a margin.</summary>
        public const float MaxLongPx = 1720f;

        /// <summary>Landscape (layout A3b): 52 px cells, centred at x 596, y 360 of 1280×720.</summary>
        public const float LandscapeBoardScale = 52f / 46f;
        public static readonly Vector2 LandscapeBoardCenterPx = new Vector2(596f, 360f);
        /// <summary>Portrait (P2): 58 px cells, left of the row tags and below the info cards, of 720×1280.</summary>
        public const float PortraitBoardScale = 58f / 46f;
        public static readonly Vector2 PortraitBoardCenterPx = new Vector2(287f, 693f);

        /// <summary>True on a phone or tablet held upright (<see cref="DeviceMode.IsMobile"/> only).</summary>
        public static bool Portrait { get; private set; }

        /// <summary>How much longer than the base frame the visible frame is, in mockup px (0 at 16:9).</summary>
        public static float ExtraPx { get; private set; }

        /// <summary>Raised when the orientation or the frame length changes: rearrange what depends on it.</summary>
        public static event Action LayoutChanged;

        /// <summary>Called every frame by <see cref="CameraFit"/> with the size of the area the game draws into.</summary>
        public static void SetScreen(float width, float height)
        {
            if (width <= 0f || height <= 0f) return;
            bool portrait = Application.isPlaying && DeviceMode.IsMobile && height > width;
            float ratio = portrait ? height / width : width / height;
            float extra = Mathf.Round(Mathf.Clamp(ShortPx * ratio, BaseLongPx, MaxLongPx) - BaseLongPx);
            if (portrait == Portrait && Mathf.Approximately(extra, ExtraPx)) return;
            Portrait = portrait;
            ExtraPx = extra;
            LayoutChanged?.Invoke();
        }

        /// <summary>The base frame the layouts are drawn on.</summary>
        public static float BaseWidthPx => Portrait ? ShortPx : BaseLongPx;
        public static float BaseHeightPx => Portrait ? BaseLongPx : ShortPx;

        /// <summary>The visible frame: the base frame stretched along its long side.</summary>
        public static float FrameWidthPx => Portrait ? ShortPx : BaseLongPx + ExtraPx;
        public static float FrameHeightPx => Portrait ? BaseLongPx + ExtraPx : ShortPx;

        /// <summary>The visible frame in world units, centred on the origin.</summary>
        public static Vector2 FrameSize => new Vector2(FrameWidthPx, FrameHeightPx) / PxPerUnit;

        /// <summary>
        /// How far the outer groups move outward, in mockup px: landscape left/right rails along x,
        /// portrait top/bottom blocks along y. Each side gets half of the extra length.
        /// </summary>
        public static float OuterShiftPx => ExtraPx / 2f;

        public static float BoardScale => Portrait ? PortraitBoardScale : LandscapeBoardScale;
        public static Vector2 BoardCenterPx => Portrait ? PortraitBoardCenterPx : LandscapeBoardCenterPx;
        public static Vector2 BoardCenter => FromMockup(BoardCenterPx.x, BoardCenterPx.y);

        /// <summary>Where lost bits land: the bit bucket card's centre (in an outer group, so it moves with it).</summary>
        public static Vector2 BucketCenter => Portrait
            ? FromMockup(360f, 290f - OuterShiftPx)
            : FromMockup(1144f + OuterShiftPx, 199f);

        /// <summary>Converts base-frame mockup coordinates (origin top-left, y down) to world units.</summary>
        public static Vector2 FromMockup(float x, float y) =>
            new Vector2(x - BaseWidthPx / 2f, BaseHeightPx / 2f - y) / PxPerUnit;
    }
}
