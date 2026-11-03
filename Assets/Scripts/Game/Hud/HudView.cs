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
    /// The in-level HUD, layout A3b from the redesign canvas: a left rail (chapter and its rule, level,
    /// target, moves/par, star outlook, undo/reset), two-line row tags beside the board (bits over
    /// "now → target"), a right rail lined up with the board (bit bucket, move log, keys,
    /// menu/help/sound), and the solved / rule-broken banner over the board.
    /// Built from code on a Screen Space canvas scaled like the camera (1280×720 reference, Expand).
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

        // Layout A in 1280×720 mockup px: the board's cells span x 360–832, y 124–596 (52 px cells, 60 px pitch).
        const float RowsX = 892f, RowsTop = 124f, RowPitch = 60f, RowBox = 84f;
        const float LeftX = 48f, LeftW = 232f, RightX = 1048f, RightW = 192f;

        TextMeshProUGUI chapterLabel, levelName, levelId, movesValue, parValue, outlook;
        readonly Image[] outlookStars = new Image[Scoring.MaxStars];
        readonly TextMeshProUGUI[] rowBits = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowFrom = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowArrow = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowTo = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowTag = new TextMeshProUGUI[N];
        readonly Image[] rowTagBox = new Image[N];
        readonly Image[] thumb = new Image[N * N];
        TextMeshProUGUI targetText, bucketCount, bucketText, logEmpty;
        // Move log rows, newest first; the last ones fade out instead of counting what is hidden.
        const int LogRows = 7;
        static readonly float[] LogAlpha = { 1f, 1f, 1f, 1f, 0.55f, 0.3f, 0.12f };
        readonly TextMeshProUGUI[] logIndex = new TextMeshProUGUI[LogRows];
        readonly TextMeshProUGUI[] logMove = new TextMeshProUGUI[LogRows];
        Image bucketFrame;
        RectTransform solvedBanner, brokenBanner;
        TextMeshProUGUI solvedText, lastLevelText;
        readonly Image[] stars = new Image[Scoring.MaxStars];
        HudButton nextButton, soundButton;
        int shownLost;
        bool solvedShown, brokenShown;

        void Awake() => Build();

        void OnEnable()
        {
            if (game == null) return;
            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
            game.Changed += OnChanged;
            game.SettingsChanged += RefreshSound;
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
            if (soundButton != null) soundButton.Text = game.Settings.muted ? "MUTED" : "SOUND";
        }

        void OnSettled()
        {
            if (game.Board != null) RefreshOutcome(true);
        }

        void Update() => UpdateHelp();

        void Build()
        {
            var canvas = (RectTransform)transform;
            var frame = Ui.Rect(canvas, "Frame", 0, 0, 1280, 720);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;

            BuildLeftRail(frame);
            BuildRows(frame);
            BuildRightRail(frame);
            BuildBanner(frame);
            BuildHelp(canvas);
        }

        void BuildLeftRail(Transform frame)
        {
            const float x = LeftX, w = LeftW;
            chapterLabel = Ui.Text(frame, "Chapter", x, 56, w, 18, mono, 13, Lcd.Text3);
            chapterLabel.characterSpacing = 10;
            targetText = Ui.Text(frame, "Chapter Rule", x, 78, w, 54, mono, 12, Lcd.Text2, TextAlignmentOptions.TopLeft);
            targetText.textWrappingMode = TextWrappingModes.Normal;
            targetText.lineSpacing = 4;
            levelName = Ui.Text(frame, "Level Name", x, 134, w, 46, displayBold, 44, Lcd.Ink);
            levelName.enableAutoSizing = true;
            levelName.fontSizeMin = 20;
            levelName.fontSizeMax = 44;
            levelId = Ui.Text(frame, "Level Id", x, 182, w, 18, mono, 14, Lcd.Text2);

            const float cardY = 212f, pad = 16f, thumbCell = 20f, thumbPitch = 23f;
            Ui.Box(frame, "Target Card", x, cardY, w, 237, 10f, Lcd.Cell);
            Ui.Text(frame, "Target Title", x + pad, cardY + 14, 160, 18, display, 14, Lcd.Ink, text: "TARGET");
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                thumb[r * N + c] = Ui.Box(frame, $"Thumb {r + 1},{c + 1}", x + pad + c * thumbPitch, cardY + 42f + r * thumbPitch,
                    thumbCell, thumbCell, 3f, Lcd.Background);

            const float tileY = 465f, tileW = 110f, tileH = 50f, gap = 12f;
            Ui.Box(frame, "Moves Tile", x, tileY, tileW, tileH, 10f, Lcd.Cell);
            Ui.Text(frame, "Moves Label", x + 14, tileY, 60, tileH, mono, 12, Lcd.Text3, text: "MOVES").characterSpacing = 10;
            movesValue = Ui.Text(frame, "Moves", x + 14, tileY, tileW - 28, tileH, display, 30, Lcd.Ink, TextAlignmentOptions.MidlineRight);
            float parX = x + tileW + gap;
            Ui.Frame(frame, "Par Tile", parX, tileY, tileW, tileH, 10f, 2f, Lcd.Ink);
            Ui.Text(frame, "Par Label", parX + 14, tileY, 60, tileH, mono, 12, Lcd.Text3, text: "PAR").characterSpacing = 10;
            parValue = Ui.Text(frame, "Par", parX + 14, tileY, tileW - 28, tileH, display, 30, Lcd.Ink, TextAlignmentOptions.MidlineRight);

            const float starY = 531f, star = 20f;
            for (int i = 0; i < Scoring.MaxStars; i++)
            {
                float sx = x + i * (star + 6f);
                Ui.Shape(frame, $"Outlook Star {i + 1} Outline", sx, starY, star, star, ProceduralSprites.Star(star, 1.6f, ui: true), Lcd.Ink);
                outlookStars[i] = Ui.Shape(frame, $"Outlook Star {i + 1}", sx, starY, star, star, ProceduralSprites.Star(star, ui: true), Lcd.Ink);
            }
            outlook = Ui.Text(frame, "Outlook", x + Scoring.MaxStars * (star + 6f) + 4f, starY, 160, star, mono, 12, Lcd.Text2);

            // Bottom edge level with the board's down-arrow row (mockup y 644).
            HudButton.CreateSolid(frame, "UNDO", x, 592, tileW, 52, display, 15, () => game.Undo());
            HudButton.Create(frame, "RESET", parX, 592, tileW, 52, display, 15, () => game.ResetLevel());
        }

        /// <summary>
        /// One tag per board row: the bits spread over a fixed box, under them "now → target" with the
        /// numbers pinned to the box edges and the arrow in its middle; then OK, the hint or a label.
        /// </summary>
        void BuildRows(Transform frame)
        {
            // JetBrains Mono advances 0.6 em; spread 8 glyphs so the first and last touch the box edges.
            const float bitSize = 13f;
            float spacingPx = (RowBox - 8f * 0.6f * bitSize) / 7f;
            for (int r = 0; r < N; r++)
            {
                float y = RowsTop + r * RowPitch;
                rowBits[r] = Ui.Text(frame, $"Row {r + 1} Bits", RowsX, y + 6, RowBox + 8, 18, mono, bitSize, Lcd.Text3);
                rowBits[r].characterSpacing = spacingPx / bitSize * 100f;
                rowFrom[r] = Ui.Text(frame, $"Row {r + 1} Now", RowsX, y + 26, RowBox, 20, mono, 14, Lcd.Ink);
                rowArrow[r] = Ui.Text(frame, $"Row {r + 1} Arrow", RowsX, y + 26, RowBox, 20, mono, 14, Lcd.Text3, TextAlignmentOptions.Center, "→");
                rowTo[r] = Ui.Text(frame, $"Row {r + 1} Target", RowsX, y + 26, RowBox, 20, mono, 14, Lcd.Text3, TextAlignmentOptions.MidlineRight);
                float tagX = RowsX + RowBox + 10f;
                rowTagBox[r] = Ui.Box(frame, $"Row {r + 1} Tag Box", tagX, y + 16, 34, 20, 4f, Lcd.Ink);
                rowTag[r] = Ui.Text(frame, $"Row {r + 1} Tag", tagX, y + 16, 34, 20, mono, 13, Lcd.Light, TextAlignmentOptions.Center);
            }
        }

        /// <summary>The right rail starts level with the board's top row (y 124) and ends with its down arrows (y 644).</summary>
        void BuildRightRail(Transform frame)
        {
            const float x = RightX, w = RightW, pad = 16f;

            const float bucketY = 124f, bucketH = 150f;
            bucketFrame = Ui.Frame(frame, "Bucket Frame", x, bucketY, w, bucketH, 10f, 3f, Lcd.Ink, 8f);
            Ui.Text(frame, "Bucket Title", x + pad, bucketY + 18, 120, 18, display, 14, Lcd.Ink, text: "BIT BUCKET");
            bucketCount = Ui.Text(frame, "Bucket Count", x + pad, bucketY + 10, w - 2 * pad, 34, display, 28, Lcd.Ink, TextAlignmentOptions.MidlineRight);
            bucketText = Ui.Text(frame, "Bucket Text", x + pad, bucketY + 52, w - 2 * pad, 90, mono, 12, Lcd.Text2, TextAlignmentOptions.TopLeft);
            bucketText.textWrappingMode = TextWrappingModes.Normal;
            bucketText.lineSpacing = 4;

            // Move log: a light counterweight to the left rail.
            const float logY = 294f, logRow = 22f;
            Ui.Box(frame, "Log Rule", x, logY, w, 2, 0f, Lcd.Cell);
            Ui.Text(frame, "Log Title", x, logY + 12, w, 16, mono, 12, Lcd.Text3, text: "MOVES SO FAR").characterSpacing = 10;
            for (int i = 0; i < LogRows; i++)
            {
                float y = logY + 36 + i * logRow;
                logIndex[i] = Ui.Text(frame, $"Log {i + 1} Index", x, y, 40, logRow, mono, 13, Lcd.Text3);
                logMove[i] = Ui.Text(frame, $"Log {i + 1} Move", x + 40, y, w - 40, logRow, mono, 13, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            }
            logEmpty = Ui.Text(frame, "Log Empty", x, logY + 36, w, logRow, mono, 13, Lcd.Cell, text: "no moves yet");

            // Key caps, like the title screen's.
            const float k1 = 504f, k2 = 536f, k3 = 568f;
            float kx = KeyCap(frame, "⇧", x, k1, 26f);
            kx = KeyLabel(frame, "+", kx, k1, 18f, TextAlignmentOptions.Center);
            kx = KeyCap(frame, "← → ↑ ↓", kx, k1, 76f);
            KeyLabel(frame, "shift", kx + 6f, k1, 50f);
            kx = KeyCap(frame, "Z", x, k2, 26f);
            kx = KeyLabel(frame, "undo", kx + 6f, k2, 40f);
            kx = KeyCap(frame, "R", kx + 8f, k2, 26f);
            KeyLabel(frame, "reset", kx + 6f, k2, 50f);
            kx = KeyCap(frame, "H", x, k3, 26f);
            kx = KeyLabel(frame, "help", kx + 6f, k3, 40f);
            kx = KeyCap(frame, "M", kx + 8f, k3, 26f);
            KeyLabel(frame, "mute", kx + 6f, k3, 50f);

            const float menuY = 604f;
            HudButton.Create(frame, "MENU", x, menuY, 56, 40, display, 12, () => flow.Show(GameScreen.Chapters));
            HudButton.Create(frame, "HELP", x + 60, menuY, 56, 40, display, 12, ToggleHelp);
            soundButton = HudButton.Create(frame, "SOUND", x + 120, menuY, 72, 40, display, 12,
                () => game.ChangeSettings(s => s.muted = !s.muted));
        }

        /// <summary>Newest move first, numbered; the bottom rows fade so a long log trails off.</summary>
        void RefreshLog()
        {
            var log = game.MoveLog;
            logEmpty.enabled = log.Count == 0;
            for (int i = 0; i < LogRows; i++)
            {
                int m = log.Count - 1 - i;
                bool shown = m >= 0;
                logIndex[i].text = shown ? (m + 1).ToString("00") : "";
                logMove[i].text = shown ? log[m].ToString() : "";
                var ink = i == 0 ? Lcd.Ink : Lcd.Text3;
                Color color = ink;
                color.a = LogAlpha[i];
                logMove[i].color = color;
                Color index = Lcd.Text3;
                index.a = LogAlpha[i];
                logIndex[i].color = index;
                logMove[i].fontStyle = i == 0 ? FontStyles.Bold : FontStyles.Normal;
            }
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
            nextButton = HudButton.Create(solvedBanner, "NEXT \u2192", (w - 150f) / 2f, 140, 150, 40, display, 15, Lcd.Light, Lcd.Ink, () => game.NextLevel());
            lastLevelText = Ui.Text(solvedBanner, "Last Level", 0, 140, w, 40, mono, 13, Lcd.Light, TextAlignmentOptions.Center,
                "That's every level so far.");

            brokenBanner = BannerRoot(frame, "Broken Banner", w, 110f);
            Ui.Box(brokenBanner, "Box", 0, 0, w, 110f, 10f, Lcd.Loss);
            var title = Ui.Text(brokenBanner, "Title", 16, 18, w - 32, 44, displayBold, 40, Lcd.Light, TextAlignmentOptions.Center, "RULE BROKEN");
            title.enableAutoSizing = true;
            title.fontSizeMin = 20;
            title.fontSizeMax = 40;
            Ui.Text(brokenBanner, "Text", 0, 68, w, 24, mono, 15, Lcd.Light, TextAlignmentOptions.Center, "Z or Backspace to undo");
        }

        /// <summary>A hidden banner centred on the board (mockup board centre 596, 360), scaled about its centre.</summary>
        static RectTransform BannerRoot(Transform frame, string name, float w, float h)
        {
            float x = 596f - w / 2f, y = 360f - h / 2f;
            var rt = Ui.Rect(frame, name, x, y, w, h);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + w / 2f, -(y + h / 2f));
            rt.gameObject.SetActive(false);
            return rt;
        }

        void RefreshBoard()
        {
            var board = game.Board;
            var level = game.Level;
            var rules = board.Rules;

            chapterLabel.text = $"CHAPTER {game.ChapterIndex + 1} · {game.Chapter.name.ToUpperInvariant()}";
            levelName.text = level.name.ToUpperInvariant();
            levelId.text = $"Level {level.id}";
            movesValue.text = board.MoveCount.ToString();
            parValue.text = level.par > 0 ? level.par.ToString() : "?";
            RefreshOutlook(board.MoveCount, level.par);
            RefreshLog();

            bool arithmetic = RowHint.ArithmeticHintsApply(rules);
            for (int r = 0; r < N; r++)
            {
                int now = board.GetRow(r);
                int target = BoardBits.GetRow(rules.Target, r);
                rowBits[r].text = RowString(rules, now, r);
                string label = RowHint.Label(rules, r);

                int chainTop = rules.ChainTopOf(r);
                bool chainBottom = chainTop >= 0 && chainTop != r;
                if (chainTop >= 0)
                {
                    // A carry chain reads as one 16-bit number: "now" on its top row, "→ target" below.
                    now = (board.GetRow(chainTop) << 8) | board.GetRow(chainTop + 1);
                    target = (BoardBits.GetRow(rules.Target, chainTop) << 8) | BoardBits.GetRow(rules.Target, chainTop + 1);
                }
                rowFrom[r].text = chainBottom ? "" : $"<b>{now}</b>";
                rowArrow[r].text = chainTop >= 0 ? "" : "→";
                rowTo[r].text = chainTop == r ? "" : chainBottom ? $"→ {target}" : target.ToString();

                string hint = chainBottom ? "" : RowHint.For(now, target, arithmetic).Replace("need ", "");
                bool ok = hint == RowHint.Ok;
                bool showHint = hint.Length > 0;
                rowTag[r].text = showHint ? hint : label;
                rowTag[r].color = ok ? Lcd.Light : showHint ? Lcd.LossText : Lcd.Text3;
                rowTag[r].fontStyle = showHint && !ok ? FontStyles.Bold : FontStyles.Normal;
                rowTag[r].fontSize = ok ? 11 : 13;
                rowTagBox[r].enabled = ok;
            }

            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                thumb[r * N + c].color = BoardBits.GetCell(rules.Target, r, c) ? Lcd.Ink : Lcd.Background;
            targetText.text = game.Chapter.description;
        }

        void RefreshOutcome(bool animate)
        {
            var board = game.Board;
            var rules = board.Rules;
            animate &= !game.Settings.reducedMotion;
            string par = game.Level.par > 0 ? game.Level.par.ToString() : "?";

            bool broken = board.IsRuleBroken;
            if (animate && board.Lost > shownLost) StartCoroutine(Bump(bucketCount.rectTransform));
            shownLost = board.Lost;
            bucketCount.text = board.Lost.ToString();
            bucketCount.color = board.Lost > 0 ? Lcd.Loss : Lcd.Ink;
            bucketFrame.color = broken ? Lcd.Loss : Lcd.Ink;
            bucketText.text = broken ? "Rule broken. Undo to continue." : RuleText(rules);
            bucketText.color = broken ? Lcd.Loss : Lcd.Text2;

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
        void RefreshOutlook(int moves, int par)
        {
            bool show = par > 0;
            outlook.enabled = show;
            int stars = game.LastResult?.Stars ?? Scoring.Stars(Mathf.Max(moves, par), par);
            for (int i = 0; i < outlookStars.Length; i++)
            {
                outlookStars[i].enabled = show && i < stars;
            }
            if (!show) return;
            outlook.text = game.LastResult != null ? $"solved for {stars}"
                : moves == 0 ? $"{par} moves for {Scoring.MaxStars}"
                : stars == Scoring.MaxStars ? $"still on track for {stars}"
                : $"on track for {stars}";
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
