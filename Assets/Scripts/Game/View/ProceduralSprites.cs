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
        const int Padding = 2;
        /// <summary>UI sprites: one mockup pixel = one canvas unit (Canvas reference pixels per unit is 100).</summary>
        const float UiPixelsPerUnit = Scale * 100f;

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>Filled rounded rectangle.</summary>
        public static Sprite RoundedRect(float width, float height, float radius, bool ui = false) =>
            Get($"rect {width} {height} {radius}", width, height, ui, (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float distance = RoundedRectDistance(pixelX, pixelY, halfWidth, halfHeight, radius * Scale);
                return new Color(1, 1, 1, Fill(distance));
            });

        /// <summary>
        /// Filled rectangle rounding only its top-left and bottom-right corners (a gate's corner tab).
        /// </summary>
        public static Sprite CornerTab(float width, float height, float topLeftRadius, float bottomRightRadius) =>
            Get($"tab {width} {height} {topLeftRadius} {bottomRightRadius}", width, height, false,
            (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float cornerRadius = pixelX < 0f && pixelY > 0f ? topLeftRadius : pixelX > 0f && pixelY < 0f
                    ? bottomRightRadius
                    : 0f;
                float distance = RoundedRectDistance(pixelX, pixelY, halfWidth, halfHeight, cornerRadius * Scale);
                return new Color(1, 1, 1, Fill(distance));
            });

        /// <summary>Rounded outline drawn inside the shape's edge; dash = 0 for a solid line.</summary>
        public static Sprite RoundedOutline(float width, float height, float radius, float thickness, float dash = 0f,
            bool ui = false) =>
            Get($"outline {width} {height} {radius} {thickness} {dash}", width, height, ui,
            (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float distance = RoundedRectDistance(pixelX, pixelY, halfWidth, halfHeight, radius * Scale);
                float alpha = Fill(distance) * Mathf.Clamp01(0.5f + distance + thickness * Scale);
                if (dash > 0f && alpha > 0f)
                {
                    // Position along the nearest edge; dashes are centred on each edge's midpoint.
                    float edgePosition = Mathf.Abs(pixelX) / halfWidth > Mathf.Abs(pixelY) / halfHeight ? pixelY
                        : pixelX;
                    float period = 2f * dash * Scale;
                    float phase = Mathf.Repeat(edgePosition + dash * Scale * 0.5f, period);
                    alpha *= phase < dash * Scale ? 1f : 0f;
                }

                return new Color(1, 1, 1, alpha);
            });

        /// <summary>
        /// A quarter of a ring line (outer radius, line inside it) whose centre is the sprite's bottom-left corner;
        /// flip the renderer for the other quarters. Matches a <see cref="RoundedOutline"/> corner of the same radius.
        /// </summary>
        public static Sprite QuarterRing(float radius, float thickness) =>
            Get($"ring {radius} {thickness}", radius, radius, false, (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float x = pixelX + halfWidth, y = pixelY + halfHeight;
                if (x < 0f || y < 0f)
                {
                    return new Color(1, 1, 1, 0);
                }

                float distance = Mathf.Sqrt(x * x + y * y) - radius * Scale;
                return new Color(1, 1, 1, Fill(distance) * Mathf.Clamp01(0.5f + distance + thickness * Scale));
            });

        /// <summary>Wall cell: diagonal hatching with a border, colours baked in.</summary>
        public static Sprite Hatched(float size, float radius, float stripe, float border, Color32 ink,
            Color32 ground) =>
            Get($"hatch {size} {radius} {stripe} {border} {ink} {ground}", size, size, false,
            (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float distance = RoundedRectDistance(pixelX, pixelY, halfWidth, halfHeight, radius * Scale);
                float alpha = Fill(distance);
                bool edge = distance > -border * Scale;
                // "/" stripes, like the mockup
                bool line = Mathf.Repeat(pixelX - pixelY, 2f * stripe * Scale) < stripe * Scale;
                Color color = edge || line ? (Color)ink : (Color)ground;
                color.a = alpha;
                return color;
            });

        /// <summary>Upward chevron with round caps (the mockup's 16 px "M3 10l5-5 5 5" icon), scaled to size.</summary>
        public static Sprite Chevron(float size, float stroke, bool ui = false) =>
            Get($"chevron {size} {stroke}", size, size, ui, (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float iconScale = size / 16f;
                var point = new Vector2(pixelX, pixelY) / Scale;
                var left = new Vector2(-5f, -2.5f) * iconScale;
                var tip = new Vector2(0f, 2.5f) * iconScale;
                var right = new Vector2(5f, -2.5f) * iconScale;
                float distance = Mathf.Min(SegmentDistance(point, left, tip),
                    SegmentDistance(point, tip, right)) - stroke * 0.5f;
                return new Color(1, 1, 1, Fill(distance * Scale));
            });

        /// <summary>Five-point star, point up; thickness 0 = filled, else an outline of that width.</summary>
        public static Sprite Star(float size, float thickness = 0f, bool ui = false) =>
            Get($"star {size} {thickness}", size, size, ui, (pixelX, pixelY, halfWidth, halfHeight) =>
            {
                float outerRadius = halfWidth * 0.92f;
                float distance = StarDistance(new Vector2(pixelX, pixelY + halfHeight * 0.06f), outerRadius,
                    0.45f) - outerRadius * 0.06f;
                float alpha = Fill(distance);
                if (thickness > 0f)
                {
                    alpha *= Mathf.Clamp01(0.5f + distance + thickness * Scale);
                }

                return new Color(1, 1, 1, alpha);
            });

        /// <summary>Signed distance to a five-point star of the given outer radius (Inigo Quilez's sdStar5).</summary>
        static float StarDistance(Vector2 point, float radius, float innerFactor)
        {
            var axis1 = new Vector2(0.809016994375f, -0.587785252292f);
            var axis2 = new Vector2(-axis1.x, axis1.y);
            point.x = Mathf.Abs(point.x);
            point -= 2f * Mathf.Max(Vector2.Dot(axis1, point), 0f) * axis1;
            point -= 2f * Mathf.Max(Vector2.Dot(axis2, point), 0f) * axis2;
            point.x = Mathf.Abs(point.x);
            point.y -= radius;
            var edge = innerFactor * new Vector2(-axis1.y, axis1.x) - new Vector2(0f, 1f);
            float along = Mathf.Clamp(Vector2.Dot(point, edge) / Vector2.Dot(edge, edge), 0f, radius);
            return (point - edge * along).magnitude * Mathf.Sign(point.y * edge.x - point.x * edge.y);
        }

        static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 toPoint = point - start, segment = end - start;
            float along = Mathf.Clamp01(Vector2.Dot(toPoint, segment) / segment.sqrMagnitude);
            return (toPoint - segment * along).magnitude;
        }

        delegate Color PixelFunction(float pixelX, float pixelY, float halfWidth, float halfHeight);

        static Sprite Get(string key, float width, float height, bool ui, PixelFunction shade)
        {
            if (ui)
            {
                key = "ui " + key;
            }

            if (cache.TryGetValue(key, out var sprite) && sprite != null)
            {
                return sprite;
            }

            int textureWidth = Mathf.CeilToInt(width * Scale) + 2 * Padding;
            int textureHeight = Mathf.CeilToInt(height * Scale) + 2 * Padding;
            float halfWidth = width * Scale * 0.5f, halfHeight = height * Scale * 0.5f;
            var pixels = new Color[textureWidth * textureHeight];
            for (int y = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < textureWidth; x++)
                {
                    float pixelX = x + 0.5f - textureWidth * 0.5f;
                    float pixelY = y + 0.5f - textureHeight * 0.5f;
                    pixels[y * textureWidth + x] = shade(pixelX, pixelY, halfWidth, halfHeight);
                }
            }

            var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, true)
            {
                name = key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels(pixels);
            texture.Apply(true, true);

            sprite = Sprite.Create(texture, new Rect(0, 0, textureWidth, textureHeight), new Vector2(0.5f, 0.5f), ui
                ? UiPixelsPerUnit
                : Resolution, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            sprite.hideFlags = HideFlags.DontSave;
            cache[key] = sprite;
            return sprite;
        }

        static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

        /// <summary>Signed distance to a rounded rectangle centred on the origin (negative inside).</summary>
        static float RoundedRectDistance(float pixelX, float pixelY, float halfWidth, float halfHeight, float radius)
        {
            radius = Mathf.Min(radius, Mathf.Min(halfWidth, halfHeight));
            float cornerX = Mathf.Abs(pixelX) - (halfWidth - radius);
            float cornerY = Mathf.Abs(pixelY) - (halfHeight - radius);
            float outside = new Vector2(Mathf.Max(cornerX, 0f), Mathf.Max(cornerY, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(cornerX, cornerY), 0f) - radius;
        }
    }
}
