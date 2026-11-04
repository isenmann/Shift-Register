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
    /// The options screen: volume, sound on/off, reduced motion and bit life. Arrows up/down pick a row,
    /// left/right change it, Enter switches it, Esc goes back. Landscape rows are one line each;
    /// portrait rows put the volume controls under their label.
    /// </summary>
    public sealed partial class MenuView
    {
        enum OptionRow { Volume, Sound, Motion, Life }

        /// <summary>One arrangement of the options screen.</summary>
        sealed class OptionsUi
        {
            public float X, Top, Width, Height, Pitch;
            public readonly Image[] VolumeCells = new Image[GameSettings.VolumeSteps];
            public TextMeshProUGUI VolumeText;
            public HudButton Sound, Motion, Life;
            public Image Focus;
        }

        enum OptionsMode { Desktop, PhoneLandscape, PhonePortrait }

        /// <summary>Sizes of one options arrangement: rows, labels, volume controls and switches.</summary>
        sealed class OptionsSpec
        {
            public float X, Top, Width, Height, Pitch;
            public float LabelSize, TextSize, TextWidth, TextHeight;
            public float MinusX, ControlsY, Button, CellWidth, CellGap, CellsGap;
            public float ToggleWidth, ToggleHeight;
        }

        OptionRow optionFocus;

        static OptionsSpec Spec(OptionsMode mode) => mode switch
        {
            OptionsMode.PhonePortrait => new OptionsSpec
            {
                X = 16,
                Top = 96,
                Width = 380,
                Height = 120,
                Pitch = 136,
                LabelSize = 18,
                TextSize = 14,
                TextWidth = 230,
                TextHeight = 60,
                MinusX = 12,
                ControlsY = 66,
                Button = 44,
                CellWidth = 22,
                CellGap = 4,
                CellsGap = 8,
                ToggleWidth = 110,
                ToggleHeight = 48
            },
            OptionsMode.PhoneLandscape => new OptionsSpec
            {
                X = (915 - 560) / 2f,
                Top = 62,
                Width = 560,
                Height = 78,
                Pitch = 85,
                LabelSize = 18,
                TextSize = 14,
                TextWidth = 400,
                TextHeight = 24,
                MinusX = 230,
                ControlsY = 20,
                Button = 44,
                CellWidth = 18,
                CellGap = 4,
                CellsGap = 8,
                ToggleWidth = 110,
                ToggleHeight = 48
            },
            _ => new OptionsSpec
            {
                X = (1280 - 760) / 2f,
                Top = 160,
                Width = 760,
                Height = 76,
                Pitch = 92,
                LabelSize = 18,
                TextSize = 13,
                TextWidth = 560,
                TextHeight = 20,
                MinusX = 330,
                ControlsY = 16,
                Button = 44,
                CellWidth = 24,
                CellGap = 6,
                CellsGap = 14,
                ToggleWidth = 120,
                ToggleHeight = 44
            }
        };

        void BuildOptions(Screens screens, OptionsMode mode)
        {
            var root = screens.Options;
            var spec = Spec(mode);
            var options = screens.OptionsUi = new OptionsUi
            {
                X = spec.X,
                Top = spec.Top,
                Width = spec.Width,
                Height = spec.Height,
                Pitch = spec.Pitch
            };
            bool portrait = mode == OptionsMode.PhonePortrait;

            // Portrait: header and rows hang from the top; landscape: header ends move out, rows stay centred.
            var top = portrait ? Outer(screens, root, "Top", 0f, 1f) : null;
            var left = portrait ? null : Outer(screens, root, "Left", -1f, 0f);
            var right = portrait ? null : Outer(screens, root, "Right", 1f, 0f);
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
            var volume = OptionRowRoot(rows, options, spec, OptionRow.Volume, "VOLUME", out options.VolumeText);
            HudButton.Create(volume, "-", spec.MinusX, spec.ControlsY, spec.Button, 44, display, 18,
                () => StepVolume(-1));
            float cellsX = spec.MinusX + spec.Button + spec.CellsGap;
            for (int i = 0; i < options.VolumeCells.Length; i++)
            {
                float cellX = cellsX + i * (spec.CellWidth + spec.CellGap);
                options.VolumeCells[i] = Ui.Box(volume, $"Volume {i + 1}", cellX, spec.ControlsY + 6, spec.CellWidth,
                    32, 4f, Lcd.Cell);
                int step = i + 1;
                UiHotspot.Create(volume, $"Volume {i + 1} Hotspot", cellX - spec.CellGap / 2f, spec.ControlsY,
                    spec.CellWidth + spec.CellGap, 44).Click =
                    () => SetVolume(game.Settings.volume == step && step == 1 ? 0 : step);
            }

            float plusX = cellsX + options.VolumeCells.Length * (spec.CellWidth + spec.CellGap) - spec.CellGap
                + spec.CellsGap;
            HudButton.Create(volume, "+", plusX, spec.ControlsY, spec.Button, 44, display, 18, () => StepVolume(1));

            // Switches sit at the row's right edge, vertically centred.
            float toggleX = options.Width - spec.ToggleWidth - 16f, toggleY = (options.Height - spec.ToggleHeight) / 2f;
            var sound = OptionRowRoot(rows, options, spec, OptionRow.Sound, "SOUND", out screens.SoundText);
            options.Sound = HudButton.Create(sound, "ON", toggleX, toggleY, spec.ToggleWidth, spec.ToggleHeight,
                display, 16, () => Toggle(OptionRow.Sound));
            var motion = OptionRowRoot(rows, options, spec, OptionRow.Motion, "REDUCED MOTION", out var motionText);
            motionText.text = "Moves snap into place. Only eyes move.";
            options.Motion = HudButton.Create(motion, "OFF", toggleX, toggleY, spec.ToggleWidth, spec.ToggleHeight,
                display, 16, () => Toggle(OptionRow.Motion));
            var life = OptionRowRoot(rows, options, spec, OptionRow.Life, "BIT LIFE", out var lifeText);
            lifeText.text = "Bits blink, nap and react while you think.";
            options.Life = HudButton.Create(life, "ON", toggleX, toggleY, spec.ToggleWidth, spec.ToggleHeight,
                display, 16, () => Toggle(OptionRow.Life));

            options.Focus = Ui.Frame(rows, "Focus", options.X, options.Top, options.Width, options.Height, 10f, 3f,
                Lcd.Ink);
            if (mode == OptionsMode.Desktop)
            {
                screens.OptionsKeys = Ui.Text(left, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3,
                    text: "Arrows choose and change · Enter switch · M mute · Esc back");
            }
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.SettingsChanged -= OnSettingsChanged;
            }
        }

        /// <summary>
        /// A card row that focuses on hover; its controls are children so hovering them keeps the focus.
        /// </summary>
        RectTransform OptionRowRoot(Transform root, OptionsUi options, OptionsSpec spec, OptionRow row, string label,
            out TextMeshProUGUI text)
        {
            float y = options.Top + (int)row * options.Pitch;
            var hot = UiHotspot.Create(root, label, options.X, y, options.Width, options.Height);
            hot.Enter = () => SetOptionFocus(row);
            var rectTransform = (RectTransform)hot.transform;
            Ui.Box(rectTransform, "Card", 0, 0, options.Width, options.Height, 10f, Lcd.Card);
            float pad = spec.Height > 80 ? 16f : 24f;
            Ui.Text(rectTransform, "Label", pad, 12, 300, 24, display, spec.LabelSize, Lcd.Ink, text: label);
            text = Ui.Text(rectTransform, "Text", pad, 40, spec.TextWidth, spec.TextHeight, mono, spec.TextSize,
                Lcd.Text2, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            return rectTransform;
        }

        void UpdateOptions(Keyboard keyboard)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
            {
                SetOptionFocus((OptionRow)Mathf.Max(0, (int)optionFocus - 1));
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                SetOptionFocus((OptionRow)Mathf.Min((int)OptionRow.Life, (int)optionFocus + 1));
            }
            else if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                Change(-1);
            }
            else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                Change(1);
            }
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame)
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

        bool IsOn(OptionRow row) => row switch
        {
            OptionRow.Sound => !game.Settings.muted,
            OptionRow.Life => game.Settings.bitLife,
            _ => game.Settings.reducedMotion
        };

        void Toggle(OptionRow row)
        {
            SetOptionFocus(row);
            if (row == OptionRow.Sound)
            {
                game.ChangeSettings(settings => settings.muted = !settings.muted);
            }
            else if (row == OptionRow.Motion)
            {
                game.ChangeSettings(settings => settings.reducedMotion = !settings.reducedMotion);
            }
            else if (row == OptionRow.Life)
            {
                game.ChangeSettings(settings => settings.bitLife = !settings.bitLife);
            }
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
            game.ChangeSettings(settings =>
            {
                settings.volume = volume;
                settings.muted = false;
            });
            Sfx.Play(Sound.Click);
        }

        void OnSettingsChanged()
        {
            if (built && shown == GameScreen.Options)
            {
                RefreshOptions(false);
            }
        }

        void RefreshOptions(bool resetFocus)
        {
            var settings = game.Settings;
            foreach (var screens in AllScreens)
            {
                var options = screens.OptionsUi;
                for (int i = 0; i < options.VolumeCells.Length; i++)
                {
                    options.VolumeCells[i].color = i < settings.volume ? (settings.muted ? Lcd.Text3 : Lcd.Ink)
                        : Lcd.Cell;
                }

                options.VolumeText.text = settings.muted ? $"{settings.volume * 10}% · muted"
                    : $"{settings.volume * 10}%";
                options.Sound.Text = settings.muted ? "OFF" : "ON";
                options.Motion.Text = settings.reducedMotion ? "ON" : "OFF";
                options.Life.Text = settings.bitLife ? "ON" : "OFF";
                if (resetFocus)
                {
                    options.Focus.enabled = false; // no focus tick when the screen opens
                }
            }

            if (resetFocus)
            {
                SetOptionFocus(OptionRow.Volume);
            }
        }

        void SetOptionFocus(OptionRow row)
        {
            if (Current.OptionsUi.Focus.enabled && row != optionFocus)
            {
                Sfx.Play(Sound.Tick);
            }

            optionFocus = row;
            foreach (var screens in AllScreens)
            {
                var options = screens.OptionsUi;
                options.Focus.rectTransform.anchoredPosition = new Vector2(options.X + options.Width / 2f,
                    -(options.Top + (int)row * options.Pitch + options.Height / 2f));
                options.Focus.enabled = true;
            }
        }
    }
}
