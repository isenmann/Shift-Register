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
    /// Title screen, chapter select and options, built from code on their own canvas in three
    /// arrangements: desktop (1280×720 units) and phone landscape / portrait (915×412 / 412×915, the
    /// phone mockups' CSS px, so text keeps its size); the one matching the screen is shown.
    /// Title: Enter plays, C opens chapters, R random, L my levels, O options, Q quits (desktop builds only). Chapters:
    /// arrows pick a level, Enter plays it;
    /// on an upright phone the chapters are a scrolling list of cards.
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

        const int BoardSize = BoardBits.Size;
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
            public RectTransform MyLevels;
            public MyUi MyLevelsUi;
            public RectTransform Random;
            public RandomUi RandomUi;
            public TextMeshProUGUI TitleProgress, ChaptersStats, SoundText;
            /// <summary>Keyboard hints (absent where there is no room); hidden or reworded on phones.</summary>
            public TextMeshProUGUI ChaptersKeys, OptionsKeys;
            /// <summary>Keyboard hints on the title screen (hidden on phones and tablets).</summary>
            public GameObject TitleKeys;
            public Image Focus;
            public readonly List<List<LevelRow>> Rows = new List<List<LevelRow>>();
            /// <summary>Portrait only: the scrolling chapter list and each chapter card's top.</summary>
            public ScrollRect Scroll;
            public readonly List<float> CardTops = new List<float>();
            public OptionsUi OptionsUi;
            /// <summary>Groups that move out into the extra length, with their direction (x right, y up).</summary>
            public readonly List<(RectTransform Group, Vector2 Direction)> Outer = new List<(RectTransform, Vector2)>();
            /// <summary>Portrait only: the chapter list's viewport, which grows with the extra length.</summary>
            public RectTransform ScrollView;
        }

        Screens wide, phoneWide, phoneTall;
        Screens Current => !ScreenLayout.Phone ? wide : ScreenLayout.Portrait ? phoneTall : phoneWide;
        Screens[] AllScreens => new[] { wide, phoneWide, phoneTall };
        const float PhoneScrollHeight = 770f;

        int focusChapter = -1, focusLevel;
        bool built;
        GameScreen shown = GameScreen.Title;

        public void Show(GameScreen screen)
        {
            if (!built)
            {
                Build();
            }
            // A layout change shows the same screen again, which must not count as arriving on it.
            bool arriving = screen != shown;
            shown = screen;
            if (screen != GameScreen.Title)
            {
                FinishIntro();
            }

            foreach (var screens in AllScreens)
            {
                bool isCurrent = screens == Current;
                screens.Title.gameObject.SetActive(isCurrent && screen == GameScreen.Title);
                screens.Chapters.gameObject.SetActive(isCurrent && screen == GameScreen.Chapters);
                screens.Options.gameObject.SetActive(isCurrent && screen == GameScreen.Options);
                screens.MyLevels.gameObject.SetActive(isCurrent && screen == GameScreen.MyLevels);
                screens.Random.gameObject.SetActive(isCurrent && screen == GameScreen.Random);
            }

            if (screen != GameScreen.Random)
            {
                randomSession.Cancel();
            }

            if (screen == GameScreen.Random)
            {
                EnterRandom(arriving);
            }

            if (screen == GameScreen.MyLevels)
            {
                EnterMyLevels(arriving);
            }

            if (screen == GameScreen.Options)
            {
                RefreshOptions(true);
            }

            if (screen == GameScreen.Title)
            {
                RefreshTitle();
            }

            if (screen == GameScreen.Title && !introPlayed)
            {
                StartIntro();
            }

            if (screen == GameScreen.Chapters)
            {
                RefreshChapters();
            }
        }

        void Awake() => Ui.RenderWithMainCamera(GetComponent<Canvas>(), 20);

        void OnEnable() => ScreenLayout.LayoutChanged += ApplyOrientation;
        void OnDisable() => ScreenLayout.LayoutChanged -= ApplyOrientation;

        /// <summary>Switches arrangement and shows the current screen in it.</summary>
        void ApplyOrientation()
        {
            if (!built)
            {
                Build();
            }

            ApplyFrame();
            Show(shown);
        }

        /// <summary>The canvas reference size for the orientation, and hints worded for touch or keys.</summary>
        void ApplyFrame()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx);
            }

            // The extra length of long phones and wide windows goes between the centre and the outer groups.
            float shift = ScreenLayout.OuterShiftPx;
            foreach (var screens in AllScreens)
            {
                foreach (var (group, direction) in screens.Outer)
                {
                    group.anchoredPosition = direction * shift;
                }
            }

            phoneTall.ScrollView.sizeDelta = new Vector2(phoneTall.ScrollView.sizeDelta.x,
                PhoneScrollHeight + (ScreenLayout.Portrait ? ScreenLayout.ExtraPx : 0f));

            bool mobile = DeviceMode.IsMobile;
            foreach (var screens in AllScreens)
            {
                if (screens.TitleKeys != null)
                {
                    screens.TitleKeys.SetActive(!mobile);
                }

                if (screens.ChaptersKeys != null)
                {
                    screens.ChaptersKeys.text = mobile
                        ? (screens == phoneTall ? "Tap a level to play it"
                        : "Tap a level to play it · you can skip one unsolved level at a time")
                        : "Arrows choose · Enter play · Esc back · you can skip one unsolved level at a time";
                }

                if (screens.OptionsKeys != null)
                {
                    screens.OptionsKeys.gameObject.SetActive(!mobile);
                }

                screens.SoundText.text = mobile ? "Turn all sounds off or on." : "M mutes and unmutes anywhere.";
            }
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (flow == null || !built)
            {
                return;
            }
            // Phones have no keyboard, but their list still scrolls by dragging.
            if (flow.Current == GameScreen.MyLevels)
            {
                UpdateMyLevels(keyboard);
            }

            if (flow.Current == GameScreen.Random)
            {
                UpdateRandom(keyboard);
            }

            if (keyboard == null)
            {
                return;
            }

            if (flow.Current == GameScreen.Title && introRunning)
            {
                // A key or click during the intro only finishes it.
                if (SkipPressed(keyboard))
                {
                    FinishIntro();
                }

                return;
            }

            if (flow.Current == GameScreen.Title)
            {
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame)
                {
                    flow.Resume();
                }
                else if (keyboard.cKey.wasPressedThisFrame)
                {
                    flow.Show(GameScreen.Chapters);
                }
                else if (keyboard.rKey.wasPressedThisFrame)
                {
                    flow.Show(GameScreen.Random);
                }
                else if (keyboard.lKey.wasPressedThisFrame)
                {
                    flow.Show(GameScreen.MyLevels);
                }
                else if (keyboard.oKey.wasPressedThisFrame)
                {
                    flow.Show(GameScreen.Options);
                }
                else if (keyboard.qKey.wasPressedThisFrame)
                {
                    flow.Quit();
                }

                return;
            }

            if (flow.Current == GameScreen.Options)
            {
                UpdateOptions(keyboard);
                return;
            }

            if (flow.Current != GameScreen.Chapters)
            {
                return;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                StepChapter(-1);
            }
            else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                StepChapter(1);
            }
            else if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
            {
                SetFocus(focusChapter, focusLevel - 1);
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                SetFocus(focusChapter, focusLevel + 1);
            }
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame)
            {
                TryPlay(focusChapter, focusLevel);
            }
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            if (built)
            {
                return;
            }

            built = true;
            var canvas = (RectTransform)transform;
            wide = new Screens
            {
                Title = Frame(canvas, "Title Screen", 1280, 720),
                Chapters = Frame(canvas, "Chapter Select", 1280, 720),
                Options = Frame(canvas, "Options", 1280, 720)
            };
            phoneWide = new Screens
            {
                Title = Frame(canvas, "Title Screen (phone landscape)", 915, 412),
                Chapters = Frame(canvas, "Chapter Select (phone landscape)", 915, 412),
                Options = Frame(canvas, "Options (phone landscape)", 915, 412)
            };
            phoneTall = new Screens
            {
                Title = Frame(canvas, "Title Screen (phone portrait)", 412, 915),
                Chapters = Frame(canvas, "Chapter Select (phone portrait)", 412, 915),
                Options = Frame(canvas, "Options (phone portrait)", 412, 915)
            };
            BuildReveal(canvas);
            BuildTitle(wide);
            BuildTitlePhoneLandscape(phoneWide);
            BuildTitlePhonePortrait(phoneTall);
            BuildChapters(wide);
            BuildChaptersPhoneLandscape(phoneWide);
            BuildChaptersPhonePortrait(phoneTall);
            BuildOptions(wide, OptionsMode.Desktop);
            BuildOptions(phoneWide, OptionsMode.PhoneLandscape);
            BuildOptions(phoneTall, OptionsMode.PhonePortrait);
            wide.MyLevels = Frame(canvas, "My Levels", 1280, 720);
            phoneWide.MyLevels = Frame(canvas, "My Levels (phone landscape)", 915, 412);
            phoneTall.MyLevels = Frame(canvas, "My Levels (phone portrait)", 412, 915);
            BuildMyLevels(wide, MyDesktop);
            BuildMyLevels(phoneWide, MyPhoneLandscape);
            BuildMyLevels(phoneTall, MyPhonePortrait);
            wide.Random = Frame(canvas, "Random", 1280, 720);
            phoneWide.Random = Frame(canvas, "Random (phone landscape)", 915, 412);
            phoneTall.Random = Frame(canvas, "Random (phone portrait)", 412, 915);
            BuildRandom(wide, RandomDesktop);
            BuildRandom(phoneWide, RandomPhoneLandscape);
            BuildRandom(phoneTall, RandomPhonePortrait);
            game.SettingsChanged += OnSettingsChanged;
            ApplyFrame();
        }

        /// <summary>
        /// A screen-sized child for one outer group; it moves by half the extra length in
        /// <paramref name="directionX"/>, <paramref name="directionY"/>.
        /// </summary>
        static RectTransform Outer(Screens screens, RectTransform screen, string name, float directionX,
            float directionY)
        {
            var group = Ui.Rect(screen, name, 0, 0, screen.sizeDelta.x, screen.sizeDelta.y);
            screens.Outer.Add((group, new Vector2(directionX, directionY)));
            return group;
        }

        static RectTransform Frame(RectTransform canvas, string name, float width, float height)
        {
            var frame = Ui.Rect(canvas, name, 0, 0, width, height);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            return frame;
        }

        void BuildTitle(Screens screens)
        {
            var root = screens.Title;
            var left = Outer(screens, root, "Left", -1f, 0f);
            var right = Outer(screens, root, "Right", 1f, 0f);
            const float x = 80f;
            var tagline = Ui.Text(left, "Tagline", x, 120, 560, 20, mono, 14, Lcd.Text3,
                text: "A PUZZLE ABOUT MOVING BITS · GAME OFF 2026");
            tagline.characterSpacing = 12;
            Ui.Text(left, "Shift", x - 4, 160, 620, 100, displayBold, 100, Lcd.Ink, text: "SHIFT");
            Ui.Text(left, "Register", x - 4, 255, 620, 100, displayBold, 100, Lcd.Ink, text: "REGISTER");
            var blurb = Ui.Text(left, "Blurb", x, 378, 560, 52, mono, 17, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "Slide whole rows of bits to draw the picture. Whatever falls off the edge is gone for good.");
            blurb.textWrappingMode = TextWrappingModes.Normal;

            HudButton.CreateSolid(left, "PLAY", x, 458, 92, 52, display, 18, () => flow.Resume());
            HudButton.Create(left, "CHAPTERS", x + 98, 458, 122, 52, display, 14, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(left, "RANDOM", x + 226, 458, 102, 52, display, 14, () => flow.Show(GameScreen.Random));
            HudButton.Create(left, "MY LEVELS", x + 334, 458, 128, 52, display, 14,
                () => flow.Show(GameScreen.MyLevels));
            HudButton.Create(left, "OPTIONS", x + 468, 458, 104, 52, display, 14, () => flow.Show(GameScreen.Options));
            // Desktop builds only: a browser tab or a phone app is closed from outside the game.
            bool canQuit = ScreenFlow.CanQuit;
            if (canQuit)
            {
                HudButton.Create(left, "QUIT", x + 578, 458, 64, 52, display, 14, () => flow.Quit());
            }

            screens.TitleProgress = Ui.Text(left, "Progress", x, 526, 560, 20, mono, 14, Lcd.Text2);

            TitleGrid(right, 740f, 120f);
            Credit(right, 1234f - 400f, 650f, 400f, 13, TextAlignmentOptions.MidlineRight);

            var keys = Ui.Rect(left, "Keys", 0, 0, 1280, 720);
            screens.TitleKeys = keys.gameObject;
            Ui.Text(keys, "Keys Hint", x, 550, 560, 20, mono, 13, Lcd.Text3, text: canQuit
                ? "Enter play · C chapters · R random · L my levels · O options · Q quit"
                : "Enter play · C chapters · R random · L my levels · O options");
            const float keysY = 642f;
            KeyCap(keys, "⇧ Shift", x, keysY, 82);
            Ui.Text(keys, "Plus", x + 82, keysY, 28, 34, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, "+");
            KeyCap(keys, "← → ↑ ↓", x + 110, keysY, 112);
            Ui.Text(keys, "Hint", x + 236, keysY, 400, 34, mono, 15, Lcd.Text2,
                text: "shifts the selected row or column");
        }

        /// <summary>Phone portrait title: name on top, the smiley in the middle, thumb-sized buttons below.</summary>
        void BuildTitlePhonePortrait(Screens screens)
        {
            var root = screens.Title;
            var top = Outer(screens, root, "Top", 0f, 1f);
            var bottom = Outer(screens, root, "Bottom", 0f, -1f);
            const float x = 16f, width = 380f;
            var tagline = Ui.Text(top, "Tagline", x, 40, width, 18, mono, 13, Lcd.Text3,
                text: "A PUZZLE ABOUT MOVING BITS · GAME OFF 2026");
            tagline.characterSpacing = 2;
            Ui.Text(top, "Shift", x - 2, 66, width, 56, displayBold, 50, Lcd.Ink, text: "SHIFT");
            Ui.Text(top, "Register", x - 2, 120, width, 56, displayBold, 50, Lcd.Ink, text: "REGISTER");

            TitleGrid(root, (412f - 340f) / 2f, 200f, 36f, 4f, 12f);

            var blurb = Ui.Text(bottom, "Blurb", x, 566, width, 64, mono, 16, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "Slide whole rows of bits to draw the picture. Whatever falls off the edge is gone for good.");
            blurb.textWrappingMode = TextWrappingModes.Normal;
            HudButton.CreateSolid(bottom, "PLAY", x, 650, width, 64, display, 22, () => flow.Resume());
            HudButton.Create(bottom, "CHAPTERS", x, 726, 184, 54, display, 16, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(bottom, "OPTIONS", x + 196, 726, 184, 54, display, 16,
                () => flow.Show(GameScreen.Options));
            HudButton.Create(bottom, "RANDOM", x, 788, 184, 50, display, 16, () => flow.Show(GameScreen.Random));
            HudButton.Create(bottom, "MY LEVELS", x + 196, 788, 184, 50, display, 16,
                () => flow.Show(GameScreen.MyLevels));
            screens.TitleProgress = Ui.Text(bottom, "Progress", x, 848, width, 18, mono, 14, Lcd.Text2,
                TextAlignmentOptions.Center);
            Credit(bottom, x, 875, width, 13, TextAlignmentOptions.Center);
        }

        /// <summary>Phone landscape title: name, blurb and buttons on the left, the smiley on the right.</summary>
        void BuildTitlePhoneLandscape(Screens screens)
        {
            var root = screens.Title;
            var left = Outer(screens, root, "Left", -1f, 0f);
            var right = Outer(screens, root, "Right", 1f, 0f);
            const float x = 32f;
            var tagline = Ui.Text(left, "Tagline", x, 36, 450, 18, mono, 13, Lcd.Text3,
                text: "A PUZZLE ABOUT MOVING BITS · GAME OFF 2026");
            tagline.characterSpacing = 2;
            Ui.Text(left, "Shift", x - 4, 60, 450, 64, displayBold, 58, Lcd.Ink, text: "SHIFT");
            Ui.Text(left, "Register", x - 4, 124, 450, 64, displayBold, 58, Lcd.Ink, text: "REGISTER");
            var blurb = Ui.Text(left, "Blurb", x, 204, 430, 44, mono, 15, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "Slide whole rows of bits to draw the picture. Whatever falls off the edge is gone for good.");
            blurb.textWrappingMode = TextWrappingModes.Normal;
            HudButton.CreateSolid(left, "PLAY", x, 264, 72, 52, display, 16, () => flow.Resume());
            HudButton.Create(left, "CHAPTERS", x + 78, 264, 102, 52, display, 13, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(left, "RANDOM", x + 186, 264, 84, 52, display, 13, () => flow.Show(GameScreen.Random));
            HudButton.Create(left, "MY LEVELS", x + 276, 264, 110, 52, display, 13,
                () => flow.Show(GameScreen.MyLevels));
            HudButton.Create(left, "OPTIONS", x + 392, 264, 90, 52, display, 13, () => flow.Show(GameScreen.Options));
            screens.TitleProgress = Ui.Text(left, "Progress", x, 334, 440, 18, mono, 14, Lcd.Text2);

            TitleGrid(right, 915f - 32f - 356f, 28f, 38f, 4f, 12f);
            Credit(right, 915f - 32f - 356f, 388f, 356f, 12, TextAlignmentOptions.MidlineRight);
        }

        /// <summary>The version and studio line in the title screen's bottom corner (centred in portrait).</summary>
        void Credit(Transform root, float x, float y, float width, float size, TextAlignmentOptions align) =>
            Ui.Text(root, "Credit", x, y, width, 18, mono, size, Lcd.Text3, align,
            $"v{Application.version} · by dev.play.repeat");

        /// <summary>
        /// The smiley bit grid (8 × 52 px cells, 6 px gaps, 18 px padding: 494 px square); its bits join the intro.
        /// </summary>
        void TitleGrid(Transform root, float gridX, float gridY, float cell = 52f, float gap = 6f, float pad = 18f)
        {
            float scale = cell / 52f;
            Ui.Box(root, "Grid", gridX, gridY, 2 * pad + BoardSize * cell + (BoardSize - 1) * gap,
                2 * pad + BoardSize * cell + (BoardSize - 1) * gap, 10f, Lcd.Cell);
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    bool on = ((TitleBits[row] >> (7 - column)) & 1) != 0;
                    float cellX = gridX + pad + column * (cell + gap), cellY = gridY + pad + row * (cell + gap);
                    var box = Ui.Box(root, $"Cell {row + 1},{column + 1}", cellX, cellY, cell, cell, 8f * scale, on
                        ? Lcd.Ink
                        : Lcd.Background);
                    if (!on)
                    {
                        continue;
                    }

                    titleBits.Add(new TitleBit
                    {
                        Column = column,
                        Body = box,
                        LeftEye = Ui.Box(root, "Eye L", cellX + 13 * scale, cellY + 16 * scale, 7 * scale, 9 * scale,
                        2f * scale, Lcd.Light),
                        RightEye = Ui.Box(root, "Eye R", cellX + 32 * scale, cellY + 16 * scale, 7 * scale, 9 * scale,
                        2f * scale, Lcd.Light)
                    });
                }
            }
        }

        void KeyCap(Transform root, string text, float x, float y, float width)
        {
            Ui.Frame(root, text, x, y, width, 34, 6f, 2f, Lcd.Ink);
            Ui.Text(root, text + " Label", x, y, width, 34, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, text);
        }

        void BuildChapters(Screens screens)
        {
            var root = screens.Chapters;
            var left = Outer(screens, root, "Left", -1f, 0f);
            var right = Outer(screens, root, "Right", 1f, 0f);
            const float rowWidth = 163f, rowHeight = 62f;
            Ui.Text(left, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "CHAPTERS");
            screens.ChaptersStats = Ui.Text(right, "Stats", 500, 36, 600, 48, mono, 15, Lcd.Text2,
                TextAlignmentOptions.MidlineRight);
            HudButton.Create(right, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));

            var chapters = game.Catalog.chapters;
            const float top = 100f, height = 580f, gap = 14f;
            float width = (1168f - (chapters.Count - 1) * gap) / Mathf.Max(1, chapters.Count);
            var thumbCell = ProceduralSprites.RoundedRect(5, 5, 1f, ui: true);

            for (int chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
            {
                var chapter = chapters[chapterIndex];
                float chapterX = 56f + chapterIndex * (width + gap);
                Ui.Box(root, $"Chapter {chapterIndex + 1}", chapterX, top, width, height, 10f, Lcd.Card);
                Ui.Frame(root, $"Chapter {chapterIndex + 1} Frame", chapterX, top, width, height, 10f, 2f, Lcd.Ink);
                Ui.Text(root, "Label", chapterX + 16, top + 16, width - 32, 16, mono, 12, Lcd.Text3,
                    text: $"CHAPTER {chapterIndex + 1}");
                var name = Ui.Text(root, "Name", chapterX + 16, top + 34, width - 32, 24, display, 17, Lcd.Ink,
                    text: chapter.name.ToUpperInvariant());
                name.enableAutoSizing = true;
                name.fontSizeMin = 11;
                name.fontSizeMax = 17;
                var text = Ui.Text(root, "Text", chapterX + 16, top + 62, width - 32, 70, mono, 12.5f, Lcd.Text2,
                    TextAlignmentOptions.TopLeft, chapter.description);
                text.textWrappingMode = TextWrappingModes.Normal;

                var list = new List<LevelRow>();
                screens.Rows.Add(list);
                if (chapter.levels.Count == 0)
                {
                    Ui.Text(root, "Soon", chapterX + 16, top + 140, width - 32, 20, mono, 13, Lcd.Text3,
                        text: "Coming soon");
                    continue;
                }

                for (int levelIndex = 0; levelIndex < chapter.levels.Count; levelIndex++)
                {
                    float rowX = chapterX + (width - rowWidth) / 2f, rowY = top + 136f + levelIndex * (rowHeight + 8f);
                    var rowRoot = Ui.Rect(root, $"Level {chapterIndex + 1}-{levelIndex + 1}", rowX, rowY, rowWidth,
                        rowHeight);
                    var row = NewRow(chapterIndex, levelIndex, rowRoot, rowX, rowY, rowWidth, rowHeight);
                    Ui.Box(rowRoot, "Thumb", 4, 4, 54, 54, 4f, Lcd.Background);
                    for (int cellRow = 0; cellRow < BoardSize; cellRow++)
                    {
                        for (int cellColumn = 0; cellColumn < BoardSize; cellColumn++)
                        {
                            row.Thumb[cellRow * BoardSize + cellColumn] = Ui.Shape(rowRoot, "Px", 8 + cellColumn * 6,
                                8 + cellRow * 6, 5, 5, thumbCell, Lcd.Cell);
                        }
                    }

                    row.Id = Ui.Text(rowRoot, "Id", 68, 6, 95, 16, mono, 12, Lcd.Text3);
                    row.Name = Ui.Text(rowRoot, "Name", 68, 22, 95, 18, mono, 13, Lcd.Ink);
                    row.Locked = Ui.Text(rowRoot, "Locked", 68, 42, 95, 14, mono, 11, Lcd.Text3, text: "locked");
                    RowStars(row, rowRoot, 68, 42, 13, 16);
                    FinishRow(row, rowRoot);
                    list.Add(row);
                }
            }

            screens.Focus = Ui.Frame(root, "Focus", 0, 0, rowWidth, rowHeight, 8f, 3f, Lcd.Ink);
            screens.Focus.enabled = false;
            screens.ChaptersKeys = Ui.Text(left, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3);
        }

        /// <summary>
        /// Phone portrait chapter select (P3 at phone size): a scrolling list of chapter cards with level tiles.
        /// </summary>
        void BuildChaptersPhonePortrait(Screens screens)
        {
            var root = screens.Chapters;
            var top = Outer(screens, root, "Top", 0f, 1f);
            var bottom = Outer(screens, root, "Bottom", 0f, -1f);
            Ui.Text(top, "Heading", 16, 24, 270, 40, displayBold, 28, Lcd.Ink, text: "CHAPTERS");
            HudButton.Create(top, "BACK", 300, 22, 96, 48, display, 15, () => flow.Show(GameScreen.Title));
            screens.ChaptersStats = Ui.Text(top, "Stats", 16, 76, 380, 18, mono, 14, Lcd.Text2);

            // Scroll view: a masked viewport, the content grows with the cards.
            var viewport = Ui.Rect(top, "Scroll", 16, 104, 380, PhoneScrollHeight);
            screens.ScrollView = viewport;
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            var content = Ui.Rect(viewport, "Content", 0, 0, 380, 100);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            screens.Scroll = scroll;

            var chapters = game.Catalog.chapters;
            var thumbCell = ProceduralSprites.RoundedRect(6, 6, 1.5f, ui: true);
            const float cardWidth = 380f, cardHeight = 246f, cardGap = 12f, tileWidth = 68f, tileHeight = 128f,
                tileGap = 4f;
            float y = 0f;
            for (int chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
            {
                var chapter = chapters[chapterIndex];
                screens.CardTops.Add(y);
                Ui.Box(content, $"Chapter {chapterIndex + 1}", 0, y, cardWidth, cardHeight, 10f, Lcd.Card);
                Ui.Frame(content, $"Chapter {chapterIndex + 1} Frame", 0, y, cardWidth, cardHeight, 10f, 2f, Lcd.Ink);
                Ui.Text(content, "Label", 12, y + 12, 300, 16, mono, 13, Lcd.Text3,
                    text: $"CHAPTER {chapterIndex + 1}").characterSpacing = 4;
                Ui.Text(content, "Name", 12, y + 30, 356, 22, display, 18, Lcd.Ink,
                    text: chapter.name.ToUpperInvariant());
                var text = Ui.Text(content, "Text", 12, y + 56, 356, 42, mono, 14, Lcd.Text2,
                    TextAlignmentOptions.TopLeft, chapter.description);
                text.textWrappingMode = TextWrappingModes.Normal;
                text.enableAutoSizing = true;
                text.fontSizeMin = 12;
                text.fontSizeMax = 14;

                var list = new List<LevelRow>();
                screens.Rows.Add(list);
                if (chapter.levels.Count == 0)
                {
                    Ui.Text(content, "Soon", 12, y + 110, 300, 20, mono, 14, Lcd.Text3, text: "Coming soon");
                }

                for (int levelIndex = 0; levelIndex < chapter.levels.Count; levelIndex++)
                {
                    float tileX = 12f + levelIndex * (tileWidth + tileGap), tileY = y + 104f;
                    var tile = Ui.Rect(content, $"Level {chapterIndex + 1}-{levelIndex + 1}", tileX, tileY, tileWidth,
                        tileHeight);
                    var row = NewRow(chapterIndex, levelIndex, tile, tileX, tileY, tileWidth, tileHeight);
                    Ui.Box(tile, "Thumb", 2, 2, 64, 64, 4f, Lcd.Background);
                    for (int cellRow = 0; cellRow < BoardSize; cellRow++)
                    {
                        for (int cellColumn = 0; cellColumn < BoardSize; cellColumn++)
                        {
                            row.Thumb[cellRow * BoardSize + cellColumn] = Ui.Shape(tile, "Px", 6 + cellColumn * 7,
                                6 + cellRow * 7, 6, 6, thumbCell, Lcd.Cell);
                        }
                    }

                    row.Id = Ui.Text(tile, "Id", 2, 70, 64, 16, mono, 13, Lcd.Text3);
                    row.Name = ShrinkingName(tile, 2, 86, 64, 16, 12);
                    row.Locked = Ui.Text(tile, "Locked", 2, 106, 64, 16, mono, 12, Lcd.Text3, text: "locked");
                    RowStars(row, tile, 2, 107, 13, 16);
                    FinishRow(row, tile);
                    list.Add(row);
                }

                y += cardHeight + cardGap;
            }

            content.sizeDelta = new Vector2(cardWidth, y);

            screens.Focus = Ui.Frame(content, "Focus", 0, 0, tileWidth, tileHeight, 6f, 3f, Lcd.Ink);
            screens.Focus.enabled = false;
            screens.ChaptersKeys = Ui.Text(bottom, "Keys", 16, 886, 380, 18, mono, 13, Lcd.Text3,
                TextAlignmentOptions.Center);
        }

        /// <summary>
        /// Phone landscape chapter select: six columns with the levels as rows (descriptions are in help).
        /// </summary>
        void BuildChaptersPhoneLandscape(Screens screens)
        {
            var root = screens.Chapters;
            var left = Outer(screens, root, "Left", -1f, 0f);
            var right = Outer(screens, root, "Right", 1f, 0f);
            Ui.Text(left, "Heading", 24, 16, 300, 40, displayBold, 26, Lcd.Ink, text: "CHAPTERS");
            screens.ChaptersStats = Ui.Text(right, "Stats", 330, 16, 450, 40, mono, 14, Lcd.Text2,
                TextAlignmentOptions.MidlineRight);
            HudButton.Create(right, "BACK", 795, 14, 96, 44, display, 15, () => flow.Show(GameScreen.Title));

            var chapters = game.Catalog.chapters;
            const float top = 66f, height = 330f, gap = 8f, rowHeight = 52f;
            float width = (867f - (chapters.Count - 1) * gap) / Mathf.Max(1, chapters.Count);
            float rowWidth = width - 8f;
            var thumbCell = ProceduralSprites.RoundedRect(4, 4, 1f, ui: true);
            for (int chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
            {
                var chapter = chapters[chapterIndex];
                float chapterX = 24f + chapterIndex * (width + gap);
                Ui.Box(root, $"Chapter {chapterIndex + 1}", chapterX, top, width, height, 8f, Lcd.Card);
                Ui.Frame(root, $"Chapter {chapterIndex + 1} Frame", chapterX, top, width, height, 8f, 2f, Lcd.Ink);
                Ui.Text(root, "Label", chapterX + 8, top + 8, width - 16, 16, mono, 13, Lcd.Text3,
                    text: $"CHAPTER {chapterIndex + 1}");
                var name = Ui.Text(root, "Name", chapterX + 8, top + 26, width - 16, 20, display, 14, Lcd.Ink,
                    text: chapter.name.ToUpperInvariant());
                name.enableAutoSizing = true;
                name.fontSizeMin = 10;
                name.fontSizeMax = 14;

                var list = new List<LevelRow>();
                screens.Rows.Add(list);
                if (chapter.levels.Count == 0)
                {
                    Ui.Text(root, "Soon", chapterX + 8, top + 56, width - 16, 20, mono, 13, Lcd.Text3,
                        text: "Coming soon");
                }

                for (int levelIndex = 0; levelIndex < chapter.levels.Count; levelIndex++)
                {
                    float rowX = chapterX + 4f, rowY = top + 52f + levelIndex * rowHeight;
                    var rowRoot = Ui.Rect(root, $"Level {chapterIndex + 1}-{levelIndex + 1}", rowX, rowY, rowWidth,
                        rowHeight);
                    var row = NewRow(chapterIndex, levelIndex, rowRoot, rowX, rowY, rowWidth, rowHeight);
                    Ui.Box(rowRoot, "Thumb", 2, 4, 44, 44, 4f, Lcd.Background);
                    for (int cellRow = 0; cellRow < BoardSize; cellRow++)
                    {
                        for (int cellColumn = 0; cellColumn < BoardSize; cellColumn++)
                        {
                            row.Thumb[cellRow * BoardSize + cellColumn] = Ui.Shape(rowRoot, "Px", 5 + cellColumn * 5,
                                7 + cellRow * 5, 4, 4, thumbCell, Lcd.Cell);
                        }
                    }

                    row.Id = Ui.Text(rowRoot, "Id", 52, 4, rowWidth - 54, 16, mono, 13, Lcd.Text3);
                    row.Name = ShrinkingName(rowRoot, 52, 20, rowWidth - 54, 16, 13);
                    row.Locked = Ui.Text(rowRoot, "Locked", 52, 36, rowWidth - 54, 14, mono, 12, Lcd.Text3,
                        text: "locked");
                    RowStars(row, rowRoot, 52, 37, 12, 14);
                    FinishRow(row, rowRoot);
                    list.Add(row);
                }
            }

            screens.Focus = Ui.Frame(root, "Focus", 0, 0, rowWidth, rowHeight, 6f, 3f, Lcd.Ink);
            screens.Focus.enabled = false;
        }

        /// <summary>
        /// A level name that shrinks a little when it does not fit (e.g. "Four Gates" on a narrow tile).
        /// </summary>
        TextMeshProUGUI ShrinkingName(Transform root, float x, float y, float width, float height, float size)
        {
            var label = Ui.Text(root, "Name", x, y, width, height, mono, size, Lcd.Ink);
            label.enableAutoSizing = true;
            label.fontSizeMin = size - 3f;
            label.fontSizeMax = size;
            return label;
        }

        LevelRow NewRow(int chapter, int level, RectTransform root, float x, float y, float width, float height) =>
            new LevelRow
            {
                Chapter = chapter,
                Level = level,
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Group = root.gameObject.AddComponent<CanvasGroup>(),
                Thumb = new Image[BoardSize * BoardSize],
                Stars = new Image[Scoring.MaxStars],
                StarOutlines = new Image[Scoring.MaxStars]
            };

        static void RowStars(LevelRow row, Transform root, float x, float y, float size, float pitch)
        {
            for (int star = 0; star < Scoring.MaxStars; star++)
            {
                row.StarOutlines[star] = Ui.Shape(root, "Star Outline", x + star * pitch, y, size, size,
                    ProceduralSprites.Star(size, 1.2f, ui: true), Lcd.Ink);
                row.Stars[star] = Ui.Shape(root, "Star", x + star * pitch, y, size, size,
                    ProceduralSprites.Star(size, ui: true), Lcd.Ink);
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
                for (int cellRow = 0; cellRow < BoardSize; cellRow++)
                {
                    for (int cellColumn = 0; cellColumn < BoardSize; cellColumn++)
                    {
                        if (((entry.Data.target[cellRow] >> (7 - cellColumn)) & 1) != 0)
                        {
                            row.Thumb[cellRow * BoardSize + cellColumn].color = Lcd.Ink;
                        }
                    }
                }
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
            foreach (var entry in game.Levels)
            {
                if (game.Progress.IsSolved(entry.Data.id))
                {
                    solved++;
                }
            }

            string text = solved == 0
                ? $"{game.Levels.Count} levels to solve"
                : $"{solved} of {game.Levels.Count} levels solved · {game.Progress.TotalStars()} stars";
            foreach (var screens in AllScreens)
            {
                screens.TitleProgress.text = text;
            }
        }

        void RefreshChapters()
        {
            int solved = 0;
            foreach (var entry in game.Levels)
            {
                if (game.Progress.IsSolved(entry.Data.id))
                {
                    solved++;
                }
            }

            foreach (var screens in AllScreens)
            {
                foreach (var list in screens.Rows)
                {
                    foreach (var row in list)
                    {
                        var entry = game.Entry(row.Chapter, row.Level);
                        if (entry == null)
                        {
                            continue;
                        }

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
                }

                screens.ChaptersStats.text =
                    $"{solved} of {game.Levels.Count} levels · {game.Progress.TotalStars()} stars";
                screens.Focus.enabled = false; // no focus tick when the screen opens
            }

            // Focus the current or resume level, and scroll the portrait list to its chapter.
            var (chapter, level) = game.Board != null ? (game.ChapterIndex, game.LevelIndex) : game.ResumeTarget();
            SetFocus(chapter, level);
            if (focusChapter >= 0 && focusChapter < phoneTall.CardTops.Count)
            {
                var content = phoneTall.Scroll.content;
                float max = Mathf.Max(0f, content.sizeDelta.y - phoneTall.ScrollView.sizeDelta.y);
                content.anchoredPosition = new Vector2(0f, Mathf.Clamp(phoneTall.CardTops[focusChapter], 0f, max));
            }
        }

        // ---------------------------------------------------------------- focus

        void StepChapter(int delta)
        {
            var rows = wide.Rows;
            for (int chapter = focusChapter + delta; chapter >= 0 && chapter < rows.Count; chapter += delta)
            {
                if (rows[chapter].Count == 0)
                {
                    continue;
                }

                SetFocus(chapter, Mathf.Min(focusLevel, rows[chapter].Count - 1));
                return;
            }
        }

        void SetFocus(int chapter, int level)
        {
            var rows = wide.Rows;
            if (chapter < 0 || chapter >= rows.Count || rows[chapter].Count == 0)
            {
                // Fall back to the first chapter that has levels.
                chapter = rows.FindIndex(chapterRows => chapterRows.Count > 0);
                if (chapter < 0)
                {
                    return;
                }

                level = 0;
            }

            level = Mathf.Clamp(level, 0, rows[chapter].Count - 1);
            if (Current.Focus.enabled && (chapter != focusChapter || level != focusLevel))
            {
                Sfx.Play(Sound.Tick);
            }

            focusChapter = chapter;
            focusLevel = level;
            foreach (var screens in AllScreens)
            {
                var row = screens.Rows[chapter][level];
                screens.Focus.rectTransform.anchoredPosition = new Vector2(row.Position.x + row.Size.x / 2f,
                    -(row.Position.y + row.Size.y / 2f));
                screens.Focus.enabled = true;
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
