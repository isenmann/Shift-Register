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

        public static UiHotspot Create(Transform parent, string name, float x, float y, float width, float height)
        {
            var rectTransform = Ui.Rect(parent, name, x, y, width, height);
            rectTransform.gameObject.AddComponent<Image>().color = Color.clear;
            return rectTransform.gameObject.AddComponent<UiHotspot>();
        }

        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
        public void OnPointerClick(PointerEventData eventData) => Click?.Invoke();
    }
}
