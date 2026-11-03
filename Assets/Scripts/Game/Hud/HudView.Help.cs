using ShiftRegister.Game.Audio;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.Progress;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The help panel over the level: the controls (keys, or swipes and taps on phones and tablets),
    /// then the goal and the mechanics on this board. Landscape shows them side by side, portrait one
    /// under the other. HELP, H or ? toggles it; Esc, Enter or a click outside closes it. It opens by
    /// itself the first time each chapter's first level is played.
    /// </summary>
    public sealed partial class HudView
    {
        sealed class HelpPanel
        {
            public RectTransform Frame, Controls, Board;
            public TextMeshProUGUI CloseHint;
        }

        RectTransform helpRoot;
        HelpPanel helpWide, helpTall;
        HelpPanel HelpCur => ScreenLayout.Portrait ? helpTall : helpWide;
        string helpCheckedLevel;
        bool questionTyped;

        public bool HelpOpen => helpRoot != null && helpRoot.gameObject.activeSelf;

        void BuildHelp(RectTransform canvas)
        {
            helpRoot = Ui.Fill(canvas, "Help");

            // Backdrop: dims the level; a click on it closes the panel.
            var backdrop = Ui.Fill(helpRoot, "Backdrop");
            var dim = backdrop.gameObject.AddComponent<Image>();
            var bg = Lcd.Background;
            dim.color = new Color32(bg.r, bg.g, bg.b, 0xE6);
            backdrop.gameObject.AddComponent<UiHotspot>().Click = CloseHelp;

            // Panels are the backdrop's siblings, so clicks on them do not reach the backdrop's handler.
            helpWide = HelpPanelFrame("Landscape", 1280, 720, 190, 96, 900, 528);
            Header(helpWide.Frame, "Controls Header", 32, 82, "CONTROLS");
            Header(helpWide.Frame, "Board Header", 456, 82, "ON THIS BOARD");
            helpWide.Controls = Column(helpWide.Frame, "Controls", 32, 116, 380, 360);
            helpWide.Board = Column(helpWide.Frame, "Board Entries", 456, 116, 412, 356);
            helpWide.CloseHint = Ui.Text(helpWide.Frame, "Close Hint", 32, 528 - 44, 836, 20, mono, 13, Lcd.Text3);

            helpTall = HelpPanelFrame("Portrait", 720, 1280, 24, 96, 672, 1088);
            Header(helpTall.Frame, "Controls Header", 32, 82, "CONTROLS");
            helpTall.Controls = Column(helpTall.Frame, "Controls", 32, 112, 608, 300);
            Header(helpTall.Frame, "Board Header", 32, 432, "ON THIS BOARD");
            helpTall.Board = Column(helpTall.Frame, "Board Entries", 32, 462, 608, 560);
            helpTall.CloseHint = Ui.Text(helpTall.Frame, "Close Hint", 32, 1088 - 44, 608, 20, mono, 13, Lcd.Text3);

            helpRoot.gameObject.SetActive(false);
        }

        /// <summary>A centred reference frame holding a card panel with title and CLOSE; returns the panel.</summary>
        HelpPanel HelpPanelFrame(string name, float frameW, float frameH, float x, float y, float w, float h)
        {
            var frame = Ui.Rect(helpRoot, name, 0, 0, frameW, frameH);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            var panel = Ui.Rect(frame, "Panel", x, y, w, h);
            panel.gameObject.AddComponent<Image>().color = Color.clear;
            Ui.Box(panel, "Card", 0, 0, w, h, 12f, Lcd.Card);
            Ui.Frame(panel, "Border", 0, 0, w, h, 12f, 3f, Lcd.Ink);
            Ui.Text(panel, "Title", 32, 24, 300, 40, displayBold, 28, Lcd.Ink, text: "HELP");
            HudButton.Create(panel, "CLOSE", w - 32f - 110f, 24, 110, 40, display, 14, CloseHelp);
            return new HelpPanel { Frame = panel };
        }

        void Header(Transform panel, string name, float x, float y, string text)
        {
            var t = Ui.Text(panel, name, x, y, 380, 18, mono, 12, Lcd.Text3, text: text);
            t.characterSpacing = 8;
        }

        /// <summary>A column whose layout group stacks and sizes the wrapped texts put into it.</summary>
        static RectTransform Column(Transform panel, string name, float x, float y, float w, float h)
        {
            var column = Ui.Rect(panel, name, x, y, w, h);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return column;
        }

        /// <summary>Fills the visible panel for the current level and device: controls, chapter, goal, mechanics.</summary>
        void FillHelp()
        {
            bool tall = ScreenLayout.Portrait, mobile = DeviceMode.IsMobile;
            helpWide.Frame.parent.gameObject.SetActive(!tall);
            helpTall.Frame.parent.gameObject.SetActive(tall);
            var p = HelpCur;
            // Still drawn in desktop units: shrink the panel to fit the frame (phones use 412 short-side units).
            var panelSize = p.Frame.sizeDelta;
            float scale = Mathf.Min(1f, (ScreenLayout.FrameWidthPx - 24f) / panelSize.x, (ScreenLayout.FrameHeightPx - 24f) / panelSize.y);
            p.Frame.parent.localScale = Vector3.one * scale;
            Clear(p.Controls);
            Clear(p.Board);

            var controls = mobile ? HelpText.TouchControls : HelpText.Controls;
            for (int i = 0; i < controls.Length; i++)
            {
                if (i > 0) Gap(p.Controls, 8f);
                Wrapped(p.Controls, controls[i].Title, mono, 15, Lcd.Ink, $"<b>{controls[i].Title}</b>");
                Wrapped(p.Controls, controls[i].Title + " Text", mono, 13, Lcd.Text2, controls[i].Text);
            }

            var ch = game.Chapter;
            Wrapped(p.Board, "Chapter", mono, 13, Lcd.Ink, $"<b>Chapter {game.ChapterIndex + 1} · {ch.name}</b>\n{ch.description}");
            foreach (var e in HelpText.Mechanics(game.Level))
            {
                Gap(p.Board, 12f);
                Wrapped(p.Board, e.Title, display, 13, Lcd.Ink, e.Title);
                Gap(p.Board, 2f);
                Wrapped(p.Board, e.Title + " Text", mono, 13, Lcd.Text2, e.Text);
            }
            p.CloseHint.text = mobile ? "Tap CLOSE or outside the panel to go back" : "H, Esc, Enter or a click outside closes this panel";
        }

        static void Clear(Transform column)
        {
            for (int i = column.childCount - 1; i >= 0; i--) Destroy(column.GetChild(i).gameObject);
        }

        /// <summary>A wrapped text in a column; the column's layout group sizes it.</summary>
        static void Wrapped(RectTransform column, string name, TMP_FontAsset font, float size, Color32 color, string text)
        {
            var t = Ui.Text(column, name, 0, 0, column.sizeDelta.x, 20, font, size, color, TextAlignmentOptions.TopLeft, text);
            t.textWrappingMode = TextWrappingModes.Normal;
        }

        static void Gap(Transform column, float height)
        {
            var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(column, false);
            gap.GetComponent<LayoutElement>().minHeight = height;
        }

        /// <summary>After a rotation an open panel switches to the other arrangement.</summary>
        void RefreshHelpOrientation()
        {
            if (HelpOpen) FillHelp();
        }

        void ToggleHelp()
        {
            if (HelpOpen) CloseHelp();
            else OpenHelp();
        }

        void OpenHelp()
        {
            if (helpRoot == null || game.Level == null) return;
            helpRoot.gameObject.SetActive(true);
            FillHelp();
            if (input != null) input.enabled = false;
        }

        public void CloseHelp()
        {
            if (!HelpOpen) return;
            helpRoot.gameObject.SetActive(false);
            if (input != null && flow != null && flow.Current == GameScreen.Playing) input.enabled = true;
        }

        /// <summary>Each chapter's first level opens the help once (saved in the progress).</summary>
        void CheckFirstHelp()
        {
            if (game.Level == null || game.Level.id == helpCheckedLevel) return;
            helpCheckedLevel = game.Level.id;
            if (HelpOpen) FillHelp();
            if (game.LevelIndex != 0 || game.IsPreviewing || game.Progress == null) return;
            if (!game.Progress.MarkHelpSeen(game.Level.id)) return;
            ProgressStore.Save(game.Progress);
            OpenHelp();
        }

        void OnTextInput(char c)
        {
            // '?' sits on different keys per keyboard layout, so it is read as typed text.
            if (c == '?') questionTyped = true;
        }

        void UpdateHelp()
        {
            var kb = Keyboard.current;
            bool question = questionTyped;
            questionTyped = false;
            if (kb == null) return;

            if (kb.hKey.wasPressedThisFrame || question)
            {
                ToggleHelp();
                Sfx.Play(Sound.Click);
            }
            else if (HelpOpen && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            {
                CloseHelp();
                Sfx.Play(Sound.Click);
            }
        }
    }
}
