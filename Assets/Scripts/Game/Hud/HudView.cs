using System.Collections;
using System.Text;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The in-level HUD, built from code in two arrangements that are kept up to date together:
    /// landscape (layout A3b from the redesign canvas: left rail with chapter, level, target,
    /// moves/par, outlook, undo/reset; row tags beside the board; right rail aligned to the board
    /// with bit bucket, move log, keys, menu/help/sound) and portrait (P2, mobile only: header, then
    /// target / bucket / move log cards above the board, row tags beside it, big buttons below).
    /// The solved / rule-broken banners are shared and sit over the board in either.
    /// </summary>
    public sealed partial class HudView : MonoBehaviour
    {
        /// <summary>Everything one arrangement shows that changes during play.</summary>
        sealed class Layout
        {
            public RectTransform Root;
            public TextMeshProUGUI ChapterLabel, Rule, LevelName, LevelId, Moves, Par, Outlook;
            public readonly Image[] OutlookStars = new Image[Scoring.MaxStars];
            public readonly Image[] Thumb = new Image[BoardSize * BoardSize];
            public readonly TextMeshProUGUI[] RowBits = new TextMeshProUGUI[BoardSize];
            public readonly TextMeshProUGUI[] RowFrom = new TextMeshProUGUI[BoardSize];
            public readonly TextMeshProUGUI[] RowArrow = new TextMeshProUGUI[BoardSize];
            public readonly TextMeshProUGUI[] RowTo = new TextMeshProUGUI[BoardSize];
            public readonly TextMeshProUGUI[] RowTag = new TextMeshProUGUI[BoardSize];
            public readonly Image[] RowTagBox = new Image[BoardSize];
            public Image BucketFrame;
            public TextMeshProUGUI BucketCount, BucketText, LogEmpty;
            public TextMeshProUGUI[] LogIndex, LogMove;
            public HudButton Sound;
            /// <summary>Phone layouts: compact texts, and the readout of the selected row.</summary>
            public bool Phone;
            public TextMeshProUGUI ReadRow, ReadOk, ReadBits, ReadFrom, ReadArrow, ReadTo, ReadTag;
            /// <summary>Landscape only: the key caps (desktop) or the swipe hint (mobile).</summary>
            public GameObject Keys, SwipeHint;
            /// <summary>
            /// Outer groups that move out into the extra length: landscape left/right rails, portrait top/bottom
            /// blocks.
            /// </summary>
            public RectTransform FirstOuterGroup, SecondOuterGroup;
        }

        /// <summary>
        /// What one board row shows: bits, "now → target" (carry chains read as one 16-bit number), and its tag.
        /// </summary>
        struct RowInfo
        {
            public string Bits, From, Arrow, To, Tag;
            public bool Ok, Hint;
        }

        const int BoardSize = BoardBits.Size;
        const float RowBox = 84f;
        // Move log rows, newest first; the last ones fade out instead of counting what is hidden.
        const int LogRows = 7;
        static readonly float[] LogAlpha = { 1f, 1f, 1f, 1f, 0.55f, 0.3f, 0.12f };
        static readonly float[] ShortLogAlpha = { 1f, 0.6f, 0.3f };

        [SerializeField] GameController game;
        [SerializeField] BoardInput input;
        [SerializeField] ScreenFlow flow;
        [Tooltip("Silkscreen Bold: level title and banner.")]
        [SerializeField] TMP_FontAsset displayBold;
        [Tooltip("Silkscreen Regular: card titles and buttons.")]
        [SerializeField] TMP_FontAsset display;
        [Tooltip("JetBrains Mono Regular (its weight table maps <b> to the Bold asset).")]
        [SerializeField] TMP_FontAsset mono;

        Layout landscape, phoneLandscape, phonePortrait;
        Layout[] layouts;
        int shownReadRow = -1;

        RectTransform bannerFrame, solvedBanner, brokenBanner;
        TextMeshProUGUI solvedText, lastLevelText;
        readonly Image[] stars = new Image[Scoring.MaxStars];
        HudButton nextButton, nextRandomButton, saveRandomButton;
        int shownLost;
        bool solvedShown, brokenShown;
        readonly RowInfo[] rowInfo = new RowInfo[BoardSize];
        int rowsOk;

        Layout Active => !ScreenLayout.Phone ? landscape : ScreenLayout.Portrait ? phonePortrait : phoneLandscape;

        void Awake()
        {
            Ui.RenderWithMainCamera(GetComponent<Canvas>(), 10);
            Build();
        }

        void OnEnable()
        {
            if (game == null)
            {
                return;
            }

            if (Keyboard.current != null)
            {
                Keyboard.current.onTextInput += OnTextInput;
            }

            game.Changed += OnChanged;
            game.SettingsChanged += RefreshSound;
            ScreenLayout.LayoutChanged += ApplyOrientation;
            ApplyOrientation();
            RefreshSound();
            if (game.BoardView != null)
            {
                game.BoardView.Settled += OnSettled;
            }
            // The level may have changed while the HUD was hidden (menus).
            if (game.Board != null)
            {
                solvedShown = brokenShown = false;
                shownLost = game.Board.Lost;
                OnChanged();
            }
        }

        void OnDisable()
        {
            if (game == null)
            {
                return;
            }

            CloseHelp();
            if (Keyboard.current != null)
            {
                Keyboard.current.onTextInput -= OnTextInput;
            }

            game.Changed -= OnChanged;
            game.SettingsChanged -= RefreshSound;
            ScreenLayout.LayoutChanged -= ApplyOrientation;
            if (game.BoardView != null)
            {
                game.BoardView.Settled -= OnSettled;
            }
        }

        void Start()
        {
            if (game != null && game.Board != null)
            {
                OnChanged();
            }
        }

        /// <summary>Board info updates at once; the bucket and banner wait for the move animation to land.</summary>
        void OnChanged()
        {
            CheckFirstHelp();
            RefreshBoard();
            bool animating = game.BoardView != null && game.BoardView.IsAnimating;
            if (!animating)
            {
                RefreshOutcome(false);
            }
        }

        void RefreshSound()
        {
            foreach (var layout in layouts)
            {
                layout.Sound.Text = game.Settings.muted ? "MUTED" : "SOUND";
            }
        }

        void OnSettled()
        {
            if (game.Board != null)
            {
                RefreshOutcome(true);
            }
        }

        void Update()
        {
            UpdateHelp();
            if (ScreenLayout.Phone)
            {
                RefreshReadout();
            }
        }

        /// <summary>Shows the arrangement for the current orientation and moves the shared parts with it.</summary>
        void ApplyOrientation()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx);
            }

            var active = Active;
            foreach (var layout in layouts)
            {
                layout.Root.gameObject.SetActive(layout == active);
            }

            bool mobile = DeviceMode.IsMobile;
            landscape.Keys.SetActive(!mobile);
            landscape.SwipeHint.SetActive(mobile);

            // The extra length of long phones and wide windows goes between the board and the outer groups.
            float shift = ScreenLayout.OuterShiftPx;
            foreach (var layout in new[] { landscape, phoneLandscape })
            {
                layout.FirstOuterGroup.anchoredPosition = new Vector2(-shift, 0f);
                layout.SecondOuterGroup.anchoredPosition = new Vector2(shift, 0f);
            }

            phonePortrait.FirstOuterGroup.anchoredPosition = new Vector2(0f, shift);
            phonePortrait.SecondOuterGroup.anchoredPosition = new Vector2(0f, -shift);

            bannerFrame.sizeDelta = new Vector2(ScreenLayout.BaseWidthPx, ScreenLayout.BaseHeightPx);
            Vector2 centre = ScreenLayout.BoardCenterPx;
            foreach (var banner in new[] { solvedBanner, brokenBanner })
            {
                banner.anchoredPosition = new Vector2(centre.x, -centre.y);
            }

            RefreshHelpOrientation();
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            var canvas = (RectTransform)transform;
            landscape = new Layout { Root = Frame(canvas, "Landscape", 1280, 720) };
            BuildLandscape(landscape);
            phoneLandscape = new Layout { Root = Frame(canvas, "Phone Landscape", 915, 412), Phone = true };
            BuildPhoneLandscape(phoneLandscape);
            phonePortrait = new Layout { Root = Frame(canvas, "Phone Portrait", 412, 915), Phone = true };
            BuildPhonePortrait(phonePortrait);
            layouts = new[] { landscape, phoneLandscape, phonePortrait };

            bannerFrame = Frame(canvas, "Banners", 1280, 720);
            BuildBanner(bannerFrame);
            BuildHelp(canvas);
        }

        /// <summary>A frame-sized child holding one outer group; moving it moves the whole group.</summary>
        static RectTransform Group(Transform frame, string name, float width, float height) =>
            Ui.Rect(frame, name, 0, 0, width, height);

        static RectTransform Frame(RectTransform canvas, string name, float width, float height)
        {
            var frame = Ui.Rect(canvas, name, 0, 0, width, height);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            return frame;
        }

        /// <summary>
        /// Layout A3b, 1280×720: left rail, row tags, right rail aligned to the board (cells y 124–596).
        /// </summary>
        void BuildLandscape(Layout layout)
        {
            var frame = layout.Root;
            var left = layout.FirstOuterGroup = Group(frame, "Left Rail", 1280, 720);
            var right = layout.SecondOuterGroup = Group(frame, "Right Rail", 1280, 720);
            const float x = 48f, width = 232f;
            layout.ChapterLabel = Ui.Text(left, "Chapter", x, 56, width, 18, mono, 13, Lcd.Text3);
            layout.ChapterLabel.characterSpacing = 10;
            layout.Rule = Ui.Text(left, "Chapter Rule", x, 78, width, 54, mono, 12, Lcd.Text2,
                TextAlignmentOptions.TopLeft);
            layout.Rule.textWrappingMode = TextWrappingModes.Normal;
            layout.Rule.lineSpacing = 4;
            layout.LevelName = LevelName(left, x, 134, width, 46, 44);
            layout.LevelId = Ui.Text(left, "Level Id", x, 182, width, 18, mono, 14, Lcd.Text2);

            TargetCard(layout, left, x, 212, width, 237, 20f, 23f);

            const float tileY = 465f, tileWidth = 110f, tileHeight = 50f, parX = x + tileWidth + 12f;
            Ui.Box(left, "Moves Tile", x, tileY, tileWidth, tileHeight, 10f, Lcd.Cell);
            Ui.Text(left, "Moves Label", x + 14, tileY, 60, tileHeight, mono, 12, Lcd.Text3,
                text: "MOVES").characterSpacing = 10;
            layout.Moves = Ui.Text(left, "Moves", x + 14, tileY, tileWidth - 28, tileHeight, display, 30, Lcd.Ink,
                TextAlignmentOptions.MidlineRight);
            Ui.Frame(left, "Par Tile", parX, tileY, tileWidth, tileHeight, 10f, 2f, Lcd.Ink);
            Ui.Text(left, "Par Label", parX + 14, tileY, 60, tileHeight, mono, 12, Lcd.Text3,
                text: "PAR").characterSpacing = 10;
            layout.Par = Ui.Text(left, "Par", parX + 14, tileY, tileWidth - 28, tileHeight, display, 30, Lcd.Ink,
                TextAlignmentOptions.MidlineRight);

            Outlook(layout, left, x, 531, 20f);

            // Bottom edge level with the board's down-arrow row (mockup y 644).
            HudButton.CreateSolid(left, "UNDO", x, 592, tileWidth, 52, display, 15, () => game.Undo());
            HudButton.Create(left, "RESET", parX, 592, tileWidth, 52, display, 15, () => game.ResetLevel());

            RowTags(layout, frame, 892f, ScreenLayout.LandscapeBoardCenterPx.y, ScreenLayout.LandscapeBoardScale);

            const float railX = 1048f, railWidth = 192f;
            Bucket(layout, right, railX, 124, railWidth, 150);
            MoveLog(layout, right, railX, 294, railWidth);

            // Key caps, like the title screen's (desktop); a swipe hint on phones and tablets.
            var keys = Ui.Rect(right, "Keys", 0, 0, 1280, 720);
            layout.Keys = keys.gameObject;
            const float keyRow1 = 504f, keyRow2 = 536f, keyRow3 = 568f;
            float keyX = KeyCap(keys, "⇧", railX, keyRow1, 26f);
            keyX = KeyLabel(keys, "+", keyX, keyRow1, 18f, TextAlignmentOptions.Center);
            keyX = KeyCap(keys, "← → ↑ ↓", keyX, keyRow1, 76f);
            KeyLabel(keys, "shift", keyX + 6f, keyRow1, 50f);
            keyX = KeyCap(keys, "Z", railX, keyRow2, 26f);
            keyX = KeyLabel(keys, "undo", keyX + 6f, keyRow2, 40f);
            keyX = KeyCap(keys, "R", keyX + 8f, keyRow2, 26f);
            KeyLabel(keys, "reset", keyX + 6f, keyRow2, 50f);
            keyX = KeyCap(keys, "H", railX, keyRow3, 26f);
            keyX = KeyLabel(keys, "help", keyX + 6f, keyRow3, 40f);
            keyX = KeyCap(keys, "M", keyX + 8f, keyRow3, 26f);
            KeyLabel(keys, "mute", keyX + 6f, keyRow3, 50f);
            var swipe = Ui.Text(right, "Swipe Hint", railX, 504, railWidth, 90, mono, 13, Lcd.Text3,
                TextAlignmentOptions.TopLeft,
                "Swipe a row or column to shift it. Tap a cell to select it.");
            swipe.textWrappingMode = TextWrappingModes.Normal;
            swipe.lineSpacing = 6;
            layout.SwipeHint = swipe.gameObject;

            const float menuY = 604f;
            HudButton.Create(right, "MENU", railX, menuY, 56, 40, display, 12, () => flow.LeaveLevel());
            HudButton.Create(right, "HELP", railX + 60, menuY, 56, 40, display, 12, ToggleHelp);
            layout.Sound = HudButton.Create(right, "SOUND", railX + 120, menuY, 72, 40, display, 12, ToggleMute);
        }

        /// <summary>
        /// B2, a 20:9 phone held upright (412×915 CSS px): header, the target / bucket / last moves
        /// cards, the readout of the selected row, the board across the full width (no row tags), and
        /// thumb-sized buttons. Text is at least 13 px, buttons at least 48 px tall.
        /// </summary>
        void BuildPhonePortrait(Layout layout)
        {
            var frame = layout.Root;
            var top = layout.FirstOuterGroup = Group(frame, "Top", 412, 915);
            var bottom = layout.SecondOuterGroup = Group(frame, "Bottom", 412, 915);
            layout.ChapterLabel = Ui.Text(top, "Chapter", 12, 20, 236, 16, mono, 13, Lcd.Text3);
            layout.LevelName = LevelName(top, 12, 38, 236, 32, 30);
            PhoneTile(top, "Moves", 250, 20, 70, 56, true, out layout.Moves);
            PhoneTile(top, "Par", 330, 20, 70, 56, false, out layout.Par);
            Outlook(layout, top, 12, 92, 18f, 14f);

            TargetCard(layout, top, 12, 124, 150, 144, 12f, 14f, 12f, 13f, 26f, 6f);
            PhoneBucket(layout, top, 172, 124, 108, 144);
            MoveLog(layout, top, 290, 124, 110, 3, 15f, "MOVES", 30f);

            Readout(layout, top, 12, 280, 388, 64, 18f, 17f, 24f);

            Ui.Text(bottom, "Swipe Hint", 12, 756, 388, 18, mono, 14, Lcd.Text3, TextAlignmentOptions.Center,
                "Swipe to shift · tap a row to read it");
            HudButton.CreateSolid(bottom, "UNDO", 12, 782, 189, 56, display, 20, () => game.Undo());
            HudButton.Create(bottom, "RESET", 211, 782, 189, 56, display, 20, () => game.ResetLevel());
            HudButton.Create(bottom, "MENU", 12, 848, 122, 48, display, 14, () => flow.LeaveLevel());
            HudButton.Create(bottom, "HELP", 145, 848, 122, 48, display, 14, ToggleHelp);
            layout.Sound = HudButton.Create(bottom, "SOUND", 278, 848, 122, 48, display, 14, ToggleMute);
        }

        /// <summary>
        /// B3, a 20:9 phone held sideways (915×412 CSS px): slim left rail (level, moves/par, outlook,
        /// row readout, undo/reset), the board across the full height with compact row tags, and a slim
        /// right rail (target, bucket, last moves, menu/help/sound).
        /// </summary>
        void BuildPhoneLandscape(Layout layout)
        {
            var frame = layout.Root;
            var left = layout.FirstOuterGroup = Group(frame, "Left Rail", 915, 412);
            var right = layout.SecondOuterGroup = Group(frame, "Right Rail", 915, 412);
            layout.ChapterLabel = Ui.Text(left, "Chapter", 16, 20, 206, 16, mono, 13, Lcd.Text3);
            layout.LevelName = LevelName(left, 16, 38, 206, 30, 28);
            PhoneTile(left, "Moves", 16, 84, 99, 54, true, out layout.Moves);
            PhoneTile(left, "Par", 123, 84, 99, 54, false, out layout.Par);
            Outlook(layout, left, 16, 150, 16f, 14f);
            Readout(layout, left, 16, 180, 206, 84, 15f, 15f, 20f);
            HudButton.CreateSolid(left, "UNDO", 16, 336, 99, 56, display, 18, () => game.Undo());
            HudButton.Create(left, "RESET", 123, 336, 99, 56, display, 18, () => game.ResetLevel());

            CompactRowTags(layout, frame, 618f, ScreenLayout.PhoneLandscapeBoardCenterPx.y,
                ScreenLayout.PhoneLandscapeBoardScale);

            // Target and bucket cards side by side, as in portrait, narrower.
            TargetCard(layout, right, 716, 20, 93, 112, 9f, 10f, 7f, 13f, 26f, 6f);
            PhoneBucket(layout, right, 817, 20, 82, 112, withText: false);
            MoveLog(layout, right, 716, 146, 183, 3, 15f, "MOVES", 30f);
            HudButton.Create(right, "MENU", 716, 346, 56, 46, display, 12, () => flow.LeaveLevel());
            HudButton.Create(right, "HELP", 778, 346, 56, 46, display, 12, ToggleHelp);
            layout.Sound = HudButton.Create(right, "SOUND", 840, 346, 59, 46, display, 12, ToggleMute);
        }

        /// <summary>A MOVES (filled) or PAR (outlined) tile with its label over a large value.</summary>
        void PhoneTile(Transform frame, string label, float x, float y, float width, float height, bool filled,
            out TextMeshProUGUI value)
        {
            if (filled)
            {
                Ui.Box(frame, label + " Tile", x, y, width, height, 8f, Lcd.Cell);
            }
            else
            {
                Ui.Frame(frame, label + " Tile", x, y, width, height, 8f, 2f, Lcd.Ink);
            }

            Ui.Text(frame, label + " Label", x + 10, y + 6, width - 20, 14, mono, 12, Lcd.Text3,
                text: label.ToUpperInvariant());
            value = Ui.Text(frame, label, x + 10, y + 22, width - 20, height - 26, display, 24, Lcd.Ink);
        }

        /// <summary>
        /// Bucket card for phones: title, a big count and (in portrait) one short line about the rule.
        /// </summary>
        void PhoneBucket(Layout layout, Transform frame, float x, float y, float width, float height,
            bool withText = true)
        {
            layout.BucketFrame = Ui.Frame(frame, "Bucket Frame", x, y, width, height, 10f, 3f, Lcd.Ink, 8f);
            Ui.Text(frame, "Bucket Title", x + 10, y + 10, width - 20, 16, display, 13, Lcd.Ink, text: "BUCKET");
            layout.BucketCount = Ui.Text(frame, "Bucket Count", x + 10, y + 30, width - 20, 50, display, 40, Lcd.Ink);
            if (!withText)
            {
                return;
            }

            layout.BucketText = Ui.Text(frame, "Bucket Text", x + 10, y + 84, width - 20, height - 92, mono, 13,
                Lcd.Text2, TextAlignmentOptions.TopLeft);
            layout.BucketText.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>
        /// The selected row, large: "ROW n" and how many rows match, the bits spread across the
        /// readout, "now → target" pinned to the same edges, and the tag (OK, ×2, a label).
        /// </summary>
        void Readout(Layout layout, Transform frame, float x, float y, float width, float height, float bitSize,
            float numberSize, float tagSize)
        {
            Ui.Frame(frame, "Readout Frame", x, y, width, height, 10f, 2f, Lcd.Ink);
            bool wide = width > 300f;
            float inner = wide ? x + 98 : x + 12, innerWidth = wide ? width - 170 : width - 76;
            if (wide)
            {
                layout.ReadRow = Ui.Text(frame, "Readout Row", x + 14, y + 12, 80, 18, mono, 13, Lcd.Text3);
                layout.ReadOk = Ui.Text(frame, "Readout OK", x + 14, y + 34, 80, 18, mono, 13, Lcd.Text3);
            }
            else
            {
                layout.ReadRow = Ui.Text(frame, "Readout Row", x + 12, y + 8, 90, 16, mono, 13, Lcd.Text3);
                layout.ReadOk = Ui.Text(frame, "Readout OK", x + width - 102, y + 8, 90, 16, mono, 13, Lcd.Text3,
                    TextAlignmentOptions.MidlineRight);
            }

            float bitsY = wide ? y + 8 : y + 28, numbersY = bitsY + bitSize + 6;
            layout.ReadBits = Ui.Text(frame, "Readout Bits", inner, bitsY, innerWidth + 10, bitSize + 4, mono, bitSize,
                Lcd.Text3);
            layout.ReadBits.characterSpacing = (innerWidth - 8f * 0.6f * bitSize) / 7f / bitSize * 100f;
            layout.ReadFrom = Ui.Text(frame, "Readout Now", inner, numbersY, innerWidth, numberSize + 4, mono,
                numberSize, Lcd.Ink);
            layout.ReadArrow = Ui.Text(frame, "Readout Arrow", inner, numbersY, innerWidth, numberSize + 4, mono,
                numberSize, Lcd.Text3, TextAlignmentOptions.Center, "→");
            layout.ReadTo = Ui.Text(frame, "Readout Target", inner, numbersY, innerWidth, numberSize + 4, mono,
                numberSize, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            float tagWidth = wide ? 56f : 52f;
            layout.ReadTag = Ui.Text(frame, "Readout Tag", x + width - tagWidth - 10, wide ? y + (height - 32) / 2
                : y + 36, tagWidth, 32, mono, tagSize, Lcd.LossText, TextAlignmentOptions.MidlineRight);
            layout.ReadTag.fontStyle = FontStyles.Bold;
        }

        /// <summary>Phone landscape row tags: "now → target" and the tag under it, no bits.</summary>
        void CompactRowTags(Layout layout, Transform frame, float x, float boardCenterY, float scale)
        {
            float pitch = ScreenLayout.Pitch * ScreenLayout.PxPerUnit * scale;
            float firstCenter = boardCenterY - 3.5f * pitch;
            const float width = 86f;
            for (int row = 0; row < BoardSize; row++)
            {
                float centerY = firstCenter + row * pitch;
                layout.RowFrom[row] = Ui.Text(frame, $"Row {row + 1} Now", x, centerY - 17, width, 16, mono, 13,
                    Lcd.Ink);
                layout.RowArrow[row] = Ui.Text(frame, $"Row {row + 1} Arrow", x, centerY - 17, width, 16, mono, 13,
                    Lcd.Text3, TextAlignmentOptions.Center, "→");
                layout.RowTo[row] = Ui.Text(frame, $"Row {row + 1} Target", x, centerY - 17, width, 16, mono, 13,
                    Lcd.Text3, TextAlignmentOptions.MidlineRight);
                layout.RowTagBox[row] = Ui.Box(frame, $"Row {row + 1} Tag Box", x + width - 30, centerY + 1, 30, 16, 3f,
                    Lcd.Ink);
                layout.RowTag[row] = Ui.Text(frame, $"Row {row + 1} Tag", x + width - 30, centerY + 1, 30, 16, mono, 13,
                    Lcd.Light, TextAlignmentOptions.MidlineRight);
            }
        }

        void ToggleMute() => game.ChangeSettings(settings => settings.muted = !settings.muted);

        TextMeshProUGUI LevelName(Transform frame, float x, float y, float width, float height, float size)
        {
            var label = Ui.Text(frame, "Level Name", x, y, width, height, displayBold, size, Lcd.Ink);
            label.enableAutoSizing = true;
            label.fontSizeMin = 20;
            label.fontSizeMax = size;
            return label;
        }

        void TargetCard(Layout layout, Transform frame, float x, float y, float width, float height, float cell,
            float pitch,
            float pad = 16f, float titleSize = 14f, float gridTop = 42f, float titleTop = 12f)
        {
            Ui.Box(frame, "Target Card", x, y, width, height, 10f, Lcd.Cell);
            Ui.Text(frame, "Target Title", x + pad, y + titleTop, 160, 18, display, titleSize, Lcd.Ink, text: "TARGET");
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    layout.Thumb[row * BoardSize + column] = Ui.Box(frame, $"Thumb {row + 1},{column + 1}",
                        x + pad + column * pitch, y + gridTop + row * pitch, cell, cell, 2f, Lcd.Background);
                }
            }
        }

        void Outlook(Layout layout, Transform frame, float x, float y, float star, float textSize = 12f)
        {
            for (int i = 0; i < Scoring.MaxStars; i++)
            {
                float starX = x + i * (star + 6f);
                Ui.Shape(frame, $"Outlook Star {i + 1} Outline", starX, y, star, star,
                    ProceduralSprites.Star(star, 1.6f, ui: true), Lcd.Ink);
                layout.OutlookStars[i] = Ui.Shape(frame, $"Outlook Star {i + 1}", starX, y, star, star,
                    ProceduralSprites.Star(star, ui: true), Lcd.Ink);
            }

            layout.Outlook = Ui.Text(frame, "Outlook", x + Scoring.MaxStars * (star + 6f) + 4f, y - 1, 200, star + 2,
                mono, textSize, Lcd.Text2);
        }

        /// <summary>
        /// One tag per board row, centred on it: the bits spread over a fixed box, under them
        /// "now → target" with the numbers pinned to the box edges and the arrow in its middle;
        /// then OK, the hint or a label.
        /// </summary>
        void RowTags(Layout layout, Transform frame, float x, float boardCenterY, float scale)
        {
            float pitch = ScreenLayout.Pitch * ScreenLayout.PxPerUnit * scale;
            float firstCenter = boardCenterY - 3.5f * pitch;
            // JetBrains Mono advances 0.6 em; spread 8 glyphs so the first and last touch the box edges.
            const float bitSize = 13f;
            float spacingPx = (RowBox - 8f * 0.6f * bitSize) / 7f;
            for (int row = 0; row < BoardSize; row++)
            {
                float centerY = firstCenter + row * pitch;
                layout.RowBits[row] = Ui.Text(frame, $"Row {row + 1} Bits", x, centerY - 20, RowBox + 8, 18, mono,
                    bitSize, Lcd.Text3);
                layout.RowBits[row].characterSpacing = spacingPx / bitSize * 100f;
                layout.RowFrom[row] = Ui.Text(frame, $"Row {row + 1} Now", x, centerY, RowBox, 20, mono, 14, Lcd.Ink);
                layout.RowArrow[row] = Ui.Text(frame, $"Row {row + 1} Arrow", x, centerY, RowBox, 20, mono, 14,
                    Lcd.Text3, TextAlignmentOptions.Center, "→");
                layout.RowTo[row] = Ui.Text(frame, $"Row {row + 1} Target", x, centerY, RowBox, 20, mono, 14, Lcd.Text3,
                    TextAlignmentOptions.MidlineRight);
                float tagX = x + RowBox + 10f;
                layout.RowTagBox[row] = Ui.Box(frame, $"Row {row + 1} Tag Box", tagX, centerY - 10, 34, 20, 4f,
                    Lcd.Ink);
                layout.RowTag[row] = Ui.Text(frame, $"Row {row + 1} Tag", tagX, centerY - 10, 34, 20, mono, 13,
                    Lcd.Light, TextAlignmentOptions.Center);
            }
        }

        void Bucket(Layout layout, Transform frame, float x, float y, float width, float height)
        {
            const float pad = 16f;
            layout.BucketFrame = Ui.Frame(frame, "Bucket Frame", x, y, width, height, 10f, 3f, Lcd.Ink, 8f);
            Ui.Text(frame, "Bucket Title", x + pad, y + 18, 120, 18, display, 14, Lcd.Ink, text: "BIT BUCKET");
            layout.BucketCount = Ui.Text(frame, "Bucket Count", x + pad, y + 10, width - 2 * pad, 34, display, 28,
                Lcd.Ink, TextAlignmentOptions.MidlineRight);
            layout.BucketText = Ui.Text(frame, "Bucket Text", x + pad, y + 52, width - 2 * pad, height - 62, mono, 12,
                Lcd.Text2, TextAlignmentOptions.TopLeft);
            layout.BucketText.textWrappingMode = TextWrappingModes.Normal;
            layout.BucketText.lineSpacing = 4;
        }

        /// <summary>Move log: a light counterweight; newest move first.</summary>
        void MoveLog(Layout layout, Transform frame, float x, float y, float width, int rows = LogRows,
            float size = 13f,
            string title = "MOVES SO FAR", float firstRow = 36f)
        {
            const float row = 22f;
            layout.LogIndex = new TextMeshProUGUI[rows];
            layout.LogMove = new TextMeshProUGUI[rows];
            Ui.Box(frame, "Log Rule", x, y, width, 2, 0f, Lcd.Cell);
            Ui.Text(frame, "Log Title", x, y + 10, width, 16, mono, layout.Phone ? 13
                : 12, Lcd.Text3, text: title).characterSpacing = layout.Phone ? 4 : 10;
            for (int i = 0; i < rows; i++)
            {
                float rowY = y + firstRow + i * row;
                layout.LogIndex[i] = Ui.Text(frame, $"Log {i + 1} Index", x, rowY, 40, row, mono, size, Lcd.Text3);
                layout.LogMove[i] = Ui.Text(frame, $"Log {i + 1} Move", x + 40, rowY, width - 40, row, mono, size,
                    Lcd.Text3, TextAlignmentOptions.MidlineRight);
            }

            layout.LogEmpty = Ui.Text(frame, "Log Empty", x, y + firstRow, width, row, mono, size, Lcd.Cell,
                text: "no moves yet");
        }

        /// <summary>An outlined key cap; returns its right edge.</summary>
        float KeyCap(Transform frame, string text, float x, float y, float width)
        {
            Ui.Frame(frame, $"Key {text}", x, y, width, 26, 5f, 2f, Lcd.Ink);
            Ui.Text(frame, $"Key {text} Label", x, y, width, 26, mono, 12, Lcd.Ink, TextAlignmentOptions.Center, text);
            return x + width;
        }

        float KeyLabel(Transform frame, string text, float x, float y, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            Ui.Text(frame, $"Key Text {text}", x, y, width, 26, mono, 12, Lcd.Text3, align, text);
            return x + width;
        }

        void BuildBanner(Transform frame)
        {
            const float width = 340f;
            solvedBanner = BannerRoot(frame, "Solved Banner", width, 196f);
            Ui.Box(solvedBanner, "Box", 0, 0, width, 196f, 10f, Lcd.Ink);
            Ui.Text(solvedBanner, "Title", 16, 16, width - 32, 44, displayBold, 40, Lcd.Light,
                TextAlignmentOptions.Center, "SOLVED");
            solvedText = Ui.Text(solvedBanner, "Text", 0, 62, width, 22, mono, 15, Lcd.Light,
                TextAlignmentOptions.Center);
            const float star = 30f, gap = 10f;
            float starsX = (width - (Scoring.MaxStars * star + (Scoring.MaxStars - 1) * gap)) / 2f;
            for (int i = 0; i < Scoring.MaxStars; i++)
            {
                float starX = starsX + i * (star + gap);
                Ui.Shape(solvedBanner, $"Star {i + 1} Outline", starX, 94, star, star,
                    ProceduralSprites.Star(star, 2f, ui: true), Lcd.Light);
                stars[i] = Ui.Shape(solvedBanner, $"Star {i + 1}", starX, 94, star, star,
                    ProceduralSprites.Star(star, ui: true), Lcd.Light);
            }

            nextButton = HudButton.Create(solvedBanner, "NEXT →", (width - 150f) / 2f, 140, 150, 40, display, 15,
                Lcd.Light, Lcd.Ink, () => game.NextLevel());
            nextRandomButton = HudButton.Create(solvedBanner, "NEXT RANDOM", 16, 140, 188, 40, display, 15, Lcd.Light,
                Lcd.Ink, () => game.NextLevel());
            saveRandomButton = HudButton.Create(solvedBanner, "SAVE", 212, 140, 112, 40, display, 15, Lcd.Light,
                Lcd.Ink, () => game.SaveRandom());
            lastLevelText = Ui.Text(solvedBanner, "Last Level", 0, 140, width, 40, mono, 13, Lcd.Light,
                TextAlignmentOptions.Center,
                "That's every level so far.");

            brokenBanner = BannerRoot(frame, "Broken Banner", width, 110f);
            Ui.Box(brokenBanner, "Box", 0, 0, width, 110f, 10f, Lcd.Loss);
            var title = Ui.Text(brokenBanner, "Title", 16, 18, width - 32, 44, displayBold, 40, Lcd.Light,
                TextAlignmentOptions.Center, "RULE BROKEN");
            title.enableAutoSizing = true;
            title.fontSizeMin = 20;
            title.fontSizeMax = 40;
            Ui.Text(brokenBanner, "Text", 0, 68, width, 24, mono, 15, Lcd.Light, TextAlignmentOptions.Center,
                "Undo to continue");
        }

        /// <summary>
        /// A hidden banner scaled about its centre; <see cref="ApplyOrientation"/> puts it over the board.
        /// </summary>
        static RectTransform BannerRoot(Transform frame, string name, float width, float height)
        {
            var rectTransform = Ui.Rect(frame, name, 0, 0, width, height);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.gameObject.SetActive(false);
            return rectTransform;
        }

        // ------------------------------------------------------------------ refresh

        void RefreshBoard()
        {
            var board = game.Board;
            var level = game.Level;
            var rules = board.Rules;
            bool arithmetic = RowHint.ArithmeticHintsApply(rules);

            rowsOk = 0;
            for (int row = 0; row < BoardSize; row++)
            {
                int now = board.GetRow(row);
                int target = BoardBits.GetRow(rules.Target, row);
                var info = new RowInfo { Bits = RowString(rules, now, row) };
                if (now == target)
                {
                    rowsOk++;
                }

                string label = RowHint.Label(rules, row);

                int chainTop = rules.ChainTopOf(row);
                bool chainBottom = chainTop >= 0 && chainTop != row;
                if (chainTop >= 0)
                {
                    // A carry chain reads as one 16-bit number: "now" on its top row, "→ target" below.
                    now = (board.GetRow(chainTop) << 8) | board.GetRow(chainTop + 1);
                    target = (BoardBits.GetRow(rules.Target, chainTop) << 8) | BoardBits.GetRow(rules.Target,
                        chainTop + 1);
                }

                info.From = chainBottom ? "" : $"<b>{now}</b>";
                info.Arrow = chainTop >= 0 ? "" : "→";
                info.To = chainTop == row ? "" : chainBottom ? $"→ {target}" : target.ToString();

                string hint = chainBottom ? "" : RowHint.For(now, target, arithmetic).Replace("need ", "");
                info.Ok = hint == RowHint.Ok;
                info.Hint = hint.Length > 0;
                info.Tag = info.Hint ? hint : label;
                rowInfo[row] = info;
            }

            foreach (var layout in layouts)
            {
                if (game.IsRandom)
                {
                    layout.ChapterLabel.text = "RANDOM LEVEL";
                }
                else if (game.IsCustom)
                {
                    layout.ChapterLabel.text = "MY LEVELS";
                }
                else
                {
                    layout.ChapterLabel.text = layout.Phone
                        ? $"CH {game.ChapterIndex + 1} · {game.Chapter.name.ToUpperInvariant()} · {level.id}"
                        : $"CHAPTER {game.ChapterIndex + 1} · {game.Chapter.name.ToUpperInvariant()}";
                }

                if (layout.Rule != null)
                {
                    layout.Rule.text = game.IsCustom ? CustomRule() : game.Chapter.description;
                }

                layout.LevelName.text = level.name.ToUpperInvariant();
                if (layout.LevelId != null)
                {
                    layout.LevelId.text = game.IsCustom ? "" : $"Level {level.id}";
                }

                layout.Moves.text = board.MoveCount.ToString();
                layout.Par.text = level.par > 0 ? level.par.ToString() : "?";
                RefreshOutlook(layout, board.MoveCount, level.par);
                RefreshLog(layout);

                for (int row = 0; row < BoardSize; row++)
                {
                    if (layout.RowFrom[row] == null)
                    {
                        continue;
                    }

                    var info = rowInfo[row];
                    if (layout.RowBits[row] != null)
                    {
                        layout.RowBits[row].text = info.Bits;
                    }

                    layout.RowFrom[row].text = info.From;
                    layout.RowArrow[row].text = info.Arrow;
                    layout.RowTo[row].text = info.To;
                    layout.RowTag[row].text = info.Tag;
                    layout.RowTag[row].color = info.Ok ? Lcd.Light : info.Hint ? Lcd.LossText : Lcd.Text3;
                    layout.RowTag[row].fontStyle = info.Hint && !info.Ok ? FontStyles.Bold : FontStyles.Normal;
                    layout.RowTag[row].fontSize = info.Ok ? (layout.Phone ? 12 : 11) : 13;
                    layout.RowTag[row].alignment = info.Ok || !layout.Phone ? TextAlignmentOptions.Center
                        : TextAlignmentOptions.MidlineRight;
                    layout.RowTagBox[row].enabled = info.Ok;
                }

                for (int row = 0; row < BoardSize; row++)
                {
                    for (int column = 0; column < BoardSize; column++)
                    {
                        layout.Thumb[row * BoardSize + column].color = BoardBits.GetCell(rules.Target, row, column)
                            ? Lcd.Ink
                            : Lcd.Background;
                    }
                }
            }

            shownReadRow = -1;
            RefreshReadout();
        }

        /// <summary>The phone layouts' readout follows the cursor row (the row last tapped or swiped).</summary>
        void RefreshReadout()
        {
            if (input == null || game.Board == null)
            {
                return;
            }

            int row = input.CursorRow;
            if (row == shownReadRow)
            {
                return;
            }

            shownReadRow = row;
            var info = rowInfo[row];
            foreach (var layout in layouts)
            {
                if (layout.ReadRow == null)
                {
                    continue;
                }

                layout.ReadRow.text = $"ROW {row + 1}";
                layout.ReadOk.text = $"{rowsOk}/{BoardSize} OK";
                layout.ReadBits.text = info.Bits;
                layout.ReadFrom.text = info.From;
                layout.ReadArrow.text = info.Arrow;
                layout.ReadTo.text = info.To;
                layout.ReadTag.text = info.Tag;
                layout.ReadTag.color = info.Ok ? Lcd.Ink : info.Hint ? Lcd.LossText : Lcd.Text3;
            }
        }

        /// <summary>Newest move first, numbered; the bottom rows fade so a long log trails off.</summary>
        void RefreshLog(Layout layout)
        {
            var log = game.MoveLog;
            layout.LogEmpty.enabled = log.Count == 0;
            int rows = layout.LogIndex.Length;
            for (int i = 0; i < rows; i++)
            {
                int moveIndex = log.Count - 1 - i;
                bool shown = moveIndex >= 0;
                layout.LogIndex[i].text = shown ? (moveIndex + 1).ToString("00") : "";
                layout.LogMove[i].text = shown ? log[moveIndex].ToString() : "";
                Color move = i == 0 ? Lcd.Ink : Lcd.Text3;
                float alpha = rows == LogRows ? LogAlpha[i] : ShortLogAlpha[Mathf.Min(i, ShortLogAlpha.Length - 1)];
                move.a = alpha;
                layout.LogMove[i].color = move;
                Color index = Lcd.Text3;
                index.a = alpha;
                layout.LogIndex[i].color = index;
                layout.LogMove[i].fontStyle = i == 0 ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        void RefreshOutcome(bool animate)
        {
            var board = game.Board;
            var rules = board.Rules;
            animate &= !game.Settings.reducedMotion;
            string par = game.Level.par > 0 ? game.Level.par.ToString() : "?";

            bool broken = board.IsRuleBroken;
            if (animate && board.Lost > shownLost)
            {
                StartCoroutine(Bump(Active.BucketCount.rectTransform));
            }

            shownLost = board.Lost;
            foreach (var layout in layouts)
            {
                layout.BucketCount.text = board.Lost.ToString();
                layout.BucketCount.color = board.Lost > 0 ? Lcd.Loss : Lcd.Ink;
                if (layout.BucketFrame != null)
                {
                    layout.BucketFrame.color = broken ? Lcd.Loss : Lcd.Ink;
                }

                if (layout.BucketText != null)
                {
                    layout.BucketText.text = broken ? (layout.Phone ? "rule broken" : "Rule broken. Undo to continue.")
                        : layout.Phone ? ShortRuleText(rules) : RuleText(rules);
                    layout.BucketText.color = broken ? Lcd.Loss : Lcd.Text2;
                }
            }

            bool solved = board.IsSolved;
            if (solved)
            {
                var result = game.LastResult ?? new Progress.LevelResult(board.MoveCount, game.Level.par,
                    Scoring.Stars(board.MoveCount, game.Level.par), false, 0);
                string best = result.IsNewBest && result.PreviousBest > 0 ? " · new best" : "";
                if (game.JustProved && game.IsCustom)
                {
                    best = " · proven";
                }

                solvedText.text = $"{Ui.Moves(result.Moves)} · par {par}{best}";
                for (int i = 0; i < stars.Length; i++)
                {
                    stars[i].enabled = i < result.Stars;
                }

                bool hasNext = game.HasNextLevel;
                nextButton.gameObject.SetActive(hasNext && !game.IsRandom);
                nextButton.Text = game.IsCustom ? "BACK" : "NEXT →";
                lastLevelText.gameObject.SetActive(!hasNext);
                nextRandomButton.gameObject.SetActive(game.IsRandom);
                saveRandomButton.gameObject.SetActive(game.IsRandom);
                if (game.IsRandom)
                {
                    bool full = !game.RandomSaved && game.CustomLevels.IsFull;
                    saveRandomButton.Text = game.RandomSaved ? "SAVED" : full ? "FULL" : DeviceMode.IsMobile ? "SAVE"
                        : "SAVE (K)";
                    saveRandomButton.Disabled = game.RandomSaved || full;
                }
            }

            ShowBanner(solvedBanner, solved, ref solvedShown, animate);
            ShowBanner(brokenBanner, broken, ref brokenShown, animate);
        }

        void ShowBanner(RectTransform banner, bool show, ref bool shown, bool animate)
        {
            banner.gameObject.SetActive(show);
            if (show && !shown && animate)
            {
                StartCoroutine(PopIn(banner));
            }
            else
            {
                banner.localScale = Vector3.one;
            }

            shown = show;
        }

        /// <summary>The mockup's banner entrance: 0.6 → 1.08 → 1.</summary>
        static IEnumerator PopIn(RectTransform rectTransform)
        {
            const float duration = 0.25f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float progress = elapsed / duration;
                float scale = progress < 0.6f ? Mathf.Lerp(0.6f, 1.08f, progress / 0.6f)
                    : Mathf.Lerp(1.08f, 1f, (progress - 0.6f) / 0.4f);
                rectTransform.localScale = Vector3.one * scale;
                yield return null;
            }

            rectTransform.localScale = Vector3.one;
        }

        static IEnumerator Bump(RectTransform rectTransform)
        {
            const float duration = 0.2f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                rectTransform.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(Mathf.PI * elapsed / duration));
                yield return null;
            }

            rectTransform.localScale = Vector3.one;
        }

        /// <summary>The stars a solve could still earn: finishing at par or later, never sooner.</summary>
        /// <summary>The line under "MY LEVELS": what par means on this level.</summary>
        string CustomRule()
        {
            if (game.IsRandom)
            {
                return game.CustomEntry.proof == LevelProof.Searched
                    ? "A random level. Par is the shortest possible."
                    : "A random level. Par is a known solve, and may not be the shortest.";
            }

            switch (game.CustomEntry.proof)
            {
                case LevelProof.Searched:
                    return "Your level. Par is the shortest possible.";
                case LevelProof.Replayed:
                    return "Your level. Par is the best solve so far.";
                default:
                    return "Your level, not proven yet. Beat it to prove it.";
            }
        }

        void RefreshOutlook(Layout layout, int moves, int par)
        {
            bool show = par > 0;
            layout.Outlook.enabled = show;
            int earned = game.LastResult?.Stars ?? Scoring.Stars(Mathf.Max(moves, par), par);
            for (int i = 0; i < layout.OutlookStars.Length; i++)
            {
                layout.OutlookStars[i].enabled = show && i < earned;
            }

            if (!show)
            {
                return;
            }

            layout.Outlook.text = game.LastResult != null ? $"solved for {earned}"
                : moves == 0 ? $"{Ui.Moves(par)} for {Scoring.MaxStars}"
                : earned == Scoring.MaxStars && !layout.Phone ? $"still on track for {earned}"
                : $"on track for {earned}";
        }

        /// <summary>Binary row with walls shown as '#'.</summary>
        static string RowString(LevelRules rules, int value, int row)
        {
            var builder = new StringBuilder(BoardSize);
            for (int column = 0; column < BoardSize; column++)
            {
                builder.Append(rules.IsWall(row, column) ? '#' : ((value >> (7 - column)) & 1) != 0 ? '1' : '0');
            }

            return builder.ToString();
        }

        /// <summary>The bucket rule in a few words for the phone's small bucket card.</summary>
        static string ShortRuleText(LevelRules rules) => rules.LossRule switch
        {
            LossRule.AtMost when rules.LossCount == 0 => "lose none",
            LossRule.AtMost => $"lose at most {rules.LossCount}",
            LossRule.Exactly => $"lose exactly {rules.LossCount}",
            _ => "bits lost"
        };

        static string RuleText(LevelRules rules) => rules.LossRule switch
        {
            LossRule.AtMost when rules.LossCount == 0 => "RULE · LOSE 0 BITS\nAny lost bit breaks the level.",
            LossRule.AtMost => $"RULE · LOSE AT MOST {rules.LossCount}\nBits pushed off the edge land here for good.",
            LossRule.Exactly => $"GOAL · LOSE EXACTLY {rules.LossCount}\nDump exactly that many bits on the way.",
            _ => "Bits pushed off the edge land here and can't come back."
        };
    }
}
