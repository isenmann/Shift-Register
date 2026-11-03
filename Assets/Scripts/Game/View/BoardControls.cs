using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// The arrow buttons around the board and the outline that marks which line part a move will shift.
    /// Lives on the same object as <see cref="BoardView"/> and shares its local space (origin = board centre).
    /// </summary>
    public sealed class BoardControls : MonoBehaviour
    {
        const float Px = ScreenLayout.PxPerUnit;
        // Mockup sizes: buttons 46×36 px, 8 px from the board; selection outline 6 px around the cells.
        const float ButtonLong = 46f, ButtonShort = 36f, ButtonGap = 8f, ButtonRadius = 6f;
        const float OutlineMargin = 6f, OutlineRadius = 10f, OutlineWidth = 3f;
        const float DisabledAlpha = 0.25f;

        enum Layer { Belt = 4, Outline = 5, ButtonFill = 5, ButtonFrame = 6, ButtonIcon = 7 }

        public sealed class ArrowButton
        {
            public Move Move;
            public bool Enabled;
            public Rect Rect;
            public SpriteRenderer Frame, Fill, Icon;
        }

        [Tooltip("Unlit sprite material, so the palette shows exactly. Empty = the renderer's default.")]
        [SerializeField] Material spriteMaterial;

        readonly List<ArrowButton> buttons = new List<ArrowButton>();
        SpriteRenderer rowOutline, columnOutline;
        Transform root;
        LevelRules rules;
        int shownHover = int.MinValue;

        public IReadOnlyList<ArrowButton> Buttons => buttons;

        public void Build(LevelRules levelRules)
        {
            rules = levelRules;
            buttons.Clear();
            shownHover = int.MinValue;
            if (root != null) DestroySafe(root.gameObject);
            root = new GameObject("Controls").transform;
            root.SetParent(transform, false);

            float offset = ScreenLayout.BoardSize / 2f + (ButtonGap + ButtonShort / 2f) / Px;
            for (int c = 0; c < BoardBits.Size; c++)
            {
                float x = BoardView.CellCenter(0, c).x;
                AddButton(Direction.Up, c, new Vector2(x, offset));
                AddButton(Direction.Down, c, new Vector2(x, -offset));
            }
            for (int r = 0; r < BoardBits.Size; r++)
            {
                float y = BoardView.CellCenter(r, 0).y;
                AddButton(Direction.Left, r, new Vector2(-offset, y));
                AddButton(Direction.Right, r, new Vector2(offset, y));
            }

            BuildBelts(offset + ButtonShort / 2f / Px);
            rowOutline = Renderer(root, "Row outline", null, Lcd.Ink, (int)Layer.Outline);
            columnOutline = Renderer(root, "Column outline", null, Lcd.Ink, (int)Layer.Outline);
        }

        /// <summary>
        /// Rows that move as a unit get a dashed "belt" around them and their arrow buttons:
        /// a rotating row loops like a conveyor; a carry chain's two rows shift as one register.
        /// </summary>
        void BuildBelts(float halfWidth)
        {
            const float margin = 4.5f, thickness = 2f;
            for (int r = 0; r < BoardBits.Size; r++)
            {
                int top = rules.ChainTopOf(r);
                bool chain = top >= 0;
                if (chain && top != r) continue; // a chain is drawn once, from its top row
                if (!chain && !rules.IsRotating(r)) continue;

                int count = chain ? 2 : 1;
                float w = 2f * halfWidth * Px + 2f * margin;
                float h = count * 46f + (count - 1) * ScreenLayout.GapPx + 2f * margin;
                var belt = Renderer(root, chain ? $"Chain belt {r + 1}-{r + 2}" : $"Wrap belt {r + 1}",
                    ProceduralSprites.RoundedOutline(w, h, 14f, thickness, 6f), Lcd.Text3, (int)Layer.Belt);
                float y = (BoardView.CellCenter(r, 0).y + BoardView.CellCenter(r + count - 1, 0).y) / 2f;
                belt.transform.localPosition = new Vector3(0f, y, 0f);
            }
        }

        /// <summary>Index of the arrow button under a board-local point, or -1.</summary>
        public int HitButton(Vector2 local)
        {
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Rect.Contains(local)) return i;
            return -1;
        }

        /// <summary>The cell under a board-local point (gaps count as the nearest cell).</summary>
        public static bool HitCell(Vector2 local, out int row, out int column)
        {
            column = Mathf.RoundToInt(local.x / ScreenLayout.Pitch + 3.5f);
            row = Mathf.RoundToInt(3.5f - local.y / ScreenLayout.Pitch);
            return row >= 0 && row < BoardBits.Size && column >= 0 && column < BoardBits.Size;
        }

        /// <summary>
        /// Hovering a button outlines the line part it shifts; otherwise the cursor's row and column parts are outlined.
        /// </summary>
        public void Highlight(int hoveredButton, int cursorRow, int cursorColumn)
        {
            if (rules == null) return;
            SetHover(hoveredButton);

            if (hoveredButton >= 0)
            {
                var b = buttons[hoveredButton];
                var target = b.Move.Axis == Axis.Row ? rowOutline : columnOutline;
                var other = b.Move.Axis == Axis.Row ? columnOutline : rowOutline;
                Place(target, b.Move.Axis, b.Move.Line, b.Enabled ? b.Move.Segment : -1);
                other.enabled = false;
                return;
            }

            Place(rowOutline, Axis.Row, cursorRow, rules.SegmentOf(Axis.Row, cursorRow, cursorColumn));
            Place(columnOutline, Axis.Column, cursorColumn, rules.SegmentOf(Axis.Column, cursorColumn, cursorRow));
        }

        void SetHover(int index)
        {
            if (index == shownHover) return;
            shownHover = index;
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                bool hot = i == index && b.Enabled;
                b.Fill.enabled = hot;
                b.Icon.color = Fade(hot ? Lcd.Light : Lcd.Ink, b.Enabled);
            }
        }

        /// <summary>Outlines one line part (both rows for a carry chain); hidden for a wall cell (segment -1).</summary>
        void Place(SpriteRenderer outline, Axis axis, int line, int segment)
        {
            if (segment < 0)
            {
                outline.enabled = false;
                return;
            }
            var (first, last) = InputMapping.SegmentRange(rules, axis, line, segment);
            int lineFrom = line, lineTo = line;
            if (axis == Axis.Row && rules.ChainTopOf(line) >= 0)
            {
                lineFrom = rules.ChainTopOf(line);
                lineTo = lineFrom + 1;
            }

            Vector2 a = axis == Axis.Row ? BoardView.CellCenter(lineFrom, first) : BoardView.CellCenter(first, line);
            Vector2 b = axis == Axis.Row ? BoardView.CellCenter(lineTo, last) : BoardView.CellCenter(last, line);
            float along = Span(last - first + 1), across = Span(lineTo - lineFrom + 1);
            float w = axis == Axis.Row ? along : across;
            float h = axis == Axis.Row ? across : along;

            outline.sprite = ProceduralSprites.RoundedOutline(w, h, OutlineRadius, OutlineWidth);
            outline.transform.localPosition = (a + b) / 2f;
            outline.enabled = true;
        }

        /// <summary>Outline length in mockup px around n cells.</summary>
        static float Span(int cells) => cells * 46f + (cells - 1) * ScreenLayout.GapPx + 2f * OutlineMargin;

        void AddButton(Direction direction, int line, Vector2 position)
        {
            bool vertical = direction == Direction.Up || direction == Direction.Down;
            float w = vertical ? ButtonLong : ButtonShort;
            float h = vertical ? ButtonShort : ButtonLong;
            bool enabled = InputMapping.TryEdgeMove(rules, line, direction, out var move);

            var t = new GameObject($"Arrow {direction} {line + 1}").transform;
            t.SetParent(root, false);
            t.localPosition = position;

            var b = new ArrowButton
            {
                Move = enabled ? move : new Move(direction, line),
                Enabled = enabled,
                Rect = new Rect(position - new Vector2(w, h) / (2f * Px), new Vector2(w, h) / Px),
                Frame = Renderer(t, "Frame", ProceduralSprites.RoundedOutline(w, h, ButtonRadius, 2f), Fade(Lcd.Ink, enabled), (int)Layer.ButtonFrame),
                Fill = Renderer(t, "Fill", ProceduralSprites.RoundedRect(w, h, ButtonRadius), Lcd.Ink, (int)Layer.ButtonFill),
                Icon = Renderer(t, "Icon", ProceduralSprites.Chevron(16f, 2.5f), Fade(Lcd.Ink, enabled), (int)Layer.ButtonIcon)
            };
            b.Fill.enabled = false;
            b.Icon.transform.localRotation = Quaternion.Euler(0, 0, direction switch
            {
                Direction.Up => 0f,
                Direction.Down => 180f,
                Direction.Left => 90f,
                _ => -90f
            });
            buttons.Add(b);
        }

        static Color Fade(Color32 c, bool enabled)
        {
            Color color = c;
            if (!enabled) color.a = DisabledAlpha;
            return color;
        }

        SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }

        static void DestroySafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
