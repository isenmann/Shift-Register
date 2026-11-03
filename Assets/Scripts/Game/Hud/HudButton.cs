using System;
using ShiftRegister.Game.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>Outlined text button that fills with ink on hover, like the mockup's UNDO / RESET.</summary>
    public sealed class HudButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        Image fill;
        TextMeshProUGUI label;
        Action onClick;
        Color32 ink, paper;
        bool solid, selected, disabled;
        Image frame;
        readonly System.Collections.Generic.List<Image> icons = new System.Collections.Generic.List<Image>();

        /// <summary>Ink-on-background button (top bar).</summary>
        public static HudButton Create(Transform parent, string text, float x, float y, float w, float h,
            TMP_FontAsset font, float size, Action onClick) =>
            Create(parent, text, x, y, w, h, font, size, Lcd.Ink, Lcd.Light, onClick);

        /// <param name="ink">Frame and label colour; also the hover fill.</param>
        /// <param name="paper">Label colour while hovered.</param>
        public static HudButton Create(Transform parent, string text, float x, float y, float w, float h,
            TMP_FontAsset font, float size, Color32 ink, Color32 paper, Action onClick)
        {
            var rt = Ui.Rect(parent, text, x, y, w, h);
            // Invisible graphic so the whole rect receives pointer events.
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var b = rt.gameObject.AddComponent<HudButton>();
            b.onClick = onClick;
            b.ink = ink;
            b.paper = paper;
            b.fill = Ui.Box(rt, "Fill", 0, 0, w, h, 6f, ink);
            b.fill.enabled = false;
            b.frame = Ui.Frame(rt, "Frame", 0, 0, w, h, 6f, 2f, ink);
            b.label = Ui.Text(rt, "Label", 0, 0, w, h, font, size, ink, TextAlignmentOptions.Center, text);
            return b;
        }

        /// <summary>Filled button (the title screen's PLAY): ink box, paper label, lighter ink on hover.</summary>
        public static HudButton CreateSolid(Transform parent, string text, float x, float y, float w, float h,
            TMP_FontAsset font, float size, Action onClick)
        {
            var b = Create(parent, text, x, y, w, h, font, size, Lcd.Ink, Lcd.Light, onClick);
            b.solid = true;
            b.SetHot(false);
            return b;
        }

        public string Text
        {
            get => label.text;
            set => label.text = value;
        }

        /// <summary>Adds a picture to the button that changes colour with its label.</summary>
        public void AddIcon(Image icon)
        {
            icons.Add(icon);
            SetHot(false);
        }

        /// <summary>Stays filled, for a tool or tab that is on.</summary>
        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                SetHot(false);
            }
        }

        /// <summary>Greyed out; a click only makes the blocked sound.</summary>
        public bool Disabled
        {
            get => disabled;
            set
            {
                disabled = value;
                SetHot(false);
            }
        }

        void OnDisable() => SetHot(false);

        public void OnPointerEnter(PointerEventData e) => SetHot(true);
        public void OnPointerExit(PointerEventData e) => SetHot(false);
        public void OnPointerClick(PointerEventData e)
        {
            if (disabled)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            Sfx.Play(Sound.Click);
            onClick?.Invoke();
        }

        void SetHot(bool hot)
        {
            if (fill == null) return;
            Paint(hot);
            foreach (var icon in icons) icon.color = label.color;
        }

        void Paint(bool hot)
        {
            if (disabled)
            {
                fill.enabled = false;
                frame.color = Lcd.Cell;
                label.color = Lcd.Text3;
                return;
            }
            frame.color = ink;
            if (selected)
            {
                fill.enabled = true;
                fill.color = ink;
                label.color = paper;
                return;
            }
            if (solid)
            {
                fill.enabled = true;
                fill.color = hot ? Lcd.Text3 : ink;
                label.color = paper;
                return;
            }
            fill.enabled = hot;
            label.color = hot ? paper : ink;
        }
    }
}
