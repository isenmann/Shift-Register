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

        enum OptionsMode { Desktop, PhoneLandscape, PhonePortrait }

        /// <summary>Sizes of one options arrangement: rows, labels, volume controls and switches.</summary>
        sealed class OptionsSpec
        {
            public float X, Top, W, H, Pitch;
            public float LabelSize, TextSize, TextW, TextH;
            public float MinusX, ControlsY, Button, CellW, CellGap, CellsGap;
            public float ToggleW, ToggleH;
        }

        static OptionsSpec Spec(OptionsMode mode) => mode switch
        {
            OptionsMode.PhonePortrait => new OptionsSpec
            {
                X = 16, Top = 96, W = 380, H = 120, Pitch = 136, LabelSize = 18, TextSize = 14, TextW = 230, TextH = 60,
                MinusX = 12, ControlsY = 66, Button = 44, CellW = 22, CellGap = 4, CellsGap = 8, ToggleW = 110, ToggleH = 48
            },
            OptionsMode.PhoneLandscape => new OptionsSpec
            {
                X = (915 - 560) / 2f, Top = 76, W = 560, H = 86, Pitch = 98, LabelSize = 18, TextSize = 14, TextW = 300, TextH = 40,
                MinusX = 230, ControlsY = 20, Button = 44, CellW = 18, CellGap = 4, CellsGap = 8, ToggleW = 110, ToggleH = 48
            },
            _ => new OptionsSpec
            {
                X = (1280 - 760) / 2f, Top = 160, W = 760, H = 76, Pitch = 92, LabelSize = 18, TextSize = 13, TextW = 560, TextH = 20,
                MinusX = 330, ControlsY = 16, Button = 44, CellW = 24, CellGap = 6, CellsGap = 14, ToggleW = 120, ToggleH = 44
            }
        };

        void BuildOptions(Screens s, OptionsMode mode)
        {
            var root = s.Options;
            var spec = Spec(mode);
            var o = s.Opt = new OptionsUi { X = spec.X, Top = spec.Top, W = spec.W, H = spec.H, Pitch = spec.Pitch };
            bool portrait = mode == OptionsMode.PhonePortrait;

            // Portrait: header and rows hang from the top; landscape: header ends move out, rows stay centred.
            var top = portrait ? Outer(s, root, "Top", 0f, 1f) : null;
            var left = portrait ? null : Outer(s, root, "Left", -1f, 0f);
            var right = portrait ? null : Outer(s, root, "Right", 1f, 0f);
            var rows = portrait ? top : root;
            switch (mode)
            {
                case OptionsMode.PhonePortrait:
                    Ui.Text(top, "Heading", 16, 24, 270, 40, displayBold, 28, Lcd.Ink, text: "OPTIONS");
                    HudButton.Create(top, "BACK", 300, 22, 96, 48, display, 15, () => flow.Show(GameScreen.Title));
                    break;
                case OptionsMode.PhoneLandscape:
                    Ui.Text(left, "Heading", 24, 16, 300, 40, displayBold, 26, Lcd.Ink, text: "OPTIONS");
                    HudButton.Create(right, "BACK", 795, 14, 96, 44, display, 15, () => flow.Show(GameScreen.Title));
                    break;
                default:
                    Ui.Text(left, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "OPTIONS");
                    HudButton.Create(right, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));
                    break;
            }

            // Volume: − [10 cells] +; on an upright phone on its own line under the label.
            var volume = OptionRowRoot(rows, o, spec, OptionRow.Volume, "VOLUME", out o.VolumeText);
            HudButton.Create(volume, "-", spec.MinusX, spec.ControlsY, spec.Button, 44, display, 18, () => StepVolume(-1));
            float cellsX = spec.MinusX + spec.Button + spec.CellsGap;
            for (int i = 0; i < o.VolumeCells.Length; i++)
            {
                float cx = cellsX + i * (spec.CellW + spec.CellGap);
                o.VolumeCells[i] = Ui.Box(volume, $"Volume {i + 1}", cx, spec.ControlsY + 6, spec.CellW, 32, 4f, Lcd.Cell);
                int step = i + 1;
                UiHotspot.Create(volume, $"Volume {i + 1} Hotspot", cx - spec.CellGap / 2f, spec.ControlsY, spec.CellW + spec.CellGap, 44).Click =
                    () => SetVolume(game.Settings.volume == step && step == 1 ? 0 : step);
            }
            float plusX = cellsX + o.VolumeCells.Length * (spec.CellW + spec.CellGap) - spec.CellGap + spec.CellsGap;
            HudButton.Create(volume, "+", plusX, spec.ControlsY, spec.Button, 44, display, 18, () => StepVolume(1));

            // Switches sit at the row's right edge, vertically centred.
            float toggleX = o.W - spec.ToggleW - 16f, toggleY = (o.H - spec.ToggleH) / 2f;
            var sound = OptionRowRoot(rows, o, spec, OptionRow.Sound, "SOUND", out s.SoundText);
            o.Sound = HudButton.Create(sound, "ON", toggleX, toggleY, spec.ToggleW, spec.ToggleH, display, 16, () => Toggle(OptionRow.Sound));
            var motion = OptionRowRoot(rows, o, spec, OptionRow.Motion, "REDUCED MOTION", out var motionText);
            motionText.text = "Moves snap into place: no sliding, flying or hopping.";
            o.Motion = HudButton.Create(motion, "OFF", toggleX, toggleY, spec.ToggleW, spec.ToggleH, display, 16, () => Toggle(OptionRow.Motion));

            o.Focus = Ui.Frame(rows, "Focus", o.X, o.Top, o.W, o.H, 10f, 3f, Lcd.Ink);
            if (mode == OptionsMode.Desktop)
                s.OptionsKeys = Ui.Text(left, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3, text: "Arrows choose and change · Enter switch · M mute · Esc back");
        }

        void OnDestroy()
        {
            if (game != null) game.SettingsChanged -= OnSettingsChanged;
        }

        /// <summary>A card row that focuses on hover; its controls are children so hovering them keeps the focus.</summary>
        RectTransform OptionRowRoot(Transform root, OptionsUi o, OptionsSpec spec, OptionRow row, string label, out TextMeshProUGUI text)
        {
            float y = o.Top + (int)row * o.Pitch;
            var hot = UiHotspot.Create(root, label, o.X, y, o.W, o.H);
            hot.Enter = () => SetOptionFocus(row);
            var rt = (RectTransform)hot.transform;
            Ui.Box(rt, "Card", 0, 0, o.W, o.H, 10f, Lcd.Card);
            float pad = spec.H > 80 ? 16f : 24f;
            Ui.Text(rt, "Label", pad, 12, 300, 24, display, spec.LabelSize, Lcd.Ink, text: label);
            text = Ui.Text(rt, "Text", pad, 40, spec.TextW, spec.TextH, mono, spec.TextSize, Lcd.Text2, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
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
