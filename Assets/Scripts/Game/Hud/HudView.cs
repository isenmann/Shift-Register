using System.Collections;
using System.Text;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The in-level HUD from the gameplay mockup: top bar (title, moves, undo, reset), row values
    /// "now → target", target thumbnail, bit bucket, key hint, and the solved / rule-broken banner.
    /// Built from code on a Screen Space canvas scaled like the camera (1280×720 reference, Expand).
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] BoardInput input;
        [Tooltip("Silkscreen Bold: level title and banner.")]
        [SerializeField] TMP_FontAsset displayBold;
        [Tooltip("Silkscreen Regular: card titles and buttons.")]
        [SerializeField] TMP_FontAsset display;
        [Tooltip("JetBrains Mono Regular (its weight table maps <b> to the Bold asset).")]
        [SerializeField] TMP_FontAsset mono;

        const int N = BoardBits.Size;

        TextMeshProUGUI title, chapter, moves;
        readonly TextMeshProUGUI[] rowBits = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowNow = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowTarget = new TextMeshProUGUI[N];
        readonly TextMeshProUGUI[] rowTag = new TextMeshProUGUI[N];
        readonly Image[] rowTagBox = new Image[N];
        readonly Image[] thumb = new Image[N * N];
        TextMeshProUGUI targetText, bucketCount, bucketText, cursorHint;
        Image bucketFrame, bannerBox;
        RectTransform banner;
        TextMeshProUGUI bannerTitle, bannerText;
        int shownCursor = -1;
        int shownLost;
        bool bannerShown;

        void Awake() => Build();

        void OnEnable()
        {
            if (game == null) return;
            game.Changed += OnChanged;
            if (game.BoardView != null) game.BoardView.Settled += OnSettled;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.Changed -= OnChanged;
            if (game.BoardView != null) game.BoardView.Settled -= OnSettled;
        }

        void Start()
        {
            if (game != null && game.Board != null) OnChanged();
        }

        /// <summary>Board info updates at once; the bucket and banner wait for the move animation to land.</summary>
        void OnChanged()
        {
            RefreshBoard();
            bool animating = game.BoardView != null && game.BoardView.IsAnimating;
            if (!animating) RefreshOutcome(false);
        }

        void OnSettled()
        {
            if (game.Board != null) RefreshOutcome(true);
        }

        void Update()
        {
            if (input == null) return;
            int cursor = input.CursorRow * N + input.CursorColumn;
            if (cursor == shownCursor) return;
            shownCursor = cursor;
            cursorHint.text = $"shift row {input.CursorRow + 1} or column {input.CursorColumn + 1}";
        }

        void Build()
        {
            var canvas = (RectTransform)transform;

            // Top bar spans the whole screen width; everything else sits in the centred 1280×720 frame.
            var bar = Ui.Fill(canvas, "Top Bar");
            bar.anchorMin = new Vector2(0f, 1f);
            bar.offsetMin = new Vector2(0f, -64f);
            var line = Ui.Fill(bar, "Border").gameObject.AddComponent<Image>();
            line.color = Lcd.Ink;
            line.raycastTarget = false;
            var lineRt = line.rectTransform;
            lineRt.anchorMax = new Vector2(1f, 0f);
            lineRt.offsetMax = new Vector2(0f, 3f);

            var left = Ui.Fill(bar, "Title Group");
            left.offsetMin = new Vector2(40f, 0f);
            var layout = left.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            title = Ui.Text(left, "Title", 0, 0, 100, 64, displayBold, 20, Lcd.Ink);
            chapter = Ui.Text(left, "Chapter", 0, 0, 100, 64, mono, 14, Lcd.Text2);

            var right = Ui.Fill(bar, "Actions");
            right.anchorMin = new Vector2(1f, 0f);
            right.offsetMin = new Vector2(-560f, 0f);
            right.offsetMax = new Vector2(-40f, 0f);
            HudButton.Create(right, "RESET", 420, 10, 100, 44, display, 14, () => game.ResetLevel());
            HudButton.Create(right, "UNDO", 304, 10, 88, 44, display, 14, () => game.Undo());
            moves = Ui.Text(right, "Moves", 0, 0, 276, 64, mono, 15, Lcd.Ink, TextAlignmentOptions.MidlineRight);

            var frame = Ui.Rect(canvas, "Frame", 0, 0, 1280, 720);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;

            BuildRows(frame);
            BuildTargetCard(frame);
            BuildBucket(frame);
            BuildKeyHint(frame);
            BuildBanner(frame);
        }

        void BuildRows(Transform frame)
        {
            const float x = 576f, top = 136f, pitch = 51f;
            Ui.Text(frame, "Rows Header", x, 104, 150, 16, mono, 12, Lcd.Text3, text: "ROW BITS").characterSpacing = 8;
            Ui.Text(frame, "Rows Header 2", x + 120, 104, 180, 16, mono, 12, Lcd.Text3, TextAlignmentOptions.MidlineRight, "NOW → TARGET").characterSpacing = 8;
            for (int r = 0; r < N; r++)
            {
                float y = top + r * pitch;
                rowBits[r] = Ui.Text(frame, $"Row {r + 1} Bits", x, y, 100, 46, mono, 15, Lcd.Text2);
                rowBits[r].characterSpacing = 6;
                rowNow[r] = Ui.Text(frame, $"Row {r + 1} Now", x + 96, y, 50, 46, mono, 15, Lcd.Ink, TextAlignmentOptions.MidlineRight);
                rowTarget[r] = Ui.Text(frame, $"Row {r + 1} Target", x + 150, y, 78, 46, mono, 15, Lcd.Text3, TextAlignmentOptions.MidlineRight);
                rowTagBox[r] = Ui.Box(frame, $"Row {r + 1} Tag Box", x + 234, y + 10, 66, 26, 5f, Lcd.Ink);
                rowTag[r] = Ui.Text(frame, $"Row {r + 1} Tag", x + 234, y + 10, 66, 26, mono, 12, Lcd.Light, TextAlignmentOptions.Center);
            }
        }

        void BuildTargetCard(Transform frame)
        {
            const float x = 924f, y = 96f, w = 316f, h = 313f, pad = 18f;
            Ui.Box(frame, "Target Card", x, y, w, h, 10f, Lcd.Cell);
            Ui.Text(frame, "Target Title", x + pad, y + pad, 200, 16, display, 14, Lcd.Ink, text: "TARGET");
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                thumb[r * N + c] = Ui.Box(frame, $"Thumb {r + 1},{c + 1}", x + pad + c * 23f, y + 44f + r * 23f, 20, 20, 3f, Lcd.Background);
            targetText = Ui.Text(frame, "Target Text", x + pad, y + 235, w - 2 * pad, 60, mono, 13, Lcd.Text2, TextAlignmentOptions.TopLeft);
            targetText.textWrappingMode = TextWrappingModes.Normal;
            targetText.lineSpacing = 8;
        }

        void BuildBucket(Transform frame)
        {
            const float x = 924f, y = 429f, w = 316f, h = 110f, pad = 18f;
            bucketFrame = Ui.Frame(frame, "Bucket Frame", x, y, w, h, 10f, 3f, Lcd.Ink, 8f);
            Ui.Text(frame, "Bucket Title", x + pad, y + pad, 200, 16, display, 14, Lcd.Ink, text: "BIT BUCKET");
            bucketCount = Ui.Text(frame, "Bucket Count", x + pad, y + pad, w - 2 * pad, 16, mono, 14, Lcd.Ink, TextAlignmentOptions.MidlineRight);
            bucketText = Ui.Text(frame, "Bucket Text", x + pad, y + 46, w - 2 * pad, 50, mono, 13, Lcd.Text2, TextAlignmentOptions.TopLeft);
            bucketText.textWrappingMode = TextWrappingModes.Normal;
            bucketText.lineSpacing = 8;
        }

        void BuildKeyHint(Transform frame)
        {
            const float x = 924f, y = 559f;
            KeyCap(frame, "⇧ Shift", x, y, 82);
            Ui.Text(frame, "Plus", x + 82, y, 28, 32, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, "+");
            KeyCap(frame, "← → ↑ ↓", x + 110, y, 112);
            cursorHint = Ui.Text(frame, "Cursor Hint", x, y + 40, 316, 20, mono, 14, Lcd.Text2);
            Ui.Text(frame, "Undo Hint", x, y + 64, 316, 20, mono, 13, Lcd.Text3, text: "Z undo · R reset");
        }

        void KeyCap(Transform frame, string text, float x, float y, float w)
        {
            Ui.Frame(frame, text, x, y, w, 32, 6f, 2f, Lcd.Ink);
            Ui.Text(frame, text + " Label", x, y, w, 32, mono, 14, Lcd.Ink, TextAlignmentOptions.Center, text);
        }

        void BuildBanner(Transform frame)
        {
            // Centred on the board (mockup board centre 305.5, 337.5).
            const float w = 340f, h = 110f, x = 305.5f - w / 2f, y = 337.5f - h / 2f;
            banner = Ui.Rect(frame, "Banner", x, y, w, h);
            banner.pivot = new Vector2(0.5f, 0.5f);
            banner.anchoredPosition = new Vector2(x + w / 2f, -(y + h / 2f));
            bannerBox = Ui.Box(banner, "Box", 0, 0, w, h, 10f, Lcd.Ink);
            bannerTitle = Ui.Text(banner, "Title", 16, 18, w - 32, 44, displayBold, 40, Lcd.Light, TextAlignmentOptions.Center);
            bannerTitle.enableAutoSizing = true;
            bannerTitle.fontSizeMin = 20;
            bannerTitle.fontSizeMax = 40;
            bannerText = Ui.Text(banner, "Text", 0, 68, w, 24, mono, 15, Lcd.Light, TextAlignmentOptions.Center);
            banner.gameObject.SetActive(false);
        }

        void RefreshBoard()
        {
            var board = game.Board;
            var level = game.Level;
            var rules = board.Rules;

            title.text = $"{level.id} · {level.name.ToUpperInvariant()}";
            chapter.text = $"Chapter {game.ChapterIndex + 1} · {game.Chapter.name}";
            string par = level.par > 0 ? level.par.ToString() : "?";
            moves.text = $"Moves <b>{board.MoveCount}</b> · par <b>{par}</b>";

            bool arithmetic = RowHint.ArithmeticHintsApply(rules);
            for (int r = 0; r < N; r++)
            {
                int now = board.GetRow(r);
                int target = BoardBits.GetRow(rules.Target, r);
                rowBits[r].text = RowString(rules, now, r);
                rowNow[r].text = $"<b>{now}</b>";
                rowTarget[r].text = $"→ {target}";
                string tag = RowHint.For(now, target, arithmetic);
                bool ok = tag == RowHint.Ok;
                rowTag[r].text = tag;
                rowTag[r].color = ok ? Lcd.Light : Lcd.LossText;
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
            string par = game.Level.par > 0 ? game.Level.par.ToString() : "?";

            bool broken = board.IsRuleBroken;
            if (animate && board.Lost > shownLost) StartCoroutine(Bump(bucketCount.rectTransform));
            shownLost = board.Lost;
            bucketCount.text = $"<b>{board.Lost}</b> lost";
            bucketCount.color = board.Lost > 0 ? Lcd.Loss : Lcd.Ink;
            bucketFrame.color = broken ? Lcd.Loss : Lcd.Ink;
            bucketText.text = broken ? "Rule broken. Undo to continue." : RuleText(rules);
            bucketText.color = broken ? Lcd.Loss : Lcd.Text2;

            bool solved = board.IsSolved;
            bool show = solved || broken;
            bannerBox.color = solved ? Lcd.Ink : Lcd.Loss;
            bannerTitle.text = solved ? "SOLVED" : "RULE BROKEN";
            bannerText.text = solved ? $"{board.MoveCount} moves · par {par}" : "Z or Backspace to undo";
            banner.gameObject.SetActive(show);
            if (show && !bannerShown && animate) StartCoroutine(PopIn(banner));
            else banner.localScale = Vector3.one;
            bannerShown = show;
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
            LossRule.AtMost when rules.LossCount == 0 => "RULE · LOSE 0 BITS\nAny bit off the edge breaks the level.",
            LossRule.AtMost => $"RULE · LOSE AT MOST {rules.LossCount}\nBits pushed off the edge land here for good.",
            LossRule.Exactly => $"GOAL · LOSE EXACTLY {rules.LossCount}\nDump exactly that many bits on the way.",
            _ => "Bits pushed off the edge land here and can't come back."
        };
    }
}
