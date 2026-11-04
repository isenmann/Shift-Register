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
        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Full-size child of a parent rect.</summary>
        public static RectTransform Fill(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, float x, float y, float w, float h,
            TMP_FontAsset font, float size, Color32 color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, string text = "")
        {
            var t = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            Style(t, font, size, color, align);
            t.text = text;
            return t;
        }

        public static void Style(TextMeshProUGUI t, TMP_FontAsset font, float size, Color32 color, TextAlignmentOptions align)
        {
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>A procedural UI sprite centred on the given mockup rect.</summary>
        public static Image Shape(Transform parent, string name, float x, float y, float w, float h, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + w / 2f, -(y + h / 2f));
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            img.SetNativeSize();
            return img;
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, float radius, Color color) =>
            Shape(parent, name, x, y, w, h, ProceduralSprites.RoundedRect(w, h, radius, ui: true), color);

        public static Image Frame(Transform parent, string name, float x, float y, float w, float h, float radius,
            float thickness, Color color, float dash = 0f) =>
            Shape(parent, name, x, y, w, h, ProceduralSprites.RoundedOutline(w, h, radius, thickness, dash, ui: true), color);

        /// <summary>
        /// Draws a canvas through the main camera instead of over the whole screen, so it follows the
        /// camera's viewport (the safe area on phones). <paramref name="order"/> keeps it above the board's sprites.
        /// </summary>
        public static void RenderWithMainCamera(Canvas canvas, int order)
        {
            var cam = Camera.main;
            if (canvas == null || cam == null) return;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
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
                if (Time.frameCount - typingEndedFrame <= TypingGraceFrames) return true;
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                return selected != null && selected.TryGetComponent<TMP_InputField>(out var field) && field.isFocused;
            }
        }

        /// <summary>"1 move", "6 moves": custom levels can have a par of 1.</summary>
        public static string Moves(int count) => count == 1 ? "1 move" : $"{count} moves";

        public static string Hex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }
}
