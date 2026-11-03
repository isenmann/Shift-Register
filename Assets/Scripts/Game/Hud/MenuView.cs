using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// Title screen, chapter select and options, built from code on their own canvas.
    /// Title: Enter plays, C opens chapters, O options. Chapters: arrows pick a level, Enter plays it.
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
            public Vector2 Position;
            public Image[] Thumb;
            public TextMeshProUGUI Id, Name, Locked;
            public Image[] Stars;
            public Image[] StarOutlines;
            public CanvasGroup Group;
        }

        RectTransform titleRoot, chaptersRoot, optionsRoot;
        TextMeshProUGUI titleProgress, chaptersStats;
        Image focusFrame;
        readonly List<List<LevelRow>> rows = new List<List<LevelRow>>();
        int focusChapter = -1, focusLevel;
        bool built;

        const float RowW = 163f, RowH = 62f;

        public void Show(GameScreen screen)
        {
            if (!built) Build();
            titleRoot.gameObject.SetActive(screen == GameScreen.Title);
            chaptersRoot.gameObject.SetActive(screen == GameScreen.Chapters);
            optionsRoot.gameObject.SetActive(screen == GameScreen.Options);
            if (screen == GameScreen.Options) RefreshOptions(true);
            if (screen == GameScreen.Title) RefreshTitle();
            if (screen == GameScreen.Chapters) RefreshChapters();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || flow == null || !built) return;

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
            titleRoot = Frame(canvas, "Title Screen");
            chaptersRoot = Frame(canvas, "Chapter Select");
            optionsRoot = Frame(canvas, "Options");
            BuildTitle(titleRoot);
            BuildChapters(chaptersRoot);
            BuildOptions(optionsRoot);
        }

        static RectTransform Frame(RectTransform canvas, string name)
        {
            var frame = Ui.Rect(canvas, name, 0, 0, 1280, 720);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            return frame;
        }

        void BuildTitle(Transform root)
        {
            const float x = 80f;
            var tagline = Ui.Text(root, "Tagline", x, 120, 560, 20, mono, 14, Lcd.Text3, text: "A PUZZLE ABOUT MOVING BITS · GAME OFF 2026");
            tagline.characterSpacing = 12;
            Ui.Text(root, "Shift", x - 4, 160, 620, 100, displayBold, 100, Lcd.Ink, text: "SHIFT");
            Ui.Text(root, "Register", x - 4, 255, 620, 100, displayBold, 100, Lcd.Ink, text: "REGISTER");
            var blurb = Ui.Text(root, "Blurb", x, 378, 560, 52, mono, 17, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "Slide whole rows of bits to draw the picture. Whatever falls off the edge is gone for good.");
            blurb.textWrappingMode = TextWrappingModes.Normal;

            HudButton.CreateSolid(root, "PLAY", x, 458, 130, 52, display, 18, () => flow.Resume());
            HudButton.Create(root, "CHAPTERS", x + 142, 458, 170, 52, display, 16, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(root, "OPTIONS", x + 324, 458, 160, 52, display, 16, () => flow.Show(GameScreen.Options));
            titleProgress = Ui.Text(root, "Progress", x, 526, 560, 20, mono, 14, Lcd.Text2);
            Ui.Text(root, "Keys", x, 550, 560, 20, mono, 13, Lcd.Text3, text: "Enter play · C chapters · O options");

            // The smiley bit grid (mockup: 8 × 52 px cells, 6 px gaps, 18 px padding).
            const float gx = 740f, gy = 120f, cell = 52f, gap = 6f, pad = 18f;
            Ui.Box(root, "Grid", gx, gy, 2 * pad + N * cell + (N - 1) * gap, 2 * pad + N * cell + (N - 1) * gap, 10f, Lcd.Cell);
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                bool on = ((TitleBits[r] >> (7 - c)) & 1) != 0;
                float cx = gx + pad + c * (cell + gap), cy = gy + pad + r * (cell + gap);
                Ui.Box(root, $"Cell {r + 1},{c + 1}", cx, cy, cell, cell, 8f, on ? Lcd.Ink : Lcd.Background);
                if (!on) continue;
                Ui.Box(root, "Eye L", cx + 13, cy + 16, 7, 9, 2f, Lcd.Light);
                Ui.Box(root, "Eye R", cx + 32, cy + 16, 7, 9, 2f, Lcd.Light);
            }

            const float ky = 642f;
            KeyCap(root, "⇧ Shift", x, ky, 82);
            Ui.Text(root, "Plus", x + 82, ky, 28, 34, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, "+");
            KeyCap(root, "← → ↑ ↓", x + 110, ky, 112);
            Ui.Text(root, "Hint", x + 236, ky, 400, 34, mono, 15, Lcd.Text2, text: "shifts the selected row or column");
        }

        void KeyCap(Transform root, string text, float x, float y, float w)
        {
            Ui.Frame(root, text, x, y, w, 34, 6f, 2f, Lcd.Ink);
            Ui.Text(root, text + " Label", x, y, w, 34, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, text);
        }

        void BuildChapters(Transform root)
        {
            Ui.Text(root, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "CHAPTERS");
            chaptersStats = Ui.Text(root, "Stats", 500, 36, 600, 48, mono, 15, Lcd.Text2, TextAlignmentOptions.MidlineRight);
            HudButton.Create(root, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));

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
                rows.Add(list);
                if (ch.levels.Count == 0)
                {
                    Ui.Text(root, "Soon", cx + 16, top + 140, w - 32, 20, mono, 13, Lcd.Text3, text: "Coming soon");
                    continue;
                }

                for (int li = 0; li < ch.levels.Count; li++)
                {
                    var entry = game.Entry(ci, li);
                    float rx = cx + (w - RowW) / 2f, ry = top + 136f + li * (RowH + 8f);
                    var rowRoot = Ui.Rect(root, $"Level {ci + 1}-{li + 1}", rx, ry, RowW, RowH);
                    var row = new LevelRow
                    {
                        Chapter = ci,
                        Level = li,
                        Position = new Vector2(rx, ry),
                        Group = rowRoot.gameObject.AddComponent<CanvasGroup>(),
                        Thumb = new Image[N * N],
                        Stars = new Image[Scoring.MaxStars],
                        StarOutlines = new Image[Scoring.MaxStars]
                    };
                    Ui.Box(rowRoot, "Thumb", 4, 4, 54, 54, 4f, Lcd.Background);
                    for (int r = 0; r < N; r++)
                    for (int c = 0; c < N; c++)
                        row.Thumb[r * N + c] = Ui.Shape(rowRoot, "Px", 8 + c * 6, 8 + r * 6, 5, 5, thumbCell, Lcd.Cell);
                    row.Id = Ui.Text(rowRoot, "Id", 68, 6, 95, 16, mono, 12, Lcd.Text3);
                    row.Name = Ui.Text(rowRoot, "Name", 68, 22, 95, 18, mono, 13, Lcd.Ink);
                    row.Locked = Ui.Text(rowRoot, "Locked", 68, 42, 95, 14, mono, 11, Lcd.Text3, text: "locked");
                    for (int s = 0; s < Scoring.MaxStars; s++)
                    {
                        row.StarOutlines[s] = Ui.Shape(rowRoot, "Star Outline", 68 + s * 16, 42, 13, 13, ProceduralSprites.Star(13, 1.2f, ui: true), Lcd.Ink);
                        row.Stars[s] = Ui.Shape(rowRoot, "Star", 68 + s * 16, 42, 13, 13, ProceduralSprites.Star(13, ui: true), Lcd.Ink);
                    }
                    if (entry != null)
                    {
                        row.Id.text = entry.Data.id;
                        row.Name.text = $"<b>{entry.Data.name}</b>";
                        for (int r = 0; r < N; r++)
                        for (int c = 0; c < N; c++)
                            if (((entry.Data.target[r] >> (7 - c)) & 1) != 0) row.Thumb[r * N + c].color = Lcd.Ink;
                    }

                    int chapterIndex = ci, levelIndex = li;
                    var hot = UiHotspot.Create(rowRoot, "Hotspot", 0, 0, RowW, RowH);
                    hot.Enter = () => SetFocus(chapterIndex, levelIndex);
                    hot.Click = () => TryPlay(chapterIndex, levelIndex);
                    list.Add(row);
                }
            }

            focusFrame = Ui.Frame(root, "Focus", 0, 0, RowW, RowH, 8f, 3f, Lcd.Ink);
            focusFrame.enabled = false;
            Ui.Text(root, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3,
                text: "Arrows choose · Enter play · Esc back · you can skip one unsolved level at a time");
        }

        // ---------------------------------------------------------------- refresh

        void RefreshTitle()
        {
            int solved = 0;
            foreach (var e in game.Levels) if (game.Progress.IsSolved(e.Data.id)) solved++;
            titleProgress.text = solved == 0
                ? $"{game.Levels.Count} levels to solve"
                : $"{solved} of {game.Levels.Count} levels solved · {game.Progress.TotalStars()} stars";
        }

        void RefreshChapters()
        {
            int solved = 0;
            foreach (var list in rows)
            foreach (var row in list)
            {
                var entry = game.Entry(row.Chapter, row.Level);
                if (entry == null) continue;
                bool open = game.IsUnlocked(row.Chapter, row.Level);
                int stars = game.Progress.StarsFor(entry.Data.id);
                if (game.Progress.IsSolved(entry.Data.id)) solved++;
                row.Group.alpha = open ? 1f : 0.4f;
                row.Locked.enabled = !open;
                for (int s = 0; s < row.Stars.Length; s++)
                {
                    row.Stars[s].enabled = open && s < stars;
                    row.StarOutlines[s].enabled = open;
                }
            }
            chaptersStats.text = $"{solved} of {game.Levels.Count} levels · {game.Progress.TotalStars()} stars";

            // Focus the current or resume level.
            var (c, l) = game.Board != null ? (game.ChapterIndex, game.LevelIndex) : game.ResumeTarget();
            focusFrame.enabled = false; // no focus tick when the screen opens
            SetFocus(c, l);
        }

        // ---------------------------------------------------------------- focus

        void StepChapter(int delta)
        {
            for (int c = focusChapter + delta; c >= 0 && c < rows.Count; c += delta)
            {
                if (rows[c].Count == 0) continue;
                SetFocus(c, Mathf.Min(focusLevel, rows[c].Count - 1));
                return;
            }
        }

        void SetFocus(int chapter, int level)
        {
            if (chapter < 0 || chapter >= rows.Count || rows[chapter].Count == 0)
            {
                // Fall back to the first chapter that has levels.
                chapter = rows.FindIndex(r => r.Count > 0);
                if (chapter < 0) return;
                level = 0;
            }
            level = Mathf.Clamp(level, 0, rows[chapter].Count - 1);
            if (focusFrame.enabled && (chapter != focusChapter || level != focusLevel)) Sfx.Play(Sound.Tick);
            focusChapter = chapter;
            focusLevel = level;
            var row = rows[chapter][level];
            var rt = focusFrame.rectTransform;
            rt.anchoredPosition = new Vector2(row.Position.x + RowW / 2f, -(row.Position.y + RowH / 2f));
            focusFrame.enabled = true;
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
