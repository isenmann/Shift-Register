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
            public readonly TextMeshProUGUI[] LogIndex = new TextMeshProUGUI[LogRows];
            public readonly TextMeshProUGUI[] LogMove = new TextMeshProUGUI[LogRows];
            public HudButton Sound;
            /// <summary>Landscape only: the key caps (desktop) or the swipe hint (mobile).</summary>
            public GameObject Keys, SwipeHint;
            /// <summary>Outer groups that move out into the extra length: landscape left/right rails, portrait top/bottom blocks.</summary>
            public RectTransform A, B;
        }

        Layout landscape, portrait;
        Layout[] layouts;
        Layout Active => ScreenLayout.Portrait ? portrait : landscape;

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

        void Update() => UpdateHelp();

        /// <summary>Shows the arrangement for the current orientation and moves the shared parts with it.</summary>
        void ApplyOrientation()
        {
            bool tall = ScreenLayout.Portrait;
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null) scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx);
            landscape.Root.gameObject.SetActive(!tall);
            portrait.Root.gameObject.SetActive(tall);
            bool mobile = DeviceMode.IsMobile;
            landscape.Keys.SetActive(!mobile);
            landscape.SwipeHint.SetActive(mobile);

            // The extra length of 20:9 phones and wide windows goes between the board and the outer groups.
            float shift = ScreenLayout.OuterShiftPx;
            landscape.A.anchoredPosition = new Vector2(-shift, 0f);
            landscape.B.anchoredPosition = new Vector2(shift, 0f);
            portrait.A.anchoredPosition = new Vector2(0f, shift);
            portrait.B.anchoredPosition = new Vector2(0f, -shift);

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
            portrait = new Layout { Root = Frame(canvas, "Portrait", 720, 1280) };
            BuildPortrait(portrait);
            layouts = new[] { landscape, portrait };

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
            HudButton.Create(right, "MENU", rx, menuY, 56, 40, display, 12, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(right, "HELP", rx + 60, menuY, 56, 40, display, 12, ToggleHelp);
            l.Sound = HudButton.Create(right, "SOUND", rx + 120, menuY, 72, 40, display, 12, ToggleMute);
        }

        /// <summary>
        /// P2, 720×1280: header, then the target / bucket / move log cards above the board so a
        /// swiping thumb never covers them, the board with its row tags, and thumb-sized buttons.
        /// </summary>
        void BuildPortrait(Layout l)
        {
            var f = l.Root;
            var top = l.A = Group(f, "Top", 720, 1280);
            var bottom = l.B = Group(f, "Bottom", 720, 1280);
            const float x = 24f;
            l.ChapterLabel = Ui.Text(top, "Chapter", x, 36, 440, 18, mono, 13, Lcd.Text3);
            l.ChapterLabel.characterSpacing = 10;
            l.LevelName = LevelName(top, x, 58, 440, 44, 40);
            l.LevelId = Ui.Text(top, "Level Id", x, 104, 440, 18, mono, 14, Lcd.Text2);
            l.Rule = Ui.Text(top, "Chapter Rule", x, 136, 440, 36, mono, 13, Lcd.Text2, TextAlignmentOptions.TopLeft);
            l.Rule.textWrappingMode = TextWrappingModes.Normal;

            Ui.Box(top, "Moves Tile", 476, 36, 104, 72, 10f, Lcd.Cell);
            Ui.Text(top, "Moves Label", 490, 46, 80, 16, mono, 12, Lcd.Text3, text: "MOVES").characterSpacing = 10;
            l.Moves = Ui.Text(top, "Moves", 490, 64, 80, 36, display, 30, Lcd.Ink);
            Ui.Frame(top, "Par Tile", 592, 36, 104, 72, 10f, 2f, Lcd.Ink);
            Ui.Text(top, "Par Label", 606, 46, 80, 16, mono, 12, Lcd.Text3, text: "PAR").characterSpacing = 10;
            l.Par = Ui.Text(top, "Par", 606, 64, 80, 36, display, 30, Lcd.Ink);
            Outlook(l, top, 476, 130, 18f);

            TargetCard(l, top, x, 176, 216, 229, 19f, 22f);
            Bucket(l, top, 256, 176, 208, 229);
            MoveLog(l, top, 480, 176, 216);

            RowTags(l, f, 560f, ScreenLayout.PortraitBoardCenterPx.y, ScreenLayout.PortraitBoardScale);

            Ui.Text(bottom, "Swipe Hint", x, 990, 672, 20, mono, 13, Lcd.Text3, TextAlignmentOptions.Center,
                "Swipe a row or column to shift it · tap a cell to select");

            HudButton.CreateSolid(bottom, "UNDO", x, 1056, 330, 76, display, 20, () => game.Undo());
            HudButton.Create(bottom, "RESET", 366, 1056, 330, 76, display, 20, () => game.ResetLevel());
            HudButton.Create(bottom, "MENU", x, 1156, 216, 56, display, 14, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(bottom, "HELP", 252, 1156, 216, 56, display, 14, ToggleHelp);
            l.Sound = HudButton.Create(bottom, "SOUND", 480, 1156, 216, 56, display, 14, ToggleMute);
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

        void TargetCard(Layout l, Transform f, float x, float y, float w, float h, float cell, float pitch)
        {
            const float pad = 16f;
            Ui.Box(f, "Target Card", x, y, w, h, 10f, Lcd.Cell);
            Ui.Text(f, "Target Title", x + pad, y + 14, 160, 18, display, 14, Lcd.Ink, text: "TARGET");
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                l.Thumb[r * N + c] = Ui.Box(f, $"Thumb {r + 1},{c + 1}", x + pad + c * pitch, y + 42f + r * pitch, cell, cell, 3f, Lcd.Background);
        }

        void Outlook(Layout l, Transform f, float x, float y, float star)
        {
            for (int i = 0; i < Scoring.MaxStars; i++)
            {
                float sx = x + i * (star + 6f);
                Ui.Shape(f, $"Outlook Star {i + 1} Outline", sx, y, star, star, ProceduralSprites.Star(star, 1.6f, ui: true), Lcd.Ink);
                l.OutlookStars[i] = Ui.Shape(f, $"Outlook Star {i + 1}", sx, y, star, star, ProceduralSprites.Star(star, ui: true), Lcd.Ink);
            }
            l.Outlook = Ui.Text(f, "Outlook", x + Scoring.MaxStars * (star + 6f) + 4f, y, 160, star, mono, 12, Lcd.Text2);
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
        void MoveLog(Layout l, Transform f, float x, float y, float w)
        {
            const float row = 22f;
            Ui.Box(f, "Log Rule", x, y, w, 2, 0f, Lcd.Cell);
            Ui.Text(f, "Log Title", x, y + 12, w, 16, mono, 12, Lcd.Text3, text: "MOVES SO FAR").characterSpacing = 10;
            for (int i = 0; i < LogRows; i++)
            {
                float ry = y + 36 + i * row;
                l.LogIndex[i] = Ui.Text(f, $"Log {i + 1} Index", x, ry, 40, row, mono, 13, Lcd.Text3);
                l.LogMove[i] = Ui.Text(f, $"Log {i + 1} Move", x + 40, ry, w - 40, row, mono, 13, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            }
            l.LogEmpty = Ui.Text(f, "Log Empty", x, y + 36, w, row, mono, 13, Lcd.Cell, text: "no moves yet");
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

        void RefreshBoard()
        {
            var board = game.Board;
            var level = game.Level;
            var rules = board.Rules;
            bool arithmetic = RowHint.ArithmeticHintsApply(rules);

            foreach (var l in layouts)
            {
                l.ChapterLabel.text = $"CHAPTER {game.ChapterIndex + 1} · {game.Chapter.name.ToUpperInvariant()}";
                l.Rule.text = game.Chapter.description;
                l.LevelName.text = level.name.ToUpperInvariant();
                l.LevelId.text = $"Level {level.id}";
                l.Moves.text = board.MoveCount.ToString();
                l.Par.text = level.par > 0 ? level.par.ToString() : "?";
                RefreshOutlook(l, board.MoveCount, level.par);
                RefreshLog(l);

                for (int r = 0; r < N; r++)
                {
                    int now = board.GetRow(r);
                    int target = BoardBits.GetRow(rules.Target, r);
                    l.RowBits[r].text = RowString(rules, now, r);
                    string label = RowHint.Label(rules, r);

                    int chainTop = rules.ChainTopOf(r);
                    bool chainBottom = chainTop >= 0 && chainTop != r;
                    if (chainTop >= 0)
                    {
                        // A carry chain reads as one 16-bit number: "now" on its top row, "→ target" below.
                        now = (board.GetRow(chainTop) << 8) | board.GetRow(chainTop + 1);
                        target = (BoardBits.GetRow(rules.Target, chainTop) << 8) | BoardBits.GetRow(rules.Target, chainTop + 1);
                    }
                    l.RowFrom[r].text = chainBottom ? "" : $"<b>{now}</b>";
                    l.RowArrow[r].text = chainTop >= 0 ? "" : "→";
                    l.RowTo[r].text = chainTop == r ? "" : chainBottom ? $"→ {target}" : target.ToString();

                    string hint = chainBottom ? "" : RowHint.For(now, target, arithmetic).Replace("need ", "");
                    bool ok = hint == RowHint.Ok;
                    bool showHint = hint.Length > 0;
                    l.RowTag[r].text = showHint ? hint : label;
                    l.RowTag[r].color = ok ? Lcd.Light : showHint ? Lcd.LossText : Lcd.Text3;
                    l.RowTag[r].fontStyle = showHint && !ok ? FontStyles.Bold : FontStyles.Normal;
                    l.RowTag[r].fontSize = ok ? 11 : 13;
                    l.RowTagBox[r].enabled = ok;
                }

                for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                    l.Thumb[r * N + c].color = BoardBits.GetCell(rules.Target, r, c) ? Lcd.Ink : Lcd.Background;
            }
        }

        /// <summary>Newest move first, numbered; the bottom rows fade so a long log trails off.</summary>
        void RefreshLog(Layout l)
        {
            var log = game.MoveLog;
            l.LogEmpty.enabled = log.Count == 0;
            for (int i = 0; i < LogRows; i++)
            {
                int m = log.Count - 1 - i;
                bool shown = m >= 0;
                l.LogIndex[i].text = shown ? (m + 1).ToString("00") : "";
                l.LogMove[i].text = shown ? log[m].ToString() : "";
                Color move = i == 0 ? Lcd.Ink : Lcd.Text3;
                move.a = LogAlpha[i];
                l.LogMove[i].color = move;
                Color index = Lcd.Text3;
                index.a = LogAlpha[i];
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
                l.BucketFrame.color = broken ? Lcd.Loss : Lcd.Ink;
                l.BucketText.text = broken ? "Rule broken. Undo to continue." : RuleText(rules);
                l.BucketText.color = broken ? Lcd.Loss : Lcd.Text2;
            }

            bool solved = board.IsSolved;
            if (solved)
            {
                var result = game.LastResult ?? new Progress.LevelResult(board.MoveCount, game.Level.par,
                    Scoring.Stars(board.MoveCount, game.Level.par), false, 0);
                string best = result.IsNewBest && result.PreviousBest > 0 ? " · new best" : "";
                solvedText.text = $"{result.Moves} moves · par {par}{best}";
                for (int i = 0; i < stars.Length; i++) stars[i].enabled = i < result.Stars;
                bool hasNext = game.HasNextLevel;
                nextButton.gameObject.SetActive(hasNext);
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
        void RefreshOutlook(Layout l, int moves, int par)
        {
            bool show = par > 0;
            l.Outlook.enabled = show;
            int earned = game.LastResult?.Stars ?? Scoring.Stars(Mathf.Max(moves, par), par);
            for (int i = 0; i < l.OutlookStars.Length; i++) l.OutlookStars[i].enabled = show && i < earned;
            if (!show) return;
            l.Outlook.text = game.LastResult != null ? $"solved for {earned}"
                : moves == 0 ? $"{par} moves for {Scoring.MaxStars}"
                : earned == Scoring.MaxStars ? $"still on track for {earned}"
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

        static string RuleText(LevelRules rules) => rules.LossRule switch
        {
            LossRule.AtMost when rules.LossCount == 0 => "RULE · LOSE 0 BITS\nAny lost bit breaks the level.",
            LossRule.AtMost => $"RULE · LOSE AT MOST {rules.LossCount}\nBits pushed off the edge land here for good.",
            LossRule.Exactly => $"GOAL · LOSE EXACTLY {rules.LossCount}\nDump exactly that many bits on the way.",
            _ => "Bits pushed off the edge land here and can't come back."
        };
    }
}
