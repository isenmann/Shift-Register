using System;
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

        public static HudButton Create(Transform parent, string text, float x, float y, float w, float h,
            TMP_FontAsset font, float size, Action onClick)
        {
            var rt = Ui.Rect(parent, text, x, y, w, h);
            // Invisible graphic so the whole rect receives pointer events.
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var b = rt.gameObject.AddComponent<HudButton>();
            b.onClick = onClick;
            b.fill = Ui.Box(rt, "Fill", 0, 0, w, h, 6f, Lcd.Ink);
            b.fill.enabled = false;
            Ui.Frame(rt, "Frame", 0, 0, w, h, 6f, 2f, Lcd.Ink);
            b.label = Ui.Text(rt, "Label", 0, 0, w, h, font, size, Lcd.Ink, TextAlignmentOptions.Center, text);
            return b;
        }

        public void OnPointerEnter(PointerEventData e) => SetHot(true);
        public void OnPointerExit(PointerEventData e) => SetHot(false);
        public void OnPointerClick(PointerEventData e) => onClick?.Invoke();

        void SetHot(bool hot)
        {
            fill.enabled = hot;
            label.color = hot ? Lcd.Light : Lcd.Ink;
        }
    }
}
