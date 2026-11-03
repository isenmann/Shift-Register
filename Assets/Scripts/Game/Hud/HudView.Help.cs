using ShiftRegister.Game.Audio;
using ShiftRegister.Game.Progress;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The help panel over the level: the controls on the left, the goal and the mechanics on this
    /// board on the right. HELP, H or ? toggles it; Esc, Enter or a click outside closes it. It opens
    /// by itself the first time each chapter's first level is played.
    /// </summary>
    public sealed partial class HudView
    {
        const float HelpX = 190f, HelpY = 96f, HelpW = 900f, HelpH = 528f;
        const float HelpPad = 32f, HelpRightX = 456f, HelpListTop = 116f;

        RectTransform helpRoot, helpBoard;
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

            // The panel is the backdrop's sibling, so clicks on it do not reach the backdrop's handler.
            var frame = Ui.Rect(helpRoot, "Frame", 0, 0, 1280, 720);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            var panel = Ui.Rect(frame, "Panel", HelpX, HelpY, HelpW, HelpH);
            panel.gameObject.AddComponent<Image>().color = Color.clear;
            Ui.Box(panel, "Card", 0, 0, HelpW, HelpH, 12f, Lcd.Card);
            Ui.Frame(panel, "Border", 0, 0, HelpW, HelpH, 12f, 3f, Lcd.Ink);

            Ui.Text(panel, "Title", HelpPad, 24, 300, 40, displayBold, 28, Lcd.Ink, text: "HELP");
            HudButton.Create(panel, "CLOSE", HelpW - HelpPad - 110f, 24, 110, 40, display, 14, CloseHelp);

            Header(panel, "Controls Header", HelpPad, "CONTROLS");
            Header(panel, "Board Header", HelpRightX, "ON THIS BOARD");

            for (int i = 0; i < HelpText.Controls.Length; i++)
            {
                var e = HelpText.Controls[i];
                float y = HelpListTop + i * 52f;
                Ui.Text(panel, e.Title, HelpPad, y, 380, 22, mono, 15, Lcd.Ink, text: $"<b>{e.Title}</b>");
                Ui.Text(panel, e.Title + " Text", HelpPad, y + 22, 380, 20, mono, 13, Lcd.Text2, text: e.Text);
            }

            helpBoard = Ui.Rect(panel, "Board Entries", HelpRightX, HelpListTop, HelpW - HelpRightX - HelpPad, HelpH - HelpListTop - 56f);
            var layout = helpBoard.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Ui.Text(panel, "Close Hint", HelpPad, HelpH - 44f, HelpW - 2 * HelpPad, 20, mono, 13, Lcd.Text3,
                text: "H, Esc, Enter or a click outside closes this panel");

            helpRoot.gameObject.SetActive(false);
        }

        void Header(Transform panel, string name, float x, string text)
        {
            var t = Ui.Text(panel, name, x, 82, 380, 18, mono, 12, Lcd.Text3, text: text);
            t.characterSpacing = 8;
        }

        /// <summary>Fills the right column for the current level: chapter, goal, mechanics.</summary>
        void FillHelp()
        {
            for (int i = helpBoard.childCount - 1; i >= 0; i--) Destroy(helpBoard.GetChild(i).gameObject);

            var ch = game.Chapter;
            Column("Chapter", mono, 13, Lcd.Ink, $"<b>Chapter {game.ChapterIndex + 1} · {ch.name}</b>\n{ch.description}");
            foreach (var e in HelpText.Mechanics(game.Level))
            {
                Gap(12f);
                Column(e.Title, display, 13, Lcd.Ink, e.Title);
                Gap(2f);
                Column(e.Title + " Text", mono, 13, Lcd.Text2, e.Text);
            }
        }

        /// <summary>A wrapped text in the right column; the column's layout group sizes it.</summary>
        void Column(string name, TMP_FontAsset font, float size, Color32 color, string text)
        {
            var t = Ui.Text(helpBoard, name, 0, 0, helpBoard.sizeDelta.x, 20, font, size, color, TextAlignmentOptions.TopLeft, text);
            t.textWrappingMode = TextWrappingModes.Normal;
        }

        void Gap(float height)
        {
            var gap = new GameObject("Gap", typeof(RectTransform), typeof(LayoutElement));
            gap.transform.SetParent(helpBoard, false);
            gap.GetComponent<LayoutElement>().minHeight = height;
        }

        /// <summary>Portrait screens are narrower than the panel: shrink it to fit (until it gets its own layout).</summary>
        void ScaleHelp(float scale)
        {
            if (helpRoot == null) return;
            var panelFrame = helpRoot.Find("Frame");
            if (panelFrame != null) panelFrame.localScale = Vector3.one * scale;
        }

        void ToggleHelp()
        {
            if (HelpOpen) CloseHelp();
            else OpenHelp();
        }

        void OpenHelp()
        {
            if (helpRoot == null || game.Level == null) return;
            FillHelp();
            helpRoot.gameObject.SetActive(true);
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
