using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// Builds uGUI elements from mockup coordinates: x/y from the top-left of a 1280×720 frame,
    /// one canvas unit per mockup pixel.
    /// </summary>
    public static class Ui
    {
        public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rectTransform = (RectTransform)gameObject.transform;
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0f, 1f);
            rectTransform.anchoredPosition = new Vector2(x, -y);
            rectTransform.sizeDelta = new Vector2(width, height);
            return rectTransform;
        }

        /// <summary>Full-size child of a parent rect.</summary>
        public static RectTransform Fill(Transform parent, string name)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rectTransform = (RectTransform)gameObject.transform;
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = rectTransform.offsetMax = Vector2.zero;
            return rectTransform;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, float x, float y, float width, float height,
            TMP_FontAsset font, float size, Color32 color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, string text = "")
        {
            var label = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            Style(label, font, size, color, align);
            label.text = text;
            return label;
        }

        public static void Style(TextMeshProUGUI label, TMP_FontAsset font, float size, Color32 color,
            TextAlignmentOptions align)
        {
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>A procedural UI sprite centred on the given mockup rect.</summary>
        public static Image Shape(Transform parent, string name, float x, float y, float width, float height,
            Sprite sprite, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rectTransform = (RectTransform)gameObject.transform;
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(x + width / 2f, -(y + height / 2f));
            var image = gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.SetNativeSize();
            return image;
        }

        public static Image Box(Transform parent, string name, float x, float y, float width, float height,
            float radius, Color color) =>
            Shape(parent, name, x, y, width, height, ProceduralSprites.RoundedRect(width, height, radius, ui: true),
            color);

        public static Image Frame(Transform parent, string name, float x, float y, float width, float height,
            float radius,
            float thickness, Color color, float dash = 0f) =>
            Shape(parent, name, x, y, width, height,
            ProceduralSprites.RoundedOutline(width, height, radius, thickness, dash, ui: true), color);

        /// <summary>
        /// Draws a canvas through the main camera instead of over the whole screen, so it follows the
        /// camera's viewport (the safe area on phones). <paramref name="order"/> keeps it above the board's sprites.
        /// </summary>
        public static void RenderWithMainCamera(Canvas canvas, int order)
        {
            var mainCamera = Camera.main;
            if (canvas == null || mainCamera == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCamera;
            canvas.planeDistance = 5f;
            canvas.sortingOrder = order;
        }

        /// <summary>Frames after a text field closes in which its closing key still belongs to it.</summary>
        const int TypingGraceFrames = 2;
        static int typingEndedFrame = -TypingGraceFrames - 1;

        /// <summary>
        /// A text field just closed. The field can handle Esc or Enter before the game's own key checks
        /// see the same press, and that press must not also act as a shortcut.
        /// </summary>
        public static void NoteTypingEnded() => typingEndedFrame = Time.frameCount;

        /// <summary>True while a text field has the keyboard: keys belong to it, not to game shortcuts.</summary>
        public static bool IsTyping
        {
            get
            {
                if (Time.frameCount - typingEndedFrame <= TypingGraceFrames)
                {
                    return true;
                }

                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                return selected != null && selected.TryGetComponent<TMP_InputField>(out var field) && field.isFocused;
            }
        }

        /// <summary>"1 move", "6 moves": custom levels can have a par of 1.</summary>
        public static string Moves(int count) => count == 1 ? "1 move" : $"{count} moves";

        public static string Hex(Color32 color) => $"#{color.r:X2}{color.g:X2}{color.b:X2}";
    }
}
