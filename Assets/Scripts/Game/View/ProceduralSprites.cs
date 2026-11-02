using System.Collections.Generic;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// Anti-aliased shapes drawn into textures at runtime, so the board needs no art files.
    /// Sizes are given in mockup pixels (a cell is 46); textures are rendered at
    /// <see cref="Resolution"/> pixels per cell, and one cell is one world unit.
    /// White sprites are tinted by the SpriteRenderer or Image colour. With ui = true a sprite is sized
    /// for a canvas whose units are mockup pixels (call Image.SetNativeSize).
    /// </summary>
    public static class ProceduralSprites
    {
        public const int Resolution = 128;
        const float Scale = Resolution / ScreenLayout.PxPerUnit;
        const int Pad = 2;
        /// <summary>UI sprites: one mockup pixel = one canvas unit (Canvas reference pixels per unit is 100).</summary>
        const float UiPixelsPerUnit = Scale * 100f;

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>Filled rounded rectangle.</summary>
        public static Sprite RoundedRect(float w, float h, float radius, bool ui = false) =>
            Get($"rect {w} {h} {radius}", w, h, ui, (px, py, hw, hh) =>
            {
                float d = RoundedRectDistance(px, py, hw, hh, radius * Scale);
                return new Color(1, 1, 1, Fill(d));
            });

        /// <summary>Rounded outline drawn inside the shape's edge; dash = 0 for a solid line.</summary>
        public static Sprite RoundedOutline(float w, float h, float radius, float thickness, float dash = 0f, bool ui = false) =>
            Get($"outline {w} {h} {radius} {thickness} {dash}", w, h, ui, (px, py, hw, hh) =>
            {
                float d = RoundedRectDistance(px, py, hw, hh, radius * Scale);
                float a = Fill(d) * Mathf.Clamp01(0.5f + d + thickness * Scale);
                if (dash > 0f && a > 0f)
                {
                    // Position along the nearest edge; dashes are centred on each edge's midpoint.
                    float s = Mathf.Abs(px) / hw > Mathf.Abs(py) / hh ? py : px;
                    float period = 2f * dash * Scale;
                    float phase = Mathf.Repeat(s + dash * Scale * 0.5f, period);
                    a *= phase < dash * Scale ? 1f : 0f;
                }
                return new Color(1, 1, 1, a);
            });

        /// <summary>Wall cell: diagonal hatching with a border, colours baked in.</summary>
        public static Sprite Hatched(float size, float radius, float stripe, float border, Color32 ink, Color32 ground) =>
            Get($"hatch {size} {radius} {stripe} {border} {ink} {ground}", size, size, false, (px, py, hw, hh) =>
            {
                float d = RoundedRectDistance(px, py, hw, hh, radius * Scale);
                float a = Fill(d);
                bool edge = d > -border * Scale;
                bool line = Mathf.Repeat(px - py, 2f * stripe * Scale) < stripe * Scale; // "/" stripes, like the mockup
                Color c = edge || line ? (Color)ink : (Color)ground;
                c.a = a;
                return c;
            });

        /// <summary>Upward chevron with round caps (the mockup's 16 px "M3 10l5-5 5 5" icon), scaled to size.</summary>
        public static Sprite Chevron(float size, float stroke, bool ui = false) =>
            Get($"chevron {size} {stroke}", size, size, ui, (px, py, hw, hh) =>
            {
                float k = size / 16f;
                var p = new Vector2(px, py) / Scale;
                var a = new Vector2(-5f, -2.5f) * k;
                var b = new Vector2(0f, 2.5f) * k;
                var c = new Vector2(5f, -2.5f) * k;
                float d = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)) - stroke * 0.5f;
                return new Color(1, 1, 1, Fill(d * Scale));
            });

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
            return (pa - ba * h).magnitude;
        }

        delegate Color PixelFn(float px, float py, float hw, float hh);

        static Sprite Get(string key, float w, float h, bool ui, PixelFn shade)
        {
            if (ui) key = "ui " + key;
            if (cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;

            int tw = Mathf.CeilToInt(w * Scale) + 2 * Pad;
            int th = Mathf.CeilToInt(h * Scale) + 2 * Pad;
            float hw = w * Scale * 0.5f, hh = h * Scale * 0.5f;
            var pixels = new Color[tw * th];
            for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                float px = x + 0.5f - tw * 0.5f;
                float py = y + 0.5f - th * 0.5f;
                pixels[y * tw + x] = shade(px, py, hw, hh);
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, true)
            {
                name = key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels(pixels);
            tex.Apply(true, true);

            sprite = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), ui ? UiPixelsPerUnit : Resolution, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            sprite.hideFlags = HideFlags.DontSave;
            cache[key] = sprite;
            return sprite;
        }

        static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

        /// <summary>Signed distance to a rounded rectangle centred on the origin (negative inside).</summary>
        static float RoundedRectDistance(float px, float py, float hw, float hh, float r)
        {
            r = Mathf.Min(r, Mathf.Min(hw, hh));
            float qx = Mathf.Abs(px) - (hw - r);
            float qy = Mathf.Abs(py) - (hh - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
    }
}
