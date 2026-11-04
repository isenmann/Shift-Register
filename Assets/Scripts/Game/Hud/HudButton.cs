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
        public static HudButton Create(Transform parent, string text, float x, float y, float width, float height,
            TMP_FontAsset font, float size, Action onClick) =>
            Create(parent, text, x, y, width, height, font, size, Lcd.Ink, Lcd.Light, onClick);

        /// <param name="ink">Frame and label colour; also the hover fill.</param>
        /// <param name="paper">Label colour while hovered.</param>
        public static HudButton Create(Transform parent, string text, float x, float y, float width, float height,
            TMP_FontAsset font, float size, Color32 ink, Color32 paper, Action onClick)
        {
            var rectTransform = Ui.Rect(parent, text, x, y, width, height);
            // Invisible graphic so the whole rect receives pointer events.
            var hitArea = rectTransform.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear;

            var button = rectTransform.gameObject.AddComponent<HudButton>();
            button.onClick = onClick;
            button.ink = ink;
            button.paper = paper;
            button.fill = Ui.Box(rectTransform, "Fill", 0, 0, width, height, 6f, ink);
            button.fill.enabled = false;
            button.frame = Ui.Frame(rectTransform, "Frame", 0, 0, width, height, 6f, 2f, ink);
            button.label = Ui.Text(rectTransform, "Label", 0, 0, width, height, font, size, ink,
                TextAlignmentOptions.Center, text);
            return button;
        }

        /// <summary>Filled button (the title screen's PLAY): ink box, paper label, lighter ink on hover.</summary>
        public static HudButton CreateSolid(Transform parent, string text, float x, float y, float width, float height,
            TMP_FontAsset font, float size, Action onClick)
        {
            var button = Create(parent, text, x, y, width, height, font, size, Lcd.Ink, Lcd.Light, onClick);
            button.solid = true;
            button.SetHot(false);
            return button;
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

        public void OnPointerEnter(PointerEventData eventData) => SetHot(true);
        public void OnPointerExit(PointerEventData eventData) => SetHot(false);
        public void OnPointerClick(PointerEventData eventData)
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
            if (fill == null)
            {
                return;
            }

            Paint(hot);
            foreach (var icon in icons)
            {
                icon.color = label.color;
            }
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
