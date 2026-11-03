using ShiftRegister.Game.Audio;
using ShiftRegister.Game.Settings;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The options screen: volume, sound on/off and reduced motion. Arrows up/down pick a row,
    /// left/right change it, Enter switches it, Esc goes back. Landscape rows are one line each;
    /// portrait rows put the volume controls under their label.
    /// </summary>
    public sealed partial class MenuView
    {
        enum OptionRow { Volume, Sound, Motion }

        /// <summary>One arrangement of the options screen.</summary>
        sealed class OptionsUi
        {
            public float X, Top, W, H, Pitch;
            public readonly Image[] VolumeCells = new Image[GameSettings.VolumeSteps];
            public TextMeshProUGUI VolumeText;
            public HudButton Sound, Motion;
            public Image Focus;
        }

        OptionRow optionFocus;

        void BuildOptions(Screens s, bool portrait)
        {
            var root = s.Options;
            // Portrait: header and rows hang from the top; landscape: header ends move out, rows stay centred.
            var top = portrait ? Outer(s, root, "Top", 0f, 1f) : null;
            var bottom = portrait ? Outer(s, root, "Bottom", 0f, -1f) : null;
            var left = portrait ? null : Outer(s, root, "Left", -1f, 0f);
            var right = portrait ? null : Outer(s, root, "Right", 1f, 0f);
            var rows = portrait ? top : root;
            var o = s.Opt = portrait
                ? new OptionsUi { X = 24f, Top = 140f, W = 672f, H = 132f, Pitch = 152f }
                : new OptionsUi { X = (1280f - 760f) / 2f, Top = 160f, W = 760f, H = 76f, Pitch = 92f };

            if (portrait)
            {
                Ui.Text(top, "Heading", 24, 32, 400, 52, displayBold, 34, Lcd.Ink, text: "OPTIONS");
                HudButton.Create(top, "BACK", 566, 32, 130, 52, display, 15, () => flow.Show(GameScreen.Title));
            }
            else
            {
                Ui.Text(left, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "OPTIONS");
                HudButton.Create(right, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));
            }

            // Volume: − [10 cells] +; in portrait on its own line under the label.
            var volume = OptionRowRoot(rows, o, OptionRow.Volume, "VOLUME", out o.VolumeText, portrait);
            float controlsY = portrait ? 78f : 16f, minusX = portrait ? 24f : 330f;
            float cellW = portrait ? 36f : 24f, cellGap = portrait ? 8f : 6f, button = portrait ? 52f : 44f;
            HudButton.Create(volume, "-", minusX, controlsY, button, 44, display, 18, () => StepVolume(-1));
            float cellsX = minusX + button + (portrait ? 16f : 14f);
            for (int i = 0; i < o.VolumeCells.Length; i++)
            {
                float cx = cellsX + i * (cellW + cellGap);
                o.VolumeCells[i] = Ui.Box(volume, $"Volume {i + 1}", cx, controlsY + 6, cellW, 32, 4f, Lcd.Cell);
                int step = i + 1;
                UiHotspot.Create(volume, $"Volume {i + 1} Hotspot", cx - cellGap / 2f, controlsY, cellW + cellGap, 44).Click =
                    () => SetVolume(game.Settings.volume == step && step == 1 ? 0 : step);
            }
            float plusX = cellsX + o.VolumeCells.Length * (cellW + cellGap) + 8f;
            HudButton.Create(volume, "+", plusX, controlsY, button, 44, display, 18, () => StepVolume(1));

            // Switches sit at the row's right edge, vertically centred.
            float toggleW = portrait ? 144f : 120f, toggleH = portrait ? 52f : 44f;
            float toggleX = o.W - toggleW - 24f, toggleY = (o.H - toggleH) / 2f;
            var sound = OptionRowRoot(rows, o, OptionRow.Sound, "SOUND", out s.SoundText, portrait);
            o.Sound = HudButton.Create(sound, "ON", toggleX, toggleY, toggleW, toggleH, display, 16, () => Toggle(OptionRow.Sound));
            var motion = OptionRowRoot(rows, o, OptionRow.Motion, "REDUCED MOTION", out var motionText, portrait);
            motionText.text = "Moves snap into place: no sliding, flying or hopping.";
            o.Motion = HudButton.Create(motion, "OFF", toggleX, toggleY, toggleW, toggleH, display, 16, () => Toggle(OptionRow.Motion));

            o.Focus = Ui.Frame(rows, "Focus", o.X, o.Top, o.W, o.H, 10f, 3f, Lcd.Ink);
            s.OptionsKeys = portrait
                ? Ui.Text(bottom, "Keys", 24, 1232, 672, 20, mono, 13, Lcd.Text3, TextAlignmentOptions.Center, "Arrows choose and change · Enter switch · Esc back")
                : Ui.Text(left, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3, text: "Arrows choose and change · Enter switch · M mute · Esc back");
        }

        void OnDestroy()
        {
            if (game != null) game.SettingsChanged -= OnSettingsChanged;
        }

        /// <summary>A card row that focuses on hover; its controls are children so hovering them keeps the focus.</summary>
        RectTransform OptionRowRoot(Transform root, OptionsUi o, OptionRow row, string label, out TextMeshProUGUI text, bool portrait)
        {
            float y = o.Top + (int)row * o.Pitch;
            var hot = UiHotspot.Create(root, label, o.X, y, o.W, o.H);
            hot.Enter = () => SetOptionFocus(row);
            var rt = (RectTransform)hot.transform;
            Ui.Box(rt, "Card", 0, 0, o.W, o.H, 10f, Lcd.Card);
            if (portrait)
            {
                Ui.Text(rt, "Label", 24, 16, 440, 26, display, 20, Lcd.Ink, text: label);
                text = Ui.Text(rt, "Text", 24, 46, 440, 40, mono, 13, Lcd.Text2, TextAlignmentOptions.TopLeft);
                text.textWrappingMode = TextWrappingModes.Normal;
            }
            else
            {
                Ui.Text(rt, "Label", 24, 14, 300, 24, display, 18, Lcd.Ink, text: label);
                text = Ui.Text(rt, "Text", 24, 42, 560, 20, mono, 13, Lcd.Text2);
            }
            return rt;
        }

        void UpdateOptions(Keyboard kb)
        {
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
                SetOptionFocus((OptionRow)Mathf.Max(0, (int)optionFocus - 1));
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
                SetOptionFocus((OptionRow)Mathf.Min((int)OptionRow.Motion, (int)optionFocus + 1));
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Change(-1);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Change(1);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (optionFocus != OptionRow.Volume)
                {
                    Toggle(optionFocus);
                    Sfx.Play(Sound.Click);
                }
            }
        }

        /// <summary>Left/right: volume steps; on a switch, right turns it on and left turns it off.</summary>
        void Change(int delta)
        {
            if (optionFocus == OptionRow.Volume)
            {
                StepVolume(delta);
                return;
            }
            bool on = delta > 0;
            if (IsOn(optionFocus) == on)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            Toggle(optionFocus);
            Sfx.Play(Sound.Click);
        }

        bool IsOn(OptionRow row) => row == OptionRow.Sound ? !game.Settings.muted : game.Settings.reducedMotion;

        void Toggle(OptionRow row)
        {
            SetOptionFocus(row);
            if (row == OptionRow.Sound) game.ChangeSettings(s => s.muted = !s.muted);
            else if (row == OptionRow.Motion) game.ChangeSettings(s => s.reducedMotion = !s.reducedMotion);
        }

        void StepVolume(int delta) => SetVolume(game.Settings.volume + delta);

        void SetVolume(int volume)
        {
            SetOptionFocus(OptionRow.Volume);
            volume = Mathf.Clamp(volume, 0, GameSettings.VolumeSteps);
            if (volume == game.Settings.volume)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            // Changing the volume also unmutes, so the change is heard.
            game.ChangeSettings(s =>
            {
                s.volume = volume;
                s.muted = false;
            });
            Sfx.Play(Sound.Click);
        }

        void OnSettingsChanged()
        {
            if (built && shown == GameScreen.Options) RefreshOptions(false);
        }

        void RefreshOptions(bool resetFocus)
        {
            var settings = game.Settings;
            foreach (var s in Both)
            {
                var o = s.Opt;
                for (int i = 0; i < o.VolumeCells.Length; i++)
                    o.VolumeCells[i].color = i < settings.volume ? (settings.muted ? Lcd.Text3 : Lcd.Ink) : Lcd.Cell;
                o.VolumeText.text = settings.muted ? $"{settings.volume * 10}% · muted" : $"{settings.volume * 10}%";
                o.Sound.Text = settings.muted ? "OFF" : "ON";
                o.Motion.Text = settings.reducedMotion ? "ON" : "OFF";
                if (resetFocus) o.Focus.enabled = false; // no focus tick when the screen opens
            }
            if (resetFocus) SetOptionFocus(OptionRow.Volume);
        }

        void SetOptionFocus(OptionRow row)
        {
            if (Cur.Opt.Focus.enabled && row != optionFocus) Sfx.Play(Sound.Tick);
            optionFocus = row;
            foreach (var s in Both)
            {
                var o = s.Opt;
                o.Focus.rectTransform.anchoredPosition = new Vector2(o.X + o.W / 2f, -(o.Top + (int)row * o.Pitch + o.H / 2f));
                o.Focus.enabled = true;
            }
        }
    }
}
