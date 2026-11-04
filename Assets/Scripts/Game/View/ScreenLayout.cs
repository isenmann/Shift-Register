using System;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// World layout taken from the mockups: one world unit is 46 mockup px, and the board's local unit
    /// is one cell. The layouts are drawn on a base frame: 1280×720 on desktop, 412×915 / 915×412 on
    /// phones and tablets (the phone mockups' CSS px, so text keeps its size); the visible frame keeps
    /// the short side and stretches its long side with the screen, so 20:9 phones and wide windows get
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

        /// <summary>Landscape (layout A3b): 52 px cells, centred at x 596, y 360 of 1280×720.</summary>
        public const float LandscapeBoardScale = 52f / 46f;
        public static readonly Vector2 LandscapeBoardCenterPx = new Vector2(596f, 360f);
        /// <summary>Phone portrait (B2): the board spans the width, x 12–399, y 356–743 of 412×915.</summary>
        public const float PhonePortraitBoardScale = 387f / 417f;
        public static readonly Vector2 PhonePortraitBoardCenterPx = new Vector2(205.5f, 549.5f);
        /// <summary>Phone landscape (B3): the board spans the height, x 236–607, y 20–391 of 915×412.</summary>
        public const float PhoneLandscapeBoardScale = 371f / 417f;
        public static readonly Vector2 PhoneLandscapeBoardCenterPx = new Vector2(421.5f, 205.5f);

        /// <summary>
        /// Desktop: 720 short side, 1280 base long side (16:9), stretching to about 21.5:9.
        /// Phones and tablets: 412 × 915 (a 20:9 phone in CSS px), stretching to 1000. A shorter screen
        /// (16:9 phones, or a 20:9 phone whose safe area leaves out the camera cutout) never compresses
        /// the layout, whose blocks have no room to spare: the camera shows the whole frame, a little smaller.
        /// </summary>
        public static float ShortPx => Phone ? 412f : 720f;
        public static float BaseLongPx => Phone ? 915f : 1280f;
        static float MinLongPx => BaseLongPx;
        static float MaxLongPx => Phone ? 1000f : 1720f;

        /// <summary>Phones and tablets in play: the phone layouts (B2 portrait, B3 landscape).</summary>
        public static bool Phone { get; private set; }

        /// <summary>True on a phone or tablet held upright (<see cref="DeviceMode.IsMobile"/> only).</summary>
        public static bool Portrait { get; private set; }

        /// <summary>How much longer than the base frame the visible frame is, in mockup px (0 at 16:9).</summary>
        public static float ExtraPx { get; private set; }

        /// <summary>Raised when the orientation or the frame length changes: rearrange what depends on it.</summary>
        public static event Action LayoutChanged;

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

        public static float BoardScale =>
            !Phone ? LandscapeBoardScale : Portrait ? PhonePortraitBoardScale : PhoneLandscapeBoardScale;
        public static Vector2 BoardCenterPx =>
            !Phone ? LandscapeBoardCenterPx : Portrait ? PhonePortraitBoardCenterPx : PhoneLandscapeBoardCenterPx;
        public static Vector2 BoardCenter => FromMockup(BoardCenterPx.x, BoardCenterPx.y);

        /// <summary>
        /// Where lost bits land: the bit bucket card's centre (in an outer group, so it moves with it).
        /// </summary>
        public static Vector2 BucketCenter => !Phone
            ? FromMockup(1144f + OuterShiftPx, 199f)
            : Portrait ? FromMockup(226f, 196f - OuterShiftPx) : FromMockup(858f + OuterShiftPx, 76f);

        /// <summary>
        /// Called every frame by <see cref="CameraFit"/> with the size of the area the game draws into.
        /// </summary>
        public static void SetScreen(float width, float height)
        {
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            bool phone = Application.isPlaying && DeviceMode.IsMobile;
            bool portrait = phone && height > width;
            float ratio = portrait ? height / width : width / height;
            bool wasPhone = Phone;
            Phone = phone;
            float extra = Mathf.Round(Mathf.Clamp(ShortPx * ratio, MinLongPx, MaxLongPx) - BaseLongPx);
            if (phone == wasPhone && portrait == Portrait && Mathf.Approximately(extra, ExtraPx))
            {
                return;
            }

            Portrait = portrait;
            ExtraPx = extra;
            LayoutChanged?.Invoke();
        }

        /// <summary>Converts base-frame mockup coordinates (origin top-left, y down) to world units.</summary>
        public static Vector2 FromMockup(float x, float y) =>
            new Vector2(x - BaseWidthPx / 2f, BaseHeightPx / 2f - y) / PxPerUnit;
    }
}
