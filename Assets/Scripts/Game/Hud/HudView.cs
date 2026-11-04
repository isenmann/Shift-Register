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
        [SerializeField] GameController game;
        [SerializeField] BoardInput input;
        [SerializeField] ScreenFlow flow;
        [Tooltip("Silkscreen Bold: level title and banner.")]
        [SerializeField] TMP_FontAsset displayBold;
        [Tooltip("Silkscreen Regular: card titles and buttons.")]
        [SerializeField] TMP_FontAsset display;
        [Tooltip("JetBrains Mono Regular (its weight table maps <b> to the Bold asset).")]
        [SerializeField] TMP_FontAsset mono;

        const int N = BoardBits.Size;
        const float RowBox = 84f;
        // Move log rows, newest first; the last ones fade out instead of counting what is hidden.
        const int LogRows = 7;
        static readonly float[] LogAlpha = { 1f, 1f, 1f, 1f, 0.55f, 0.3f, 0.12f };
        static readonly float[] ShortLogAlpha = { 1f, 0.6f, 0.3f };

        /// <summary>Everything one arrangement shows that changes during play.</summary>
        sealed class Layout
        {
            public RectTransform Root;
            public TextMeshProUGUI ChapterLabel, Rule, LevelName, LevelId, Moves, Par, Outlook;
            public readonly Image[] OutlookStars = new Image[Scoring.MaxStars];
            public readonly Image[] Thumb = new Image[N * N];
            public readonly TextMeshProUGUI[] RowBits = new TextMeshProUGUI[N];
            public readonly TextMeshProUGUI[] RowFrom = new TextMeshProUGUI[N];
            public readonly TextMeshProUGUI[] RowArrow = new TextMeshProUGUI[N];
            public readonly TextMeshProUGUI[] RowTo = new TextMeshProUGUI[N];
            public readonly TextMeshProUGUI[] RowTag = new TextMeshProUGUI[N];
            public readonly Image[] RowTagBox = new Image[N];
            public Image BucketFrame;
            public TextMeshProUGUI BucketCount, BucketText, LogEmpty;
            public TextMeshProUGUI[] LogIndex, LogMove;
            public HudButton Sound;
            /// <summary>Phone layouts: compact texts, and the readout of the selected row.</summary>
            public bool Phone;
            public TextMeshProUGUI ReadRow, ReadOk, ReadBits, ReadFrom, ReadArrow, ReadTo, ReadTag;
            /// <summary>Landscape only: the key caps (desktop) or the swipe hint (mobile).</summary>
            public GameObject Keys, SwipeHint;
            /// <summary>Outer groups that move out into the extra length: landscape left/right rails, portrait top/bottom blocks.</summary>
            public RectTransform A, B;
        }

        Layout landscape, phoneLandscape, phonePortrait;
        Layout[] layouts;
        Layout Active => !ScreenLayout.Phone ? landscape : ScreenLayout.Portrait ? phonePortrait : phoneLandscape;
        int shownReadRow = -1;

        RectTransform bannerFrame, solvedBanner, brokenBanner;
        TextMeshProUGUI solvedText, lastLevelText;
        readonly Image[] stars = new Image[Scoring.MaxStars];
        HudButton nextButton;
        int shownLost;
        bool solvedShown, brokenShown;

        void Awake()
        {
            Ui.RenderWithMainCamera(GetComponent<Canvas>(), 10);
            Build();
        }

        void OnEnable()
        {
            if (game == null) return;
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
            game.Changed += OnChanged;
            game.SettingsChanged += RefreshSound;
            ScreenLayout.LayoutChanged += ApplyOrientation;
            ApplyOrientation();
            RefreshSound();
            if (game.BoardView != null) game.BoardView.Settled += OnSettled;
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
            if (game == null) return;
            CloseHelp();
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
            game.Changed -= OnChanged;
            game.SettingsChanged -= RefreshSound;
            ScreenLayout.LayoutChanged -= ApplyOrientation;
            if (game.BoardView != null) game.BoardView.Settled -= OnSettled;
        }

        void Start()
        {
            if (game != null && game.Board != null) OnChanged();
        }

        /// <summary>Board info updates at once; the bucket and banner wait for the move animation to land.</summary>
        void OnChanged()
        {
            CheckFirstHelp();
            RefreshBoard();
            bool animating = game.BoardView != null && game.BoardView.IsAnimating;
            if (!animating) RefreshOutcome(false);
        }

        void RefreshSound()
        {
            foreach (var l in layouts) l.Sound.Text = game.Settings.muted ? "MUTED" : "SOUND";
        }

        void OnSettled()
        {
            if (game.Board != null) RefreshOutcome(true);
        }

        void Update()
        {
            UpdateHelp();
            if (ScreenLayout.Phone) RefreshReadout();
        }

        /// <summary>Shows the arrangement for the current orientation and moves the shared parts with it.</summary>
        void ApplyOrientation()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null) scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx);
            var active = Active;
            foreach (var l in layouts) l.Root.gameObject.SetActive(l == active);
            bool mobile = DeviceMode.IsMobile;
            landscape.Keys.SetActive(!mobile);
            landscape.SwipeHint.SetActive(mobile);

            // The extra length of long phones and wide windows goes between the board and the outer groups.
            float shift = ScreenLayout.OuterShiftPx;
            foreach (var l in new[] { landscape, phoneLandscape })
            {
                l.A.anchoredPosition = new Vector2(-shift, 0f);
                l.B.anchoredPosition = new Vector2(shift, 0f);
            }
            phonePortrait.A.anchoredPosition = new Vector2(0f, shift);
            phonePortrait.B.anchoredPosition = new Vector2(0f, -shift);

            bannerFrame.sizeDelta = new Vector2(ScreenLayout.BaseWidthPx, ScreenLayout.BaseHeightPx);
            Vector2 centre = ScreenLayout.BoardCenterPx;
            foreach (var banner in new[] { solvedBanner, brokenBanner })
                banner.anchoredPosition = new Vector2(centre.x, -centre.y);
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
        static RectTransform Group(Transform frame, string name, float w, float h) => Ui.Rect(frame, name, 0, 0, w, h);

        static RectTransform Frame(RectTransform canvas, string name, float w, float h)
        {
            var frame = Ui.Rect(canvas, name, 0, 0, w, h);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            return frame;
        }

        /// <summary>Layout A3b, 1280×720: left rail, row tags, right rail aligned to the board (cells y 124–596).</summary>
        void BuildLandscape(Layout l)
        {
            var f = l.Root;
            var left = l.A = Group(f, "Left Rail", 1280, 720);
            var right = l.B = Group(f, "Right Rail", 1280, 720);
            const float x = 48f, w = 232f;
            l.ChapterLabel = Ui.Text(left, "Chapter", x, 56, w, 18, mono, 13, Lcd.Text3);
            l.ChapterLabel.characterSpacing = 10;
            l.Rule = Ui.Text(left, "Chapter Rule", x, 78, w, 54, mono, 12, Lcd.Text2, TextAlignmentOptions.TopLeft);
            l.Rule.textWrappingMode = TextWrappingModes.Normal;
            l.Rule.lineSpacing = 4;
            l.LevelName = LevelName(left, x, 134, w, 46, 44);
            l.LevelId = Ui.Text(left, "Level Id", x, 182, w, 18, mono, 14, Lcd.Text2);

            TargetCard(l, left, x, 212, w, 237, 20f, 23f);

            const float tileY = 465f, tileW = 110f, tileH = 50f, parX = x + tileW + 12f;
            Ui.Box(left, "Moves Tile", x, tileY, tileW, tileH, 10f, Lcd.Cell);
            Ui.Text(left, "Moves Label", x + 14, tileY, 60, tileH, mono, 12, Lcd.Text3, text: "MOVES").characterSpacing = 10;
            l.Moves = Ui.Text(left, "Moves", x + 14, tileY, tileW - 28, tileH, display, 30, Lcd.Ink, TextAlignmentOptions.MidlineRight);
            Ui.Frame(left, "Par Tile", parX, tileY, tileW, tileH, 10f, 2f, Lcd.Ink);
            Ui.Text(left, "Par Label", parX + 14, tileY, 60, tileH, mono, 12, Lcd.Text3, text: "PAR").characterSpacing = 10;
            l.Par = Ui.Text(left, "Par", parX + 14, tileY, tileW - 28, tileH, display, 30, Lcd.Ink, TextAlignmentOptions.MidlineRight);

            Outlook(l, left, x, 531, 20f);

            // Bottom edge level with the board's down-arrow row (mockup y 644).
            HudButton.CreateSolid(left, "UNDO", x, 592, tileW, 52, display, 15, () => game.Undo());
            HudButton.Create(left, "RESET", parX, 592, tileW, 52, display, 15, () => game.ResetLevel());

            RowTags(l, f, 892f, ScreenLayout.LandscapeBoardCenterPx.y, ScreenLayout.LandscapeBoardScale);

            const float rx = 1048f, rw = 192f;
            Bucket(l, right, rx, 124, rw, 150);
            MoveLog(l, right, rx, 294, rw);

            // Key caps, like the title screen's (desktop); a swipe hint on phones and tablets.
            var keys = Ui.Rect(right, "Keys", 0, 0, 1280, 720);
            l.Keys = keys.gameObject;
            const float k1 = 504f, k2 = 536f, k3 = 568f;
            float kx = KeyCap(keys, "⇧", rx, k1, 26f);
            kx = KeyLabel(keys, "+", kx, k1, 18f, TextAlignmentOptions.Center);
            kx = KeyCap(keys, "← → ↑ ↓", kx, k1, 76f);
            KeyLabel(keys, "shift", kx + 6f, k1, 50f);
            kx = KeyCap(keys, "Z", rx, k2, 26f);
            kx = KeyLabel(keys, "undo", kx + 6f, k2, 40f);
            kx = KeyCap(keys, "R", kx + 8f, k2, 26f);
            KeyLabel(keys, "reset", kx + 6f, k2, 50f);
            kx = KeyCap(keys, "H", rx, k3, 26f);
            kx = KeyLabel(keys, "help", kx + 6f, k3, 40f);
            kx = KeyCap(keys, "M", kx + 8f, k3, 26f);
            KeyLabel(keys, "mute", kx + 6f, k3, 50f);
            var swipe = Ui.Text(right, "Swipe Hint", rx, 504, rw, 90, mono, 13, Lcd.Text3, TextAlignmentOptions.TopLeft,
                "Swipe a row or column to shift it. Tap a cell to select it.");
            swipe.textWrappingMode = TextWrappingModes.Normal;
            swipe.lineSpacing = 6;
            l.SwipeHint = swipe.gameObject;

            const float menuY = 604f;
            HudButton.Create(right, "MENU", rx, menuY, 56, 40, display, 12, () => flow.LeaveLevel());
            HudButton.Create(right, "HELP", rx + 60, menuY, 56, 40, display, 12, ToggleHelp);
            l.Sound = HudButton.Create(right, "SOUND", rx + 120, menuY, 72, 40, display, 12, ToggleMute);
        }

        /// <summary>
        /// B2, a 20:9 phone held upright (412×915 CSS px): header, the target / bucket / last moves
        /// cards, the readout of the selected row, the board across the full width (no row tags), and
        /// thumb-sized buttons. Text is at least 13 px, buttons at least 48 px tall.
        /// </summary>
        void BuildPhonePortrait(Layout l)
        {
            var f = l.Root;
            var top = l.A = Group(f, "Top", 412, 915);
            var bottom = l.B = Group(f, "Bottom", 412, 915);
            l.ChapterLabel = Ui.Text(top, "Chapter", 12, 20, 236, 16, mono, 13, Lcd.Text3);
            l.LevelName = LevelName(top, 12, 38, 236, 32, 30);
            PhoneTile(top, "Moves", 250, 20, 70, 56, true, out l.Moves);
            PhoneTile(top, "Par", 330, 20, 70, 56, false, out l.Par);
            Outlook(l, top, 12, 92, 18f, 14f);

            TargetCard(l, top, 12, 124, 150, 144, 12f, 14f, 12f, 13f, 26f, 6f);
            PhoneBucket(l, top, 172, 124, 108, 144);
            MoveLog(l, top, 290, 124, 110, 3, 15f, "MOVES", 30f);

            Readout(l, top, 12, 280, 388, 64, 18f, 17f, 24f);

            Ui.Text(bottom, "Swipe Hint", 12, 756, 388, 18, mono, 14, Lcd.Text3, TextAlignmentOptions.Center,
                "Swipe to shift · tap a row to read it");
            HudButton.CreateSolid(bottom, "UNDO", 12, 782, 189, 56, display, 20, () => game.Undo());
            HudButton.Create(bottom, "RESET", 211, 782, 189, 56, display, 20, () => game.ResetLevel());
            HudButton.Create(bottom, "MENU", 12, 848, 122, 48, display, 14, () => flow.LeaveLevel());
            HudButton.Create(bottom, "HELP", 145, 848, 122, 48, display, 14, ToggleHelp);
            l.Sound = HudButton.Create(bottom, "SOUND", 278, 848, 122, 48, display, 14, ToggleMute);
        }

        /// <summary>
        /// B3, a 20:9 phone held sideways (915×412 CSS px): slim left rail (level, moves/par, outlook,
        /// row readout, undo/reset), the board across the full height with compact row tags, and a slim
        /// right rail (target, bucket, last moves, menu/help/sound).
        /// </summary>
        void BuildPhoneLandscape(Layout l)
        {
            var f = l.Root;
            var left = l.A = Group(f, "Left Rail", 915, 412);
            var right = l.B = Group(f, "Right Rail", 915, 412);
            l.ChapterLabel = Ui.Text(left, "Chapter", 16, 20, 206, 16, mono, 13, Lcd.Text3);
            l.LevelName = LevelName(left, 16, 38, 206, 30, 28);
            PhoneTile(left, "Moves", 16, 84, 99, 54, true, out l.Moves);
            PhoneTile(left, "Par", 123, 84, 99, 54, false, out l.Par);
            Outlook(l, left, 16, 150, 16f, 14f);
            Readout(l, left, 16, 180, 206, 84, 15f, 15f, 20f);
            HudButton.CreateSolid(left, "UNDO", 16, 336, 99, 56, display, 18, () => game.Undo());
            HudButton.Create(left, "RESET", 123, 336, 99, 56, display, 18, () => game.ResetLevel());

            CompactRowTags(l, f, 618f, ScreenLayout.PhoneLandscapeBoardCenterPx.y, ScreenLayout.PhoneLandscapeBoardScale);

            // Target and bucket cards side by side, as in portrait, narrower.
            TargetCard(l, right, 716, 20, 93, 112, 9f, 10f, 7f, 13f, 26f, 6f);
            PhoneBucket(l, right, 817, 20, 82, 112, withText: false);
            MoveLog(l, right, 716, 146, 183, 3, 15f, "MOVES", 30f);
            HudButton.Create(right, "MENU", 716, 346, 56, 46, display, 12, () => flow.LeaveLevel());
            HudButton.Create(right, "HELP", 778, 346, 56, 46, display, 12, ToggleHelp);
            l.Sound = HudButton.Create(right, "SOUND", 840, 346, 59, 46, display, 12, ToggleMute);
        }

        /// <summary>A MOVES (filled) or PAR (outlined) tile with its label over a large value.</summary>
        void PhoneTile(Transform f, string label, float x, float y, float w, float h, bool filled, out TextMeshProUGUI value)
        {
            if (filled) Ui.Box(f, label + " Tile", x, y, w, h, 8f, Lcd.Cell);
            else Ui.Frame(f, label + " Tile", x, y, w, h, 8f, 2f, Lcd.Ink);
            Ui.Text(f, label + " Label", x + 10, y + 6, w - 20, 14, mono, 12, Lcd.Text3, text: label.ToUpperInvariant());
            value = Ui.Text(f, label, x + 10, y + 22, w - 20, h - 26, display, 24, Lcd.Ink);
        }

        /// <summary>Bucket card for phones: title, a big count and (in portrait) one short line about the rule.</summary>
        void PhoneBucket(Layout l, Transform f, float x, float y, float w, float h, bool withText = true)
        {
            l.BucketFrame = Ui.Frame(f, "Bucket Frame", x, y, w, h, 10f, 3f, Lcd.Ink, 8f);
            Ui.Text(f, "Bucket Title", x + 10, y + 10, w - 20, 16, display, 13, Lcd.Ink, text: "BUCKET");
            l.BucketCount = Ui.Text(f, "Bucket Count", x + 10, y + 30, w - 20, 50, display, 40, Lcd.Ink);
            if (!withText) return;
            l.BucketText = Ui.Text(f, "Bucket Text", x + 10, y + 84, w - 20, h - 92, mono, 13, Lcd.Text2, TextAlignmentOptions.TopLeft);
            l.BucketText.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>
        /// The selected row, large: "ROW n" and how many rows match, the bits spread across the
        /// readout, "now → target" pinned to the same edges, and the tag (OK, ×2, a label).
        /// </summary>
        void Readout(Layout l, Transform f, float x, float y, float w, float h, float bitSize, float numberSize, float tagSize)
        {
            Ui.Frame(f, "Readout Frame", x, y, w, h, 10f, 2f, Lcd.Ink);
            bool wide = w > 300f;
            float inner = wide ? x + 98 : x + 12, innerW = wide ? w - 170 : w - 76;
            if (wide)
            {
                l.ReadRow = Ui.Text(f, "Readout Row", x + 14, y + 12, 80, 18, mono, 13, Lcd.Text3);
                l.ReadOk = Ui.Text(f, "Readout OK", x + 14, y + 34, 80, 18, mono, 13, Lcd.Text3);
            }
            else
            {
                l.ReadRow = Ui.Text(f, "Readout Row", x + 12, y + 8, 90, 16, mono, 13, Lcd.Text3);
                l.ReadOk = Ui.Text(f, "Readout OK", x + w - 102, y + 8, 90, 16, mono, 13, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            }
            float bitsY = wide ? y + 8 : y + 28, numbersY = bitsY + bitSize + 6;
            l.ReadBits = Ui.Text(f, "Readout Bits", inner, bitsY, innerW + 10, bitSize + 4, mono, bitSize, Lcd.Text3);
            l.ReadBits.characterSpacing = (innerW - 8f * 0.6f * bitSize) / 7f / bitSize * 100f;
            l.ReadFrom = Ui.Text(f, "Readout Now", inner, numbersY, innerW, numberSize + 4, mono, numberSize, Lcd.Ink);
            l.ReadArrow = Ui.Text(f, "Readout Arrow", inner, numbersY, innerW, numberSize + 4, mono, numberSize, Lcd.Text3, TextAlignmentOptions.Center, "→");
            l.ReadTo = Ui.Text(f, "Readout Target", inner, numbersY, innerW, numberSize + 4, mono, numberSize, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            float tagW = wide ? 56f : 52f;
            l.ReadTag = Ui.Text(f, "Readout Tag", x + w - tagW - 10, wide ? y + (h - 32) / 2 : y + 36, tagW, 32, mono, tagSize, Lcd.LossText, TextAlignmentOptions.MidlineRight);
            l.ReadTag.fontStyle = FontStyles.Bold;
        }

        /// <summary>Phone landscape row tags: "now → target" and the tag under it, no bits.</summary>
        void CompactRowTags(Layout l, Transform f, float x, float boardCenterY, float scale)
        {
            float pitch = ScreenLayout.Pitch * ScreenLayout.PxPerUnit * scale;
            float firstCenter = boardCenterY - 3.5f * pitch;
            const float w = 86f;
            for (int r = 0; r < N; r++)
            {
                float c = firstCenter + r * pitch;
                l.RowFrom[r] = Ui.Text(f, $"Row {r + 1} Now", x, c - 17, w, 16, mono, 13, Lcd.Ink);
                l.RowArrow[r] = Ui.Text(f, $"Row {r + 1} Arrow", x, c - 17, w, 16, mono, 13, Lcd.Text3, TextAlignmentOptions.Center, "→");
                l.RowTo[r] = Ui.Text(f, $"Row {r + 1} Target", x, c - 17, w, 16, mono, 13, Lcd.Text3, TextAlignmentOptions.MidlineRight);
                l.RowTagBox[r] = Ui.Box(f, $"Row {r + 1} Tag Box", x + w - 30, c + 1, 30, 16, 3f, Lcd.Ink);
                l.RowTag[r] = Ui.Text(f, $"Row {r + 1} Tag", x + w - 30, c + 1, 30, 16, mono, 13, Lcd.Light, TextAlignmentOptions.MidlineRight);
            }
        }

        void ToggleMute() => game.ChangeSettings(s => s.muted = !s.muted);

        TextMeshProUGUI LevelName(Transform f, float x, float y, float w, float h, float size)
        {
            var t = Ui.Text(f, "Level Name", x, y, w, h, displayBold, size, Lcd.Ink);
            t.enableAutoSizing = true;
            t.fontSizeMin = 20;
            t.fontSizeMax = size;
            return t;
        }

        void TargetCard(Layout l, Transform f, float x, float y, float w, float h, float cell, float pitch,
            float pad = 16f, float titleSize = 14f, float gridTop = 42f, float titleTop = 12f)
        {
            Ui.Box(f, "Target Card", x, y, w, h, 10f, Lcd.Cell);
            Ui.Text(f, "Target Title", x + pad, y + titleTop, 160, 18, display, titleSize, Lcd.Ink, text: "TARGET");
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                l.Thumb[r * N + c] = Ui.Box(f, $"Thumb {r + 1},{c + 1}", x + pad + c * pitch, y + gridTop + r * pitch, cell, cell, 2f, Lcd.Background);
        }

        void Outlook(Layout l, Transform f, float x, float y, float star, float textSize = 12f)
        {
            for (int i = 0; i < Scoring.MaxStars; i++)
            {
                float sx = x + i * (star + 6f);
                Ui.Shape(f, $"Outlook Star {i + 1} Outline", sx, y, star, star, ProceduralSprites.Star(star, 1.6f, ui: true), Lcd.Ink);
                l.OutlookStars[i] = Ui.Shape(f, $"Outlook Star {i + 1}", sx, y, star, star, ProceduralSprites.Star(star, ui: true), Lcd.Ink);
            }
            l.Outlook = Ui.Text(f, "Outlook", x + Scoring.MaxStars * (star + 6f) + 4f, y - 1, 200, star + 2, mono, textSize, Lcd.Text2);
        }

        /// <summary>
        /// One tag per board row, centred on it: the bits spread over a fixed box, under them
        /// "now → target" with the numbers pinned to the box edges and the arrow in its middle;
        /// then OK, the hint or a label.
        /// </summary>
        void RowTags(Layout l, Transform f, float x, float boardCenterY, float scale)
        {
            float pitch = ScreenLayout.Pitch * ScreenLayout.PxPerUnit * scale;
            float firstCenter = boardCenterY - 3.5f * pitch;
            // JetBrains Mono advances 0.6 em; spread 8 glyphs so the first and last touch the box edges.
            const float bitSize = 13f;
            float spacingPx = (RowBox - 8f * 0.6f * bitSize) / 7f;
            for (int r = 0; r < N; r++)
            {
                float c = firstCenter + r * pitch;
                l.RowBits[r] = Ui.Text(f, $"Row {r + 1} Bits", x, c - 20, RowBox + 8, 18, mono, bitSize, Lcd.Text3);
                l.RowBits[r].characterSpacing = spacingPx / bitSize * 100f;
                l.RowFrom[r] = Ui.Text(f, $"Row {r + 1} Now", x, c, RowBox, 20, mono, 14, Lcd.Ink);
                l.RowArrow[r] = Ui.Text(f, $"Row {r + 1} Arrow", x, c, RowBox, 20, mono, 14, Lcd.Text3, TextAlignmentOptions.Center, "→");
                l.RowTo[r] = Ui.Text(f, $"Row {r + 1} Target", x, c, RowBox, 20, mono, 14, Lcd.Text3, TextAlignmentOptions.MidlineRight);
                float tagX = x + RowBox + 10f;
                l.RowTagBox[r] = Ui.Box(f, $"Row {r + 1} Tag Box", tagX, c - 10, 34, 20, 4f, Lcd.Ink);
                l.RowTag[r] = Ui.Text(f, $"Row {r + 1} Tag", tagX, c - 10, 34, 20, mono, 13, Lcd.Light, TextAlignmentOptions.Center);
            }
        }

        void Bucket(Layout l, Transform f, float x, float y, float w, float h)
        {
            const float pad = 16f;
            l.BucketFrame = Ui.Frame(f, "Bucket Frame", x, y, w, h, 10f, 3f, Lcd.Ink, 8f);
            Ui.Text(f, "Bucket Title", x + pad, y + 18, 120, 18, display, 14, Lcd.Ink, text: "BIT BUCKET");
            l.BucketCount = Ui.Text(f, "Bucket Count", x + pad, y + 10, w - 2 * pad, 34, display, 28, Lcd.Ink, TextAlignmentOptions.MidlineRight);
            l.BucketText = Ui.Text(f, "Bucket Text", x + pad, y + 52, w - 2 * pad, h - 62, mono, 12, Lcd.Text2, TextAlignmentOptions.TopLeft);
            l.BucketText.textWrappingMode = TextWrappingModes.Normal;
            l.BucketText.lineSpacing = 4;
        }

        /// <summary>Move log: a light counterweight; newest move first.</summary>
        void MoveLog(Layout l, Transform f, float x, float y, float w, int rows = LogRows, float size = 13f,
            string title = "MOVES SO FAR", float firstRow = 36f)
        {
            const float row = 22f;
            l.LogIndex = new TextMeshProUGUI[rows];
            l.LogMove = new TextMeshProUGUI[rows];
            Ui.Box(f, "Log Rule", x, y, w, 2, 0f, Lcd.Cell);
            Ui.Text(f, "Log Title", x, y + 10, w, 16, mono, l.Phone ? 13 : 12, Lcd.Text3, text: title).characterSpacing = l.Phone ? 4 : 10;
            for (int i = 0; i < rows; i++)
            {
                float ry = y + firstRow + i * row;
                l.LogIndex[i] = Ui.Text(f, $"Log {i + 1} Index", x, ry, 40, row, mono, size, Lcd.Text3);
                l.LogMove[i] = Ui.Text(f, $"Log {i + 1} Move", x + 40, ry, w - 40, row, mono, size, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            }
            l.LogEmpty = Ui.Text(f, "Log Empty", x, y + firstRow, w, row, mono, size, Lcd.Cell, text: "no moves yet");
        }

        /// <summary>An outlined key cap; returns its right edge.</summary>
        float KeyCap(Transform frame, string text, float x, float y, float w)
        {
            Ui.Frame(frame, $"Key {text}", x, y, w, 26, 5f, 2f, Lcd.Ink);
            Ui.Text(frame, $"Key {text} Label", x, y, w, 26, mono, 12, Lcd.Ink, TextAlignmentOptions.Center, text);
            return x + w;
        }

        float KeyLabel(Transform frame, string text, float x, float y, float w,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            Ui.Text(frame, $"Key Text {text}", x, y, w, 26, mono, 12, Lcd.Text3, align, text);
            return x + w;
        }

        void BuildBanner(Transform frame)
        {
            const float w = 340f;
            solvedBanner = BannerRoot(frame, "Solved Banner", w, 196f);
            Ui.Box(solvedBanner, "Box", 0, 0, w, 196f, 10f, Lcd.Ink);
            Ui.Text(solvedBanner, "Title", 16, 16, w - 32, 44, displayBold, 40, Lcd.Light, TextAlignmentOptions.Center, "SOLVED");
            solvedText = Ui.Text(solvedBanner, "Text", 0, 62, w, 22, mono, 15, Lcd.Light, TextAlignmentOptions.Center);
            const float star = 30f, gap = 10f;
            float starsX = (w - (Scoring.MaxStars * star + (Scoring.MaxStars - 1) * gap)) / 2f;
            for (int i = 0; i < Scoring.MaxStars; i++)
            {
                float sx = starsX + i * (star + gap);
                Ui.Shape(solvedBanner, $"Star {i + 1} Outline", sx, 94, star, star, ProceduralSprites.Star(star, 2f, ui: true), Lcd.Light);
                stars[i] = Ui.Shape(solvedBanner, $"Star {i + 1}", sx, 94, star, star, ProceduralSprites.Star(star, ui: true), Lcd.Light);
            }
            nextButton = HudButton.Create(solvedBanner, "NEXT →", (w - 150f) / 2f, 140, 150, 40, display, 15, Lcd.Light, Lcd.Ink, () => game.NextLevel());
            lastLevelText = Ui.Text(solvedBanner, "Last Level", 0, 140, w, 40, mono, 13, Lcd.Light, TextAlignmentOptions.Center,
                "That's every level so far.");

            brokenBanner = BannerRoot(frame, "Broken Banner", w, 110f);
            Ui.Box(brokenBanner, "Box", 0, 0, w, 110f, 10f, Lcd.Loss);
            var title = Ui.Text(brokenBanner, "Title", 16, 18, w - 32, 44, displayBold, 40, Lcd.Light, TextAlignmentOptions.Center, "RULE BROKEN");
            title.enableAutoSizing = true;
            title.fontSizeMin = 20;
            title.fontSizeMax = 40;
            Ui.Text(brokenBanner, "Text", 0, 68, w, 24, mono, 15, Lcd.Light, TextAlignmentOptions.Center, "Undo to continue");
        }

        /// <summary>A hidden banner scaled about its centre; <see cref="ApplyOrientation"/> puts it over the board.</summary>
        static RectTransform BannerRoot(Transform frame, string name, float w, float h)
        {
            var rt = Ui.Rect(frame, name, 0, 0, w, h);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.gameObject.SetActive(false);
            return rt;
        }

        // ------------------------------------------------------------------ refresh

        /// <summary>What one board row shows: bits, "now → target" (carry chains read as one 16-bit number), and its tag.</summary>
        struct RowInfo
        {
            public string Bits, From, Arrow, To, Tag;
            public bool Ok, Hint;
        }

        readonly RowInfo[] rowInfo = new RowInfo[N];
        int rowsOk;

        void RefreshBoard()
        {
            var board = game.Board;
            var level = game.Level;
            var rules = board.Rules;
            bool arithmetic = RowHint.ArithmeticHintsApply(rules);

            rowsOk = 0;
            for (int r = 0; r < N; r++)
            {
                int now = board.GetRow(r);
                int target = BoardBits.GetRow(rules.Target, r);
                var info = new RowInfo { Bits = RowString(rules, now, r) };
                if (now == target) rowsOk++;
                string label = RowHint.Label(rules, r);

                int chainTop = rules.ChainTopOf(r);
                bool chainBottom = chainTop >= 0 && chainTop != r;
                if (chainTop >= 0)
                {
                    // A carry chain reads as one 16-bit number: "now" on its top row, "→ target" below.
                    now = (board.GetRow(chainTop) << 8) | board.GetRow(chainTop + 1);
                    target = (BoardBits.GetRow(rules.Target, chainTop) << 8) | BoardBits.GetRow(rules.Target, chainTop + 1);
                }
                info.From = chainBottom ? "" : $"<b>{now}</b>";
                info.Arrow = chainTop >= 0 ? "" : "→";
                info.To = chainTop == r ? "" : chainBottom ? $"→ {target}" : target.ToString();

                string hint = chainBottom ? "" : RowHint.For(now, target, arithmetic).Replace("need ", "");
                info.Ok = hint == RowHint.Ok;
                info.Hint = hint.Length > 0;
                info.Tag = info.Hint ? hint : label;
                rowInfo[r] = info;
            }

            foreach (var l in layouts)
            {
                if (game.IsCustom) l.ChapterLabel.text = "MY LEVELS";
                else
                    l.ChapterLabel.text = l.Phone
                        ? $"CH {game.ChapterIndex + 1} · {game.Chapter.name.ToUpperInvariant()} · {level.id}"
                        : $"CHAPTER {game.ChapterIndex + 1} · {game.Chapter.name.ToUpperInvariant()}";
                if (l.Rule != null) l.Rule.text = game.IsCustom ? CustomRule() : game.Chapter.description;
                l.LevelName.text = level.name.ToUpperInvariant();
                if (l.LevelId != null) l.LevelId.text = game.IsCustom ? "" : $"Level {level.id}";
                l.Moves.text = board.MoveCount.ToString();
                l.Par.text = level.par > 0 ? level.par.ToString() : "?";
                RefreshOutlook(l, board.MoveCount, level.par);
                RefreshLog(l);

                for (int r = 0; r < N; r++)
                {
                    if (l.RowFrom[r] == null) continue;
                    var info = rowInfo[r];
                    if (l.RowBits[r] != null) l.RowBits[r].text = info.Bits;
                    l.RowFrom[r].text = info.From;
                    l.RowArrow[r].text = info.Arrow;
                    l.RowTo[r].text = info.To;
                    l.RowTag[r].text = info.Tag;
                    l.RowTag[r].color = info.Ok ? Lcd.Light : info.Hint ? Lcd.LossText : Lcd.Text3;
                    l.RowTag[r].fontStyle = info.Hint && !info.Ok ? FontStyles.Bold : FontStyles.Normal;
                    l.RowTag[r].fontSize = info.Ok ? (l.Phone ? 12 : 11) : 13;
                    l.RowTag[r].alignment = info.Ok || !l.Phone ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineRight;
                    l.RowTagBox[r].enabled = info.Ok;
                }

                for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                    l.Thumb[r * N + c].color = BoardBits.GetCell(rules.Target, r, c) ? Lcd.Ink : Lcd.Background;
            }
            shownReadRow = -1;
            RefreshReadout();
        }

        /// <summary>The phone layouts' readout follows the cursor row (the row last tapped or swiped).</summary>
        void RefreshReadout()
        {
            if (input == null || game.Board == null) return;
            int r = input.CursorRow;
            if (r == shownReadRow) return;
            shownReadRow = r;
            var info = rowInfo[r];
            foreach (var l in layouts)
            {
                if (l.ReadRow == null) continue;
                l.ReadRow.text = $"ROW {r + 1}";
                l.ReadOk.text = $"{rowsOk}/{N} OK";
                l.ReadBits.text = info.Bits;
                l.ReadFrom.text = info.From;
                l.ReadArrow.text = info.Arrow;
                l.ReadTo.text = info.To;
                l.ReadTag.text = info.Tag;
                l.ReadTag.color = info.Ok ? Lcd.Ink : info.Hint ? Lcd.LossText : Lcd.Text3;
            }
        }

        /// <summary>Newest move first, numbered; the bottom rows fade so a long log trails off.</summary>
        void RefreshLog(Layout l)
        {
            var log = game.MoveLog;
            l.LogEmpty.enabled = log.Count == 0;
            int rows = l.LogIndex.Length;
            for (int i = 0; i < rows; i++)
            {
                int m = log.Count - 1 - i;
                bool shown = m >= 0;
                l.LogIndex[i].text = shown ? (m + 1).ToString("00") : "";
                l.LogMove[i].text = shown ? log[m].ToString() : "";
                Color move = i == 0 ? Lcd.Ink : Lcd.Text3;
                float alpha = rows == LogRows ? LogAlpha[i] : ShortLogAlpha[Mathf.Min(i, ShortLogAlpha.Length - 1)];
                move.a = alpha;
                l.LogMove[i].color = move;
                Color index = Lcd.Text3;
                index.a = alpha;
                l.LogIndex[i].color = index;
                l.LogMove[i].fontStyle = i == 0 ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        void RefreshOutcome(bool animate)
        {
            var board = game.Board;
            var rules = board.Rules;
            animate &= !game.Settings.reducedMotion;
            string par = game.Level.par > 0 ? game.Level.par.ToString() : "?";

            bool broken = board.IsRuleBroken;
            if (animate && board.Lost > shownLost) StartCoroutine(Bump(Active.BucketCount.rectTransform));
            shownLost = board.Lost;
            foreach (var l in layouts)
            {
                l.BucketCount.text = board.Lost.ToString();
                l.BucketCount.color = board.Lost > 0 ? Lcd.Loss : Lcd.Ink;
                if (l.BucketFrame != null) l.BucketFrame.color = broken ? Lcd.Loss : Lcd.Ink;
                if (l.BucketText != null)
                {
                    l.BucketText.text = broken ? (l.Phone ? "rule broken" : "Rule broken. Undo to continue.")
                        : l.Phone ? ShortRuleText(rules) : RuleText(rules);
                    l.BucketText.color = broken ? Lcd.Loss : Lcd.Text2;
                }
            }

            bool solved = board.IsSolved;
            if (solved)
            {
                var result = game.LastResult ?? new Progress.LevelResult(board.MoveCount, game.Level.par,
                    Scoring.Stars(board.MoveCount, game.Level.par), false, 0);
                string best = result.IsNewBest && result.PreviousBest > 0 ? " · new best" : "";
                if (game.JustProved && game.IsCustom) best = " · proven";
                solvedText.text = $"{Ui.Moves(result.Moves)} · par {par}{best}";
                for (int i = 0; i < stars.Length; i++) stars[i].enabled = i < result.Stars;
                bool hasNext = game.HasNextLevel;
                nextButton.gameObject.SetActive(hasNext);
                nextButton.Text = game.IsCustom ? "BACK" : "NEXT →";
                lastLevelText.gameObject.SetActive(!hasNext);
            }
            ShowBanner(solvedBanner, solved, ref solvedShown, animate);
            ShowBanner(brokenBanner, broken, ref brokenShown, animate);
        }

        void ShowBanner(RectTransform banner, bool show, ref bool shown, bool animate)
        {
            banner.gameObject.SetActive(show);
            if (show && !shown && animate) StartCoroutine(PopIn(banner));
            else banner.localScale = Vector3.one;
            shown = show;
        }

        /// <summary>The mockup's banner entrance: 0.6 → 1.08 → 1.</summary>
        static IEnumerator PopIn(RectTransform rt)
        {
            const float duration = 0.25f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                float s = k < 0.6f ? Mathf.Lerp(0.6f, 1.08f, k / 0.6f) : Mathf.Lerp(1.08f, 1f, (k - 0.6f) / 0.4f);
                rt.localScale = Vector3.one * s;
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        static IEnumerator Bump(RectTransform rt)
        {
            const float duration = 0.2f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                rt.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(Mathf.PI * t / duration));
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        /// <summary>The stars a solve could still earn: finishing at par or later, never sooner.</summary>
        /// <summary>The line under "MY LEVELS": what par means on this level.</summary>
        string CustomRule()
        {
            switch (game.CustomEntry.proof)
            {
                case LevelProof.Searched: return "Your level. Par is the shortest possible.";
                case LevelProof.Replayed: return "Your level. Par is the best solve so far.";
                default: return "Your level, not proven yet. Beat it to prove it.";
            }
        }

        void RefreshOutlook(Layout l, int moves, int par)
        {
            bool show = par > 0;
            l.Outlook.enabled = show;
            int earned = game.LastResult?.Stars ?? Scoring.Stars(Mathf.Max(moves, par), par);
            for (int i = 0; i < l.OutlookStars.Length; i++) l.OutlookStars[i].enabled = show && i < earned;
            if (!show) return;
            l.Outlook.text = game.LastResult != null ? $"solved for {earned}"
                : moves == 0 ? $"{Ui.Moves(par)} for {Scoring.MaxStars}"
                : earned == Scoring.MaxStars && !l.Phone ? $"still on track for {earned}"
                : $"on track for {earned}";
        }

        /// <summary>Binary row with walls shown as '#'.</summary>
        static string RowString(LevelRules rules, int value, int row)
        {
            var sb = new StringBuilder(N);
            for (int c = 0; c < N; c++)
                sb.Append(rules.IsWall(row, c) ? '#' : ((value >> (7 - c)) & 1) != 0 ? '1' : '0');
            return sb.ToString();
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
