using ShiftRegister.Game.Audio;
using ShiftRegister.Game.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The options screen: volume, sound on/off and reduced motion. Arrows up/down pick a row,
    /// left/right change it, Enter switches it, Esc goes back.
    /// </summary>
    public sealed partial class MenuView
    {
        enum OptionRow { Volume, Sound, Motion }

        const float OptX = (1280f - OptW) / 2f, OptTop = 160f, OptW = 760f, OptH = 76f, OptPitch = 92f;
        const float CellW = 24f, CellGap = 6f;

        readonly Image[] volumeCells = new Image[GameSettings.VolumeSteps];
        TextMeshProUGUI volumeText;
        HudButton soundToggle, motionToggle;
        Image optionsFocus;
        OptionRow optionFocus;

        void BuildOptions(Transform root)
        {
            Ui.Text(root, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "OPTIONS");
            HudButton.Create(root, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));

            // Volume: − [10 cells] +
            var volume = OptionRowRoot(root, OptionRow.Volume, "VOLUME", out volumeText);
            HudButton.Create(volume, "-", 330, 16, 44, 44, display, 18, () => StepVolume(-1));
            float cellsX = 388f;
            for (int i = 0; i < volumeCells.Length; i++)
            {
                float cx = cellsX + i * (CellW + CellGap);
                volumeCells[i] = Ui.Box(volume, $"Volume {i + 1}", cx, 22, CellW, 32, 4f, Lcd.Cell);
                int step = i + 1;
                UiHotspot.Create(volume, $"Volume {i + 1} Hotspot", cx - CellGap / 2f, 16, CellW + CellGap, 44).Click =
                    () => SetVolume(game.Settings.volume == step && step == 1 ? 0 : step);
            }
            float plusX = cellsX + volumeCells.Length * (CellW + CellGap) + 8f;
            HudButton.Create(volume, "+", plusX, 16, 44, 44, display, 18, () => StepVolume(1));

            var sound = OptionRowRoot(root, OptionRow.Sound, "SOUND", out var soundText);
            soundText.text = "M mutes and unmutes anywhere.";
            soundToggle = HudButton.Create(sound, "ON", OptW - 144f, 16, 120, 44, display, 16, () => Toggle(OptionRow.Sound));

            var motion = OptionRowRoot(root, OptionRow.Motion, "REDUCED MOTION", out var motionText);
            motionText.text = "Moves snap into place: no sliding, flying or hopping.";
            motionToggle = HudButton.Create(motion, "OFF", OptW - 144f, 16, 120, 44, display, 16, () => Toggle(OptionRow.Motion));

            optionsFocus = Ui.Frame(root, "Focus", OptX, OptTop, OptW, OptH, 10f, 3f, Lcd.Ink);
            Ui.Text(root, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3,
                text: "Arrows choose and change · Enter switch · M mute · Esc back");

            game.SettingsChanged += OnSettingsChanged;
        }

        void OnDestroy()
        {
            if (game != null) game.SettingsChanged -= OnSettingsChanged;
        }

        /// <summary>A card row that focuses on hover; its controls are children so hovering them keeps the focus.</summary>
        RectTransform OptionRowRoot(Transform root, OptionRow row, string label, out TextMeshProUGUI text)
        {
            float y = OptTop + (int)row * OptPitch;
            var hot = UiHotspot.Create(root, label, OptX, y, OptW, OptH);
            hot.Enter = () => SetOptionFocus(row);
            var rt = (RectTransform)hot.transform;
            Ui.Box(rt, "Card", 0, 0, OptW, OptH, 10f, Lcd.Card);
            Ui.Text(rt, "Label", 24, 14, 300, 24, display, 18, Lcd.Ink, text: label);
            text = Ui.Text(rt, "Text", 24, 42, 560, 20, mono, 13, Lcd.Text2);
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
            if (optionsRoot != null && optionsRoot.gameObject.activeSelf) RefreshOptions(false);
        }

        void RefreshOptions(bool resetFocus)
        {
            var s = game.Settings;
            for (int i = 0; i < volumeCells.Length; i++)
                volumeCells[i].color = i < s.volume ? (s.muted ? Lcd.Text3 : Lcd.Ink) : Lcd.Cell;
            volumeText.text = s.muted ? $"{s.volume * 10}% · muted" : $"{s.volume * 10}%";
            soundToggle.Text = s.muted ? "OFF" : "ON";
            motionToggle.Text = s.reducedMotion ? "ON" : "OFF";
            if (resetFocus)
            {
                optionsFocus.enabled = false; // no focus tick when the screen opens
                SetOptionFocus(OptionRow.Volume);
            }
        }

        void SetOptionFocus(OptionRow row)
        {
            if (optionsFocus.enabled && row != optionFocus) Sfx.Play(Sound.Tick);
            optionFocus = row;
            var rt = optionsFocus.rectTransform;
            rt.anchoredPosition = new Vector2(OptX + OptW / 2f, -(OptTop + (int)row * OptPitch + OptH / 2f));
            optionsFocus.enabled = true;
        }
    }
}
