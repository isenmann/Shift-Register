using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// Title screen, chapter select and options, built from code on their own canvas, each in a
    /// landscape and a portrait (mobile) arrangement; the one matching the screen is shown.
    /// Title: Enter plays, C opens chapters, O options. Chapters: arrows pick a level, Enter plays it;
    /// in portrait the chapters are a scrolling list of cards.
    /// </summary>
    public sealed partial class MenuView : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] ScreenFlow flow;
        [Tooltip("Silkscreen Bold: big titles.")]
        [SerializeField] TMP_FontAsset displayBold;
        [Tooltip("Silkscreen Regular: buttons and chapter names.")]
        [SerializeField] TMP_FontAsset display;
        [Tooltip("JetBrains Mono Regular.")]
        [SerializeField] TMP_FontAsset mono;

        const int N = BoardBits.Size;
        // The title screen's smiley, as row numbers.
        static readonly int[] TitleBits = { 60, 66, 165, 129, 165, 153, 66, 60 };

        sealed class LevelRow
        {
            public int Chapter, Level;
            /// <summary>Top-left of the row in its parent, where the focus frame goes.</summary>
            public Vector2 Position, Size;
            public Image[] Thumb;
            public TextMeshProUGUI Id, Name, Locked;
            public Image[] Stars;
            public Image[] StarOutlines;
            public CanvasGroup Group;
        }

        /// <summary>One arrangement of all three menu screens.</summary>
        sealed class Screens
        {
            public RectTransform Title, Chapters, Options;
            public TextMeshProUGUI TitleProgress, ChaptersStats, ChaptersKeys, OptionsKeys, SoundText;
            /// <summary>Keyboard hints on the title screen (hidden on phones and tablets).</summary>
            public GameObject TitleKeys;
            public Image Focus;
            public readonly List<List<LevelRow>> Rows = new List<List<LevelRow>>();
            /// <summary>Portrait only: the scrolling chapter list and each chapter card's top.</summary>
            public ScrollRect Scroll;
            public readonly List<float> CardTops = new List<float>();
            public OptionsUi Opt;
            /// <summary>Groups that move out into the extra length, with their direction (x right, y up).</summary>
            public readonly List<(RectTransform Group, Vector2 Direction)> Outer = new List<(RectTransform, Vector2)>();
            /// <summary>Portrait only: the chapter list's viewport, which grows with the extra length.</summary>
            public RectTransform ScrollView;
        }

        Screens wide, tall;
        Screens Cur => ScreenLayout.Portrait ? tall : wide;
        Screens[] Both => new[] { wide, tall };

        int focusChapter = -1, focusLevel;
        bool built;
        GameScreen shown = GameScreen.Title;

        public void Show(GameScreen screen)
        {
            if (!built) Build();
            shown = screen;
            if (screen != GameScreen.Title) FinishIntro();
            foreach (var s in Both)
            {
                bool cur = s == Cur;
                s.Title.gameObject.SetActive(cur && screen == GameScreen.Title);
                s.Chapters.gameObject.SetActive(cur && screen == GameScreen.Chapters);
                s.Options.gameObject.SetActive(cur && screen == GameScreen.Options);
            }
            if (screen == GameScreen.Options) RefreshOptions(true);
            if (screen == GameScreen.Title) RefreshTitle();
            if (screen == GameScreen.Title && !introPlayed) StartIntro();
            if (screen == GameScreen.Chapters) RefreshChapters();
        }

        void Awake() => Ui.RenderWithMainCamera(GetComponent<Canvas>(), 20);

        void OnEnable() => ScreenLayout.LayoutChanged += ApplyOrientation;
        void OnDisable() => ScreenLayout.LayoutChanged -= ApplyOrientation;

        /// <summary>Switches arrangement and shows the current screen in it.</summary>
        void ApplyOrientation()
        {
            if (!built) Build();
            ApplyFrame();
            Show(shown);
        }

        /// <summary>The canvas reference size for the orientation, and hints worded for touch or keys.</summary>
        void ApplyFrame()
        {
            var scaler = GetComponent<CanvasScaler>();
            // Menus are still drawn in desktop units (720 short side), on phones too.
            float units = ScreenLayout.MenuScale;
            if (scaler != null) scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx) * units;

            // The extra length of 20:9 phones and wide windows goes between the centre and the outer groups.
            // The menus' own extra length beyond their 1280 base, in desktop units.
            float longSide = (ScreenLayout.Portrait ? ScreenLayout.FrameHeightPx : ScreenLayout.FrameWidthPx) * units;
            float extra = longSide - 1280f, shift = extra / 2f;
            foreach (var s in Both)
                foreach (var (group, direction) in s.Outer)
                    group.anchoredPosition = direction * shift;
            tall.ScrollView.sizeDelta = new Vector2(tall.ScrollView.sizeDelta.x, 1080f + extra);

            bool mobile = DeviceMode.IsMobile;
            foreach (var s in Both)
            {
                if (s.TitleKeys != null) s.TitleKeys.SetActive(!mobile);
                s.ChaptersKeys.text = mobile
                    ? "Tap a level to play it · you can skip one unsolved level at a time"
                    : "Arrows choose · Enter play · Esc back · you can skip one unsolved level at a time";
                s.OptionsKeys.gameObject.SetActive(!mobile);
                s.SoundText.text = mobile ? "Turn all sounds off or on." : "M mutes and unmutes anywhere.";
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || flow == null || !built) return;

            if (flow.Current == GameScreen.Title && introRunning)
            {
                // A key or click during the intro only finishes it.
                if (SkipPressed(kb)) FinishIntro();
                return;
            }
            if (flow.Current == GameScreen.Title)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) flow.Resume();
                else if (kb.cKey.wasPressedThisFrame) flow.Show(GameScreen.Chapters);
                else if (kb.oKey.wasPressedThisFrame) flow.Show(GameScreen.Options);
                return;
            }
            if (flow.Current == GameScreen.Options)
            {
                UpdateOptions(kb);
                return;
            }
            if (flow.Current != GameScreen.Chapters) return;

            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) StepChapter(-1);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) StepChapter(1);
            else if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) SetFocus(focusChapter, focusLevel - 1);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) SetFocus(focusChapter, focusLevel + 1);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                TryPlay(focusChapter, focusLevel);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            if (built) return;
            built = true;
            var canvas = (RectTransform)transform;
            wide = new Screens
            {
                Title = Frame(canvas, "Title Screen", 1280, 720),
                Chapters = Frame(canvas, "Chapter Select", 1280, 720),
                Options = Frame(canvas, "Options", 1280, 720)
            };
            tall = new Screens
            {
                Title = Frame(canvas, "Title Screen (portrait)", 720, 1280),
                Chapters = Frame(canvas, "Chapter Select (portrait)", 720, 1280),
                Options = Frame(canvas, "Options (portrait)", 720, 1280)
            };
            BuildReveal(canvas);
            BuildTitle(wide);
            BuildTitlePortrait(tall);
            BuildChapters(wide);
            BuildChaptersPortrait(tall);
            BuildOptions(wide, portrait: false);
            BuildOptions(tall, portrait: true);
            game.SettingsChanged += OnSettingsChanged;
            ApplyFrame();
        }

        /// <summary>A screen-sized child for one outer group; it moves by half the extra length in <paramref name="dx"/>, <paramref name="dy"/>.</summary>
        static RectTransform Outer(Screens s, RectTransform screen, string name, float dx, float dy)
        {
            var group = Ui.Rect(screen, name, 0, 0, screen.sizeDelta.x, screen.sizeDelta.y);
            s.Outer.Add((group, new Vector2(dx, dy)));
            return group;
        }

        static RectTransform Frame(RectTransform canvas, string name, float w, float h)
        {
            var frame = Ui.Rect(canvas, name, 0, 0, w, h);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            return frame;
        }

        void BuildTitle(Screens s)
        {
            var root = s.Title;
            var a = Outer(s, root, "Left", -1f, 0f);
            var b = Outer(s, root, "Right", 1f, 0f);
            const float x = 80f;
            var tagline = Ui.Text(a, "Tagline", x, 120, 560, 20, mono, 14, Lcd.Text3, text: "A PUZZLE ABOUT MOVING BITS · GAME OFF 2026");
            tagline.characterSpacing = 12;
            Ui.Text(a, "Shift", x - 4, 160, 620, 100, displayBold, 100, Lcd.Ink, text: "SHIFT");
            Ui.Text(a, "Register", x - 4, 255, 620, 100, displayBold, 100, Lcd.Ink, text: "REGISTER");
            var blurb = Ui.Text(a, "Blurb", x, 378, 560, 52, mono, 17, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "Slide whole rows of bits to draw the picture. Whatever falls off the edge is gone for good.");
            blurb.textWrappingMode = TextWrappingModes.Normal;

            HudButton.CreateSolid(a, "PLAY", x, 458, 130, 52, display, 18, () => flow.Resume());
            HudButton.Create(a, "CHAPTERS", x + 142, 458, 170, 52, display, 16, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(a, "OPTIONS", x + 324, 458, 160, 52, display, 16, () => flow.Show(GameScreen.Options));
            s.TitleProgress = Ui.Text(a, "Progress", x, 526, 560, 20, mono, 14, Lcd.Text2);

            TitleGrid(b, 740f, 120f);

            var keys = Ui.Rect(a, "Keys", 0, 0, 1280, 720);
            s.TitleKeys = keys.gameObject;
            Ui.Text(keys, "Keys Hint", x, 550, 560, 20, mono, 13, Lcd.Text3, text: "Enter play · C chapters · O options");
            const float ky = 642f;
            KeyCap(keys, "⇧ Shift", x, ky, 82);
            Ui.Text(keys, "Plus", x + 82, ky, 28, 34, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, "+");
            KeyCap(keys, "← → ↑ ↓", x + 110, ky, 112);
            Ui.Text(keys, "Hint", x + 236, ky, 400, 34, mono, 15, Lcd.Text2, text: "shifts the selected row or column");
        }

        /// <summary>Portrait title: the name on top, the smiley in the middle, thumb-sized buttons below.</summary>
        void BuildTitlePortrait(Screens s)
        {
            var root = s.Title;
            var top = Outer(s, root, "Top", 0f, 1f);
            var bottom = Outer(s, root, "Bottom", 0f, -1f);
            const float x = 48f, w = 624f;
            var tagline = Ui.Text(top, "Tagline", x, 72, w, 20, mono, 13, Lcd.Text3, text: "A PUZZLE ABOUT MOVING BITS · GAME OFF 2026");
            tagline.characterSpacing = 8;
            Ui.Text(top, "Shift", x - 4, 104, w, 90, displayBold, 84, Lcd.Ink, text: "SHIFT");
            Ui.Text(top, "Register", x - 4, 190, w, 90, displayBold, 84, Lcd.Ink, text: "REGISTER");

            TitleGrid(root, (720f - 494f) / 2f, 300f);

            var blurb = Ui.Text(bottom, "Blurb", x, 826, w, 60, mono, 17, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "Slide whole rows of bits to draw the picture. Whatever falls off the edge is gone for good.");
            blurb.textWrappingMode = TextWrappingModes.Normal;

            HudButton.CreateSolid(bottom, "PLAY", x, 912, w, 76, display, 22, () => flow.Resume());
            HudButton.Create(bottom, "CHAPTERS", x, 1004, 306, 64, display, 17, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(bottom, "OPTIONS", x + 318, 1004, 306, 64, display, 17, () => flow.Show(GameScreen.Options));
            s.TitleProgress = Ui.Text(bottom, "Progress", x, 1088, w, 20, mono, 14, Lcd.Text2, TextAlignmentOptions.Center);
        }

        /// <summary>The smiley bit grid (8 × 52 px cells, 6 px gaps, 18 px padding: 494 px square); its bits join the intro.</summary>
        void TitleGrid(Transform root, float gx, float gy)
        {
            const float cell = 52f, gap = 6f, pad = 18f;
            Ui.Box(root, "Grid", gx, gy, 2 * pad + N * cell + (N - 1) * gap, 2 * pad + N * cell + (N - 1) * gap, 10f, Lcd.Cell);
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                bool on = ((TitleBits[r] >> (7 - c)) & 1) != 0;
                float cx = gx + pad + c * (cell + gap), cy = gy + pad + r * (cell + gap);
                var box = Ui.Box(root, $"Cell {r + 1},{c + 1}", cx, cy, cell, cell, 8f, on ? Lcd.Ink : Lcd.Background);
                if (!on) continue;
                titleBits.Add(new TitleBit
                {
                    Column = c,
                    Body = box,
                    EyeL = Ui.Box(root, "Eye L", cx + 13, cy + 16, 7, 9, 2f, Lcd.Light),
                    EyeR = Ui.Box(root, "Eye R", cx + 32, cy + 16, 7, 9, 2f, Lcd.Light)
                });
            }
        }

        void KeyCap(Transform root, string text, float x, float y, float w)
        {
            Ui.Frame(root, text, x, y, w, 34, 6f, 2f, Lcd.Ink);
            Ui.Text(root, text + " Label", x, y, w, 34, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, text);
        }

        void BuildChapters(Screens s)
        {
            var root = s.Chapters;
            var left = Outer(s, root, "Left", -1f, 0f);
            var right = Outer(s, root, "Right", 1f, 0f);
            const float rowW = 163f, rowH = 62f;
            Ui.Text(left, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "CHAPTERS");
            s.ChaptersStats = Ui.Text(right, "Stats", 500, 36, 600, 48, mono, 15, Lcd.Text2, TextAlignmentOptions.MidlineRight);
            HudButton.Create(right, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));

            var chapters = game.Catalog.chapters;
            const float top = 100f, height = 580f, gap = 14f;
            float w = (1168f - (chapters.Count - 1) * gap) / Mathf.Max(1, chapters.Count);
            var thumbCell = ProceduralSprites.RoundedRect(5, 5, 1f, ui: true);

            for (int ci = 0; ci < chapters.Count; ci++)
            {
                var ch = chapters[ci];
                float cx = 56f + ci * (w + gap);
                Ui.Box(root, $"Chapter {ci + 1}", cx, top, w, height, 10f, Lcd.Card);
                Ui.Frame(root, $"Chapter {ci + 1} Frame", cx, top, w, height, 10f, 2f, Lcd.Ink);
                Ui.Text(root, "Label", cx + 16, top + 16, w - 32, 16, mono, 12, Lcd.Text3, text: $"CHAPTER {ci + 1}");
                var name = Ui.Text(root, "Name", cx + 16, top + 34, w - 32, 24, display, 17, Lcd.Ink, text: ch.name.ToUpperInvariant());
                name.enableAutoSizing = true;
                name.fontSizeMin = 11;
                name.fontSizeMax = 17;
                var text = Ui.Text(root, "Text", cx + 16, top + 62, w - 32, 70, mono, 12.5f, Lcd.Text2, TextAlignmentOptions.TopLeft, ch.description);
                text.textWrappingMode = TextWrappingModes.Normal;

                var list = new List<LevelRow>();
                s.Rows.Add(list);
                if (ch.levels.Count == 0)
                {
                    Ui.Text(root, "Soon", cx + 16, top + 140, w - 32, 20, mono, 13, Lcd.Text3, text: "Coming soon");
                    continue;
                }

                for (int li = 0; li < ch.levels.Count; li++)
                {
                    float rx = cx + (w - rowW) / 2f, ry = top + 136f + li * (rowH + 8f);
                    var rowRoot = Ui.Rect(root, $"Level {ci + 1}-{li + 1}", rx, ry, rowW, rowH);
                    var row = NewRow(ci, li, rowRoot, rx, ry, rowW, rowH);
                    Ui.Box(rowRoot, "Thumb", 4, 4, 54, 54, 4f, Lcd.Background);
                    for (int r = 0; r < N; r++)
                    for (int c = 0; c < N; c++)
                        row.Thumb[r * N + c] = Ui.Shape(rowRoot, "Px", 8 + c * 6, 8 + r * 6, 5, 5, thumbCell, Lcd.Cell);
                    row.Id = Ui.Text(rowRoot, "Id", 68, 6, 95, 16, mono, 12, Lcd.Text3);
                    row.Name = Ui.Text(rowRoot, "Name", 68, 22, 95, 18, mono, 13, Lcd.Ink);
                    row.Locked = Ui.Text(rowRoot, "Locked", 68, 42, 95, 14, mono, 11, Lcd.Text3, text: "locked");
                    RowStars(row, rowRoot, 68, 42, 13, 16);
                    FinishRow(row, rowRoot);
                    list.Add(row);
                }
            }

            s.Focus = Ui.Frame(root, "Focus", 0, 0, rowW, rowH, 8f, 3f, Lcd.Ink);
            s.Focus.enabled = false;
            s.ChaptersKeys = Ui.Text(left, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3);
        }

        /// <summary>P3: a scrolling list of chapter cards, each with its levels as tiles.</summary>
        void BuildChaptersPortrait(Screens s)
        {
            var root = s.Chapters;
            var top = Outer(s, root, "Top", 0f, 1f);
            var bottom = Outer(s, root, "Bottom", 0f, -1f);
            Ui.Text(top, "Heading", 24, 32, 400, 52, displayBold, 34, Lcd.Ink, text: "CHAPTERS");
            HudButton.Create(top, "BACK", 566, 32, 130, 52, display, 15, () => flow.Show(GameScreen.Title));
            s.ChaptersStats = Ui.Text(top, "Stats", 24, 96, 672, 20, mono, 14, Lcd.Text2);

            // Scroll view: a masked viewport, the content grows with the cards.
            var viewport = Ui.Rect(top, "Scroll", 24, 136, 672, 1080);
            s.ScrollView = viewport;
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            var content = Ui.Rect(viewport, "Content", 0, 0, 672, 100);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            s.Scroll = scroll;

            var chapters = game.Catalog.chapters;
            var thumbCell = ProceduralSprites.RoundedRect(8, 8, 2f, ui: true);
            const float cardW = 672f, cardH = 278f, cardGap = 16f, tileW = 122f, tileH = 150f, tileGap = 7f;
            float y = 0f;
            for (int ci = 0; ci < chapters.Count; ci++)
            {
                var ch = chapters[ci];
                s.CardTops.Add(y);
                Ui.Box(content, $"Chapter {ci + 1}", 0, y, cardW, cardH, 12f, Lcd.Card);
                Ui.Frame(content, $"Chapter {ci + 1} Frame", 0, y, cardW, cardH, 12f, 2f, Lcd.Ink);
                Ui.Text(content, "Label", 16, y + 16, 400, 16, mono, 12, Lcd.Text3, text: $"CHAPTER {ci + 1}").characterSpacing = 8;
                Ui.Text(content, "Name", 16, y + 34, 640, 26, display, 20, Lcd.Ink, text: ch.name.ToUpperInvariant());
                var text = Ui.Text(content, "Text", 16, y + 64, 640, 40, mono, 13, Lcd.Text2, TextAlignmentOptions.TopLeft, ch.description);
                text.textWrappingMode = TextWrappingModes.Normal;

                var list = new List<LevelRow>();
                s.Rows.Add(list);
                if (ch.levels.Count == 0)
                {
                    Ui.Text(content, "Soon", 16, y + 120, 400, 20, mono, 13, Lcd.Text3, text: "Coming soon");
                }
                for (int li = 0; li < ch.levels.Count; li++)
                {
                    float tx = 16f + li * (tileW + tileGap), ty = y + 112f;
                    var tile = Ui.Rect(content, $"Level {ci + 1}-{li + 1}", tx, ty, tileW, tileH);
                    var row = NewRow(ci, li, tile, tx, ty, tileW, tileH);
                    Ui.Box(tile, "Thumb", 18, 6, 86, 86, 5f, Lcd.Background);
                    for (int r = 0; r < N; r++)
                    for (int c = 0; c < N; c++)
                        row.Thumb[r * N + c] = Ui.Shape(tile, "Px", 22 + c * 10, 10 + r * 10, 8, 8, thumbCell, Lcd.Cell);
                    row.Id = Ui.Text(tile, "Id", 8, 98, 106, 14, mono, 11, Lcd.Text3);
                    row.Name = Ui.Text(tile, "Name", 8, 114, 106, 16, mono, 13, Lcd.Ink);
                    row.Locked = Ui.Text(tile, "Locked", 8, 132, 106, 14, mono, 11, Lcd.Text3, text: "locked");
                    RowStars(row, tile, 8, 132, 13, 16);
                    FinishRow(row, tile);
                    list.Add(row);
                }
                y += cardH + cardGap;
            }
            content.sizeDelta = new Vector2(cardW, y);

            s.Focus = Ui.Frame(content, "Focus", 0, 0, tileW, tileH, 8f, 3f, Lcd.Ink);
            s.Focus.enabled = false;
            s.ChaptersKeys = Ui.Text(bottom, "Keys", 24, 1232, 672, 20, mono, 13, Lcd.Text3, TextAlignmentOptions.Center);
        }

        LevelRow NewRow(int chapter, int level, RectTransform root, float x, float y, float w, float h) => new LevelRow
        {
            Chapter = chapter,
            Level = level,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            Group = root.gameObject.AddComponent<CanvasGroup>(),
            Thumb = new Image[N * N],
            Stars = new Image[Scoring.MaxStars],
            StarOutlines = new Image[Scoring.MaxStars]
        };

        static void RowStars(LevelRow row, Transform root, float x, float y, float size, float pitch)
        {
            for (int s = 0; s < Scoring.MaxStars; s++)
            {
                row.StarOutlines[s] = Ui.Shape(root, "Star Outline", x + s * pitch, y, size, size, ProceduralSprites.Star(size, 1.2f, ui: true), Lcd.Ink);
                row.Stars[s] = Ui.Shape(root, "Star", x + s * pitch, y, size, size, ProceduralSprites.Star(size, ui: true), Lcd.Ink);
            }
        }

        /// <summary>Level data, picture and the hotspot (hover focuses, click or tap plays).</summary>
        void FinishRow(LevelRow row, RectTransform root)
        {
            var entry = game.Entry(row.Chapter, row.Level);
            if (entry != null)
            {
                row.Id.text = entry.Data.id;
                row.Name.text = $"<b>{entry.Data.name}</b>";
                for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                    if (((entry.Data.target[r] >> (7 - c)) & 1) != 0) row.Thumb[r * N + c].color = Lcd.Ink;
            }
            int chapterIndex = row.Chapter, levelIndex = row.Level;
            var hot = UiHotspot.Create(root, "Hotspot", 0, 0, row.Size.x, row.Size.y);
            hot.Enter = () => SetFocus(chapterIndex, levelIndex);
            hot.Click = () => TryPlay(chapterIndex, levelIndex);
        }

        // ---------------------------------------------------------------- refresh

        void RefreshTitle()
        {
            int solved = 0;
            foreach (var e in game.Levels) if (game.Progress.IsSolved(e.Data.id)) solved++;
            string text = solved == 0
                ? $"{game.Levels.Count} levels to solve"
                : $"{solved} of {game.Levels.Count} levels solved · {game.Progress.TotalStars()} stars";
            foreach (var s in Both) s.TitleProgress.text = text;
        }

        void RefreshChapters()
        {
            int solved = 0;
            foreach (var e in game.Levels) if (game.Progress.IsSolved(e.Data.id)) solved++;
            foreach (var s in Both)
            {
                foreach (var list in s.Rows)
                foreach (var row in list)
                {
                    var entry = game.Entry(row.Chapter, row.Level);
                    if (entry == null) continue;
                    bool open = game.IsUnlocked(row.Chapter, row.Level);
                    int stars = game.Progress.StarsFor(entry.Data.id);
                    row.Group.alpha = open ? 1f : 0.4f;
                    row.Locked.enabled = !open;
                    for (int i = 0; i < row.Stars.Length; i++)
                    {
                        row.Stars[i].enabled = open && i < stars;
                        row.StarOutlines[i].enabled = open;
                    }
                }
                s.ChaptersStats.text = $"{solved} of {game.Levels.Count} levels · {game.Progress.TotalStars()} stars";
                s.Focus.enabled = false; // no focus tick when the screen opens
            }

            // Focus the current or resume level, and scroll the portrait list to its chapter.
            var (c, l) = game.Board != null ? (game.ChapterIndex, game.LevelIndex) : game.ResumeTarget();
            SetFocus(c, l);
            if (focusChapter >= 0 && focusChapter < tall.CardTops.Count)
            {
                var content = tall.Scroll.content;
                float max = Mathf.Max(0f, content.sizeDelta.y - tall.Scroll.viewport.rect.height);
                content.anchoredPosition = new Vector2(0f, Mathf.Clamp(tall.CardTops[focusChapter], 0f, max));
            }
        }

        // ---------------------------------------------------------------- focus

        void StepChapter(int delta)
        {
            var rows = wide.Rows;
            for (int c = focusChapter + delta; c >= 0 && c < rows.Count; c += delta)
            {
                if (rows[c].Count == 0) continue;
                SetFocus(c, Mathf.Min(focusLevel, rows[c].Count - 1));
                return;
            }
        }

        void SetFocus(int chapter, int level)
        {
            var rows = wide.Rows;
            if (chapter < 0 || chapter >= rows.Count || rows[chapter].Count == 0)
            {
                // Fall back to the first chapter that has levels.
                chapter = rows.FindIndex(r => r.Count > 0);
                if (chapter < 0) return;
                level = 0;
            }
            level = Mathf.Clamp(level, 0, rows[chapter].Count - 1);
            if (Cur.Focus.enabled && (chapter != focusChapter || level != focusLevel)) Sfx.Play(Sound.Tick);
            focusChapter = chapter;
            focusLevel = level;
            foreach (var s in Both)
            {
                var row = s.Rows[chapter][level];
                s.Focus.rectTransform.anchoredPosition = new Vector2(row.Position.x + row.Size.x / 2f, -(row.Position.y + row.Size.y / 2f));
                s.Focus.enabled = true;
            }
        }

        void TryPlay(int chapter, int level)
        {
            if (chapter < 0 || !game.IsUnlocked(chapter, level))
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            flow.Play(chapter, level);
        }
    }
}
