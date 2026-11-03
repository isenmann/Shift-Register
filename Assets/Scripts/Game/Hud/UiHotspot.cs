using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>An invisible rect that reports pointer enter, exit and click (menu rows, cards).</summary>
    public sealed class UiHotspot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action Enter, Exit, Click;

        public static UiHotspot Create(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = Ui.Rect(parent, name, x, y, w, h);
            rt.gameObject.AddComponent<Image>().color = Color.clear;
            return rt.gameObject.AddComponent<UiHotspot>();
        }

        public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
        public void OnPointerClick(PointerEventData e) => Click?.Invoke();
    }
}
