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
            /// <summary>Phone panels use 14 px body text (desktop 13).</summary>
            public float TextSize = 13f;
        }

        RectTransform helpRoot;
        HelpPanel helpWide, helpPhoneWide, helpPhoneTall;
        HelpPanel HelpCur => !ScreenLayout.Phone ? helpWide : ScreenLayout.Portrait ? helpPhoneTall : helpPhoneWide;
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

            // Phones, in phone units (412 short side): side by side when held sideways, stacked when upright.
            helpPhoneWide = HelpPanelFrame("Phone Landscape", 915, 412, 16, 12, 883, 388, phone: true);
            Header(helpPhoneWide.Frame, "Controls Header", 16, 60, "CONTROLS", 13f);
            Header(helpPhoneWide.Frame, "Board Header", 440, 60, "ON THIS BOARD", 13f);
            helpPhoneWide.Controls = Column(helpPhoneWide.Frame, "Controls", 16, 84, 400, 270);
            helpPhoneWide.Board = Column(helpPhoneWide.Frame, "Board Entries", 440, 84, 427, 270);
            helpPhoneWide.CloseHint = Ui.Text(helpPhoneWide.Frame, "Close Hint", 16, 388 - 28, 851, 18, mono, 13, Lcd.Text3);

            helpPhoneTall = HelpPanelFrame("Phone Portrait", 412, 915, 8, 40, 396, 835, phone: true);
            Header(helpPhoneTall.Frame, "Controls Header", 16, 66, "CONTROLS", 13f);
            helpPhoneTall.Controls = Column(helpPhoneTall.Frame, "Controls", 16, 90, 364, 250);
            Header(helpPhoneTall.Frame, "Board Header", 16, 352, "ON THIS BOARD", 13f);
            helpPhoneTall.Board = Column(helpPhoneTall.Frame, "Board Entries", 16, 376, 364, 420);
            helpPhoneTall.CloseHint = Ui.Text(helpPhoneTall.Frame, "Close Hint", 16, 835 - 30, 364, 18, mono, 13, Lcd.Text3);

            helpRoot.gameObject.SetActive(false);
        }

        /// <summary>A centred reference frame holding a card panel with title and CLOSE; returns the panel.</summary>
        HelpPanel HelpPanelFrame(string name, float frameW, float frameH, float x, float y, float w, float h, bool phone = false)
        {
            var frame = Ui.Rect(helpRoot, name, 0, 0, frameW, frameH);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;
            var panel = Ui.Rect(frame, "Panel", x, y, w, h);
            panel.gameObject.AddComponent<Image>().color = Color.clear;
            Ui.Box(panel, "Card", 0, 0, w, h, 12f, Lcd.Card);
            Ui.Frame(panel, "Border", 0, 0, w, h, 12f, 3f, Lcd.Ink);
            float pad = phone ? 16f : 32f, top = phone ? 12f : 24f;
            Ui.Text(panel, "Title", pad, top, 300, 40, displayBold, phone ? 24 : 28, Lcd.Ink, text: "HELP");
            HudButton.Create(panel, "CLOSE", w - pad - (phone ? 96f : 110f), top, phone ? 96 : 110, phone ? 44 : 40, display, 14, CloseHelp);
            return new HelpPanel { Frame = panel, TextSize = phone ? 14f : 13f };
        }

        void Header(Transform panel, string name, float x, float y, string text, float size = 12f)
        {
            var t = Ui.Text(panel, name, x, y, 380, 18, mono, size, Lcd.Text3, text: text);
            t.characterSpacing = size > 12f ? 4 : 8;
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
            bool mobile = DeviceMode.IsMobile;
            var p = HelpCur;
            foreach (var panel in new[] { helpWide, helpPhoneWide, helpPhoneTall })
                panel.Frame.parent.gameObject.SetActive(panel == p);
            Clear(p.Controls);
            Clear(p.Board);

            var controls = mobile ? HelpText.TouchControls : HelpText.Controls;
            for (int i = 0; i < controls.Length; i++)
            {
                if (i > 0) Gap(p.Controls, 8f);
                Wrapped(p.Controls, controls[i].Title, mono, 15, Lcd.Ink, $"<b>{controls[i].Title}</b>");
                Wrapped(p.Controls, controls[i].Title + " Text", mono, p.TextSize, Lcd.Text2, controls[i].Text);
            }

            var ch = game.Chapter;
            Wrapped(p.Board, "Chapter", mono, p.TextSize, Lcd.Ink, game.IsRandom
                ? "<b>Random level</b>\nMade just now. It earns no stars and unlocks nothing."
                : game.IsCustom
                ? "<b>My levels</b>\nA level from the editor. It earns no stars and unlocks nothing."
                : $"<b>Chapter {game.ChapterIndex + 1} · {ch.name}</b>\n{ch.description}");
            foreach (var e in HelpText.Mechanics(game.Level))
            {
                Gap(p.Board, 12f);
                Wrapped(p.Board, e.Title, display, p.TextSize, Lcd.Ink, e.Title);
                Gap(p.Board, 2f);
                Wrapped(p.Board, e.Title + " Text", mono, p.TextSize, Lcd.Text2, e.Text);
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
