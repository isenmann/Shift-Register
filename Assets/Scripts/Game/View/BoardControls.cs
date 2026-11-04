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
        enum Layer { Belt = 4, Outline = 5, ButtonFill = 5, ButtonFrame = 6, ButtonIcon = 7 }

        public sealed class ArrowButton
        {
            public Move Move;
            public bool Enabled;
            public Rect Rect;
            public SpriteRenderer Frame, Fill, Icon;
        }

        const float PixelsPerUnit = ScreenLayout.PxPerUnit;
        // Mockup sizes: buttons 46×36 px, 8 px from the board; selection outline 6 px around the cells.
        const float ButtonLong = 46f, ButtonShort = 36f, ButtonGap = 8f, ButtonRadius = 6f;
        const float OutlineMargin = 6f, OutlineRadius = 10f, OutlineWidth = 3f;
        const float DisabledAlpha = 0.25f;

        [Tooltip("Unlit sprite material, so the palette shows exactly. Empty = the renderer's default.")]
        [SerializeField] Material spriteMaterial;

        readonly List<ArrowButton> buttons = new List<ArrowButton>();
        SpriteRenderer rowOutline, columnOutline, cellOutline;
        readonly List<SpriteRenderer> crossPieces = new List<SpriteRenderer>();
        Transform crossRoot;
        Transform root;
        LevelRules rules;
        int shownHover = int.MinValue;

        public IReadOnlyList<ArrowButton> Buttons => buttons;

        /// <param name="withArrows">False for the level editor, where the board is painted and nothing shifts.</param>
        /// <param name="tightBelts">Belts hug the cells, leaving the arrow button slots free for the editor's own
        /// buttons.</param>
        public void Build(LevelRules levelRules, bool withArrows = true, bool tightBelts = false)
        {
            rules = levelRules;
            buttons.Clear();
            shownHover = int.MinValue;
            if (root != null)
            {
                DestroySafe(root.gameObject);
            }

            root = new GameObject("Controls").transform;
            root.SetParent(transform, false);

            // Phones and tablets shift by swiping in either orientation: no arrow buttons, and the belts hug the board.
            bool arrows = withArrows && !DeviceMode.IsMobile;
            float offset = ScreenLayout.BoardSize / 2f + (ButtonGap + ButtonShort / 2f) / PixelsPerUnit;
            if (arrows)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    float x = BoardView.CellCenter(0, column).x;
                    AddButton(Direction.Up, column, new Vector2(x, offset));
                    AddButton(Direction.Down, column, new Vector2(x, -offset));
                }

                for (int row = 0; row < BoardBits.Size; row++)
                {
                    float y = BoardView.CellCenter(row, 0).y;
                    AddButton(Direction.Left, row, new Vector2(-offset, y));
                    AddButton(Direction.Right, row, new Vector2(offset, y));
                }
            }

            BuildBelts(arrows ? offset + ButtonShort / 2f / PixelsPerUnit
                : ScreenLayout.BoardSize / 2f + (tightBelts ? 0f : ButtonGap / PixelsPerUnit));
            rowOutline = Renderer(root, "Row outline", null, Lcd.Ink, (int)Layer.Outline);
            columnOutline = Renderer(root, "Column outline", null, Lcd.Ink, (int)Layer.Outline);
            crossPieces.Clear();
            crossRoot = new GameObject("Cursor outline").transform;
            crossRoot.SetParent(root, false);
            float cellOutlineSize = 46f + 2f * OutlineMargin;
            cellOutline = Renderer(root, "Cell outline",
                ProceduralSprites.RoundedOutline(cellOutlineSize, cellOutlineSize, OutlineRadius, OutlineWidth),
                Lcd.Ink, (int)Layer.Outline);
            cellOutline.enabled = false;
        }

        /// <summary>The level editor's cursor: outlines one cell.</summary>
        public void HighlightCell(int row, int column)
        {
            if (cellOutline == null)
            {
                return;
            }

            cellOutline.transform.localPosition = BoardView.CellCenter(row, column);
            cellOutline.enabled = true;
        }

        /// <summary>
        /// Rows that move as a unit get a dashed "belt" around them and their arrow buttons:
        /// a rotating row loops like a conveyor; a carry chain's two rows shift as one register.
        /// </summary>
        void BuildBelts(float halfWidth)
        {
            const float margin = 4.5f, thickness = 2f;
            for (int row = 0; row < BoardBits.Size; row++)
            {
                int top = rules.ChainTopOf(row);
                bool chain = top >= 0;
                if (chain && top != row)
                {
                    continue; // a chain is drawn once, from its top row
                }

                if (!chain && !rules.IsRotating(row))
                {
                    continue;
                }

                int count = chain ? 2 : 1;
                float width = 2f * halfWidth * PixelsPerUnit + 2f * margin;
                float height = count * 46f + (count - 1) * ScreenLayout.GapPx + 2f * margin;
                var belt = Renderer(root, chain ? $"Chain belt {row + 1}-{row + 2}" : $"Wrap belt {row + 1}",
                    ProceduralSprites.RoundedOutline(width, height, 14f, thickness, 6f), Lcd.Text3, (int)Layer.Belt);
                float y = (BoardView.CellCenter(row, 0).y + BoardView.CellCenter(row + count - 1, 0).y) / 2f;
                belt.transform.localPosition = new Vector3(0f, y, 0f);
            }
        }

        /// <summary>Index of the arrow button under a board-local point, or -1.</summary>
        public int HitButton(Vector2 local)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i].Rect.Contains(local))
                {
                    return i;
                }
            }

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
        /// Hovering a button outlines the line part it shifts; otherwise the cursor's row and column parts are
        /// outlined.
        /// </summary>
        public void Highlight(int hoveredButton, int cursorRow, int cursorColumn)
        {
            if (rules == null)
            {
                return;
            }

            SetHover(hoveredButton);

            if (hoveredButton >= 0)
            {
                var button = buttons[hoveredButton];
                var target = button.Move.Axis == Axis.Row ? rowOutline : columnOutline;
                var other = button.Move.Axis == Axis.Row ? columnOutline : rowOutline;
                Place(target, button.Move.Axis, button.Move.Line, button.Enabled ? button.Move.Segment : -1);
                other.enabled = false;
                crossRoot.gameObject.SetActive(false);
                return;
            }

            rowOutline.enabled = columnOutline.enabled = false;
            ShowCross(cursorRow, cursorColumn);
        }

        void SetHover(int index)
        {
            if (index == shownHover)
            {
                return;
            }

            shownHover = index;
            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                bool hot = i == index && button.Enabled;
                button.Fill.enabled = hot;
                button.Icon.color = Fade(hot ? Lcd.Light : Lcd.Ink, button.Enabled);
            }
        }

        /// <summary>
        /// Outlines one line part (both rows for a carry chain); hidden for a wall cell (segment -1).
        /// </summary>
        void Place(SpriteRenderer outline, Axis axis, int line, int segment)
        {
            if (!PartRect(axis, line, segment, out var rect))
            {
                outline.enabled = false;
                return;
            }

            outline.sprite = ProceduralSprites.RoundedOutline(rect.width * PixelsPerUnit, rect.height * PixelsPerUnit,
                OutlineRadius, OutlineWidth);
            outline.transform.localPosition = rect.center;
            outline.enabled = true;
        }

        /// <summary>
        /// The outline's outer edge around one line part (both rows for a carry chain), in board-local units.
        /// </summary>
        bool PartRect(Axis axis, int line, int segment, out Rect rect)
        {
            rect = default;
            if (segment < 0)
            {
                return false;
            }

            var (first, last) = InputMapping.SegmentRange(rules, axis, line, segment);
            int lineFrom = line, lineTo = line;
            if (axis == Axis.Row && rules.ChainTopOf(line) >= 0)
            {
                lineFrom = rules.ChainTopOf(line);
                lineTo = lineFrom + 1;
            }

            Vector2 firstCenter = axis == Axis.Row ? BoardView.CellCenter(lineFrom, first)
                : BoardView.CellCenter(first, line);
            Vector2 lastCenter = axis == Axis.Row ? BoardView.CellCenter(lineTo, last)
                : BoardView.CellCenter(last, line);
            float along = Span(last - first + 1), across = Span(lineTo - lineFrom + 1);
            float width = axis == Axis.Row ? along : across;
            float height = axis == Axis.Row ? across : along;

            var size = new Vector2(width, height) / PixelsPerUnit;
            rect = new Rect((firstCenter + lastCenter) / 2f - size / 2f, size);
            return true;
        }

        /// <summary>
        /// The cursor's row and column parts outlined as one shape (a plus, T or L): the outline of their union,
        /// with the inner corners rounded like the outer ones. Built from straight bars and quarter rings,
        /// since one texture of the whole shape would be up to board-sized for every cursor position.
        /// </summary>
        void ShowCross(int row, int column)
        {
            int used = 0;
            var rects = new List<Rect>(2);
            if (PartRect(Axis.Row, row, rules.SegmentOf(Axis.Row, row, column), out var rowRect))
            {
                rects.Add(rowRect);
            }

            if (PartRect(Axis.Column, column, rules.SegmentOf(Axis.Column, column, row), out var columnRect))
            {
                rects.Add(columnRect);
            }

            if (rects.Count > 0)
            {
                // The union on a grid of the rectangles' edges: cell (i, j) spans xEdges[i]..xEdges[i+1],
                // yEdges[j]..yEdges[j+1].
                var xEdges = Edges(rects, true);
                var yEdges = Edges(rects, false);
                bool Occupied(int i, int j)
                {
                    if (i < 0 || j < 0 || i >= xEdges.Count - 1 || j >= yEdges.Count - 1)
                    {
                        return false;
                    }

                    var center = new Vector2((xEdges[i] + xEdges[i + 1]) / 2f, (yEdges[j] + yEdges[j + 1]) / 2f);
                    foreach (var rect in rects)
                    {
                        if (rect.Contains(center))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                // Corners: one filled quadrant around a grid point is an outer corner, three an inner one.
                // Both use the same quarter ring; how far the straight lines stop short of each corner is kept.
                var trim = new float[xEdges.Count, yEdges.Count];
                float outer = OutlineRadius / PixelsPerUnit, inner = (OutlineRadius - OutlineWidth) / PixelsPerUnit;
                for (int i = 0; i < xEdges.Count; i++)
                {
                    for (int j = 0; j < yEdges.Count; j++)
                    {
                        bool bottomLeft = Occupied(i - 1, j - 1), bottomRight = Occupied(i, j - 1),
                            topLeft = Occupied(i - 1, j), topRight = Occupied(i, j);
                        int filled = (bottomLeft ? 1 : 0) + (bottomRight ? 1 : 0) + (topLeft ? 1 : 0)
                            + (topRight ? 1 : 0);
                        if (filled != 1 && filled != 3)
                        {
                            continue;
                        }

                        // the odd quadrant lies to the right
                        bool odd = filled == 1 ? topRight || bottomRight : !topRight || !bottomRight;
                        bool up = filled == 1 ? topRight || topLeft : !topRight || !topLeft;
                        var toward = new Vector2(odd ? 1f : -1f, up ? 1f : -1f);
                        var corner = new Vector2(xEdges[i], yEdges[j]);
                        // Outer: ring centre inside the shape, arc facing out. Inner: centre outside, arc facing the
                        // corner.
                        float radius = filled == 1 ? outer : inner;
                        var centre = corner + toward * radius;
                        var arc = -toward;
                        trim[i, j] = radius;

                        var ring = Piece(used++, ProceduralSprites.QuarterRing(OutlineRadius, OutlineWidth));
                        ring.flipX = arc.x < 0f;
                        ring.flipY = arc.y < 0f;
                        ring.transform.localPosition = centre + arc * (OutlineRadius / PixelsPerUnit / 2f);
                    }
                }

                // Straight lines: runs of grid edges with the shape on the same side, drawn on that side.
                for (int pass = 0; pass < 2; pass++)
                {
                    bool horizontal = pass == 0;
                    var along = horizontal ? xEdges : yEdges;
                    var across = horizontal ? yEdges : xEdges;
                    for (int acrossIndex = 0; acrossIndex < across.Count; acrossIndex++)
                    {
                        int start = 0, startSide = 0;
                        for (int alongIndex = 0; alongIndex <= along.Count - 1; alongIndex++)
                        {
                            int side = 0;
                            if (alongIndex < along.Count - 1)
                            {
                                bool before = horizontal ? Occupied(alongIndex, acrossIndex - 1)
                                    : Occupied(acrossIndex - 1, alongIndex);
                                bool after = horizontal ? Occupied(alongIndex, acrossIndex)
                                    : Occupied(acrossIndex, alongIndex);
                                side = before == after ? 0 : after ? 1 : -1;
                            }

                            if (alongIndex > 0 && side == startSide)
                            {
                                continue;
                            }

                            if (startSide != 0)
                            {
                                used = AddBar(used, horizontal, across[acrossIndex], startSide,
                                along[start] + (horizontal ? trim[start, acrossIndex] : trim[acrossIndex, start]),
                                along[alongIndex] - (horizontal ? trim[alongIndex, acrossIndex]
                                : trim[acrossIndex, alongIndex]));
                            }

                            start = alongIndex;
                            startSide = side;
                        }
                    }
                }
            }

            for (int i = used; i < crossPieces.Count; i++)
            {
                crossPieces[i].enabled = false;
            }

            crossRoot.gameObject.SetActive(true);
        }

        /// <summary>
        /// One straight piece of the cursor outline on the line at 'position', drawn towards 'side' (+1 = up/right).
        /// </summary>
        int AddBar(int used, bool horizontal, float position, int side, float from, float to)
        {
            // A pixel of overlap with the rings at both ends hides the seams.
            float overlap = 1f / PixelsPerUnit, length = to - from + 2f * overlap;
            if (length <= 0f)
            {
                return used;
            }

            float lengthPx = Mathf.Round(length * PixelsPerUnit * 100f) / 100f;
            var bar = Piece(used, horizontal
                ? ProceduralSprites.RoundedRect(lengthPx, OutlineWidth, 0f)
                : ProceduralSprites.RoundedRect(OutlineWidth, lengthPx, 0f));
            bar.flipX = bar.flipY = false;
            float mid = (from + to) / 2f, offset = position + side * OutlineWidth / PixelsPerUnit / 2f;
            bar.transform.localPosition = horizontal ? new Vector2(mid, offset) : new Vector2(offset, mid);
            return used + 1;
        }

        SpriteRenderer Piece(int index, Sprite sprite)
        {
            if (index == crossPieces.Count)
            {
                crossPieces.Add(Renderer(crossRoot, $"Piece {index}", null, Lcd.Ink, (int)Layer.Outline));
            }

            var piece = crossPieces[index];
            piece.sprite = sprite;
            piece.enabled = true;
            return piece;
        }

        /// <summary>The rectangles' distinct left/right (or bottom/top) edges, in order.</summary>
        static List<float> Edges(List<Rect> rects, bool x)
        {
            var edges = new List<float>();
            foreach (var rect in rects)
            {
                foreach (float edge in x ? new[] { rect.xMin, rect.xMax } : new[] { rect.yMin, rect.yMax })
                {
                    if (!edges.Exists(other => Mathf.Abs(other - edge) < 0.001f))
                    {
                        edges.Add(edge);
                    }
                }
            }

            edges.Sort();
            return edges;
        }

        /// <summary>Outline length in mockup px around n cells.</summary>
        static float Span(int cells) => cells * 46f + (cells - 1) * ScreenLayout.GapPx + 2f * OutlineMargin;

        void AddButton(Direction direction, int line, Vector2 position)
        {
            bool vertical = direction == Direction.Up || direction == Direction.Down;
            float width = vertical ? ButtonLong : ButtonShort;
            float height = vertical ? ButtonShort : ButtonLong;
            bool enabled = InputMapping.TryEdgeMove(rules, line, direction, out var move);

            var parent = new GameObject($"Arrow {direction} {line + 1}").transform;
            parent.SetParent(root, false);
            parent.localPosition = position;

            var button = new ArrowButton
            {
                Move = enabled ? move : new Move(direction, line),
                Enabled = enabled,
                Rect = new Rect(position - new Vector2(width, height) / (2f * PixelsPerUnit),
                new Vector2(width, height) / PixelsPerUnit),
                Frame = Renderer(parent, "Frame", ProceduralSprites.RoundedOutline(width, height, ButtonRadius, 2f),
                Fade(Lcd.Ink, enabled), (int)Layer.ButtonFrame),
                Fill = Renderer(parent, "Fill", ProceduralSprites.RoundedRect(width, height, ButtonRadius), Lcd.Ink,
                (int)Layer.ButtonFill),
                Icon = Renderer(parent, "Icon", ProceduralSprites.Chevron(16f, 2.5f), Fade(Lcd.Ink, enabled),
                (int)Layer.ButtonIcon)
            };
            button.Fill.enabled = false;
            button.Icon.transform.localRotation = Quaternion.Euler(0, 0, direction switch
            {
                Direction.Up => 0f,
                Direction.Down => 180f,
                Direction.Left => 90f,
                _ => -90f
            });
            buttons.Add(button);
        }

        static Color Fade(Color32 baseColor, bool enabled)
        {
            Color color = baseColor;
            if (!enabled)
            {
                color.a = DisabledAlpha;
            }

            return color;
        }

        SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, Color color, int order)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = order;
            if (spriteMaterial != null)
            {
                spriteRenderer.sharedMaterial = spriteMaterial;
            }

            return spriteRenderer;
        }

        static void DestroySafe(GameObject gameObject)
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }
    }
}
