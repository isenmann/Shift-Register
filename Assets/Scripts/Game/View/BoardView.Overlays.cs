using ShiftRegister.Core;
using TMPro;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// Chapter 6 overlays (layout A of the gate-symbol canvas): gate and 1-IN cells get a light ground, an
    /// inset dashed frame while empty and a corner tab holding the symbol: ink tab with a light symbol,
    /// flipped to a light tab with an ink symbol while a bit sits on the cell. 1-IN edge cells add an ink
    /// bar on the side bits enter from (light under a bit) and put their tab in the opposite corner.
    /// </summary>
    public sealed partial class BoardView
    {
        sealed class Overlay
        {
            public SpriteRenderer Frame;
            public SpriteRenderer Bar;
            /// <summary>The corner tab behind the symbol.</summary>
            public SpriteRenderer Tab;
            public TextMeshPro Symbol;
            /// <summary>The symbol without rich-text tags.</summary>
            public string SymbolText;
        }

        const int OverlayFrameOrder = 5, OverlaySymbolOrder = 6;
        /// <summary>Gate tab in mockup pixels: sits in the cell's bottom-right corner, below the bit's eyes.</summary>
        const float TabWidth = 21f, TabHeight = 19f, TabRadius = 6.5f;

        [Tooltip("Font for gate symbols (JetBrains Mono).")]
        [SerializeField] TMP_FontAsset symbolFont;

        Overlay[,] overlays;

        public static string GateSymbol(GateOp op) => op switch
        {
            GateOp.And => "&",
            GateOp.Or => "|",
            GateOp.Xor => "^",
            _ => "~"
        };

        /// <summary>True if the cell shows a gate or 1-IN overlay (for tests).</summary>
        public bool HasOverlay(int row, int column) => overlays?[row, column] != null;

        public string OverlaySymbol(int row, int column) => overlays?[row, column]?.SymbolText;

        void BuildOverlays()
        {
            overlays = new Overlay[BoardBits.Size, BoardBits.Size];
            var data = rules.Data;

            foreach (var gate in data.gates ?? System.Array.Empty<RowGate>())
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if ((gate.mask & (0x80 >> column)) != 0 && overlays[gate.row, column] == null)
                    {
                        overlays[gate.row, column] = CreateOverlay(gate.row, column, GateSymbol(gate.op), 0);
                    }
                }
            }

            foreach (int row in data.fillLeftRows ?? System.Array.Empty<int>())
            {
                if (overlays[row, 0] == null)
                {
                    overlays[row, 0] = CreateOverlay(row, 0, "1›", -1);
                }
            }

            foreach (int row in data.fillRightRows ?? System.Array.Empty<int>())
            {
                if (overlays[row, BoardBits.Size - 1] == null)
                {
                    overlays[row, BoardBits.Size - 1] = CreateOverlay(row, BoardBits.Size - 1, "‹1", 1);
                }
            }
        }

        /// <param name="barSide">-1 bar on the left edge, 1 on the right, 0 none (a gate). The tab sits in
        /// the bottom corner away from the bar.</param>
        Overlay CreateOverlay(int row, int column, string symbol, int barSide)
        {
            var root = cellsRoot.Find($"Cell {row + 1},{column + 1}");
            cells[row, column].Ground.color = Lcd.Light;
            var overlay = new Overlay
            {
                Frame = Renderer(root, "Gate Frame", ProceduralSprites.RoundedOutline(38f, 38f, 5f, 2f, 4f), Lcd.Ink,
                Layer.Bit)
            };
            overlay.Frame.sortingOrder = OverlayFrameOrder;

            if (barSide != 0)
            {
                overlay.Bar = Renderer(root, "Edge Bar", ProceduralSprites.RoundedRect(4f, 30f, 2f), Lcd.Ink,
                    Layer.Bit);
                overlay.Bar.sortingOrder = OverlayFrameOrder;
                overlay.Bar.transform.localPosition = new Vector3(barSide * 20f / CellPx, 0f, 0f);
            }

            float side = barSide == 1 ? -1f : 1f;
            overlay.Tab = Renderer(root, "Gate Tab",
                ProceduralSprites.CornerTab(TabWidth, TabHeight, TabRadius, RadiusPx), Lcd.Ink, Layer.Bit);
            overlay.Tab.sortingOrder = OverlayFrameOrder;
            overlay.Tab.flipX = side < 0f;
            var centre = new Vector3(side * (CellPx - TabWidth) * 0.5f / CellPx, -(CellPx - TabHeight) * 0.5f / CellPx,
                0f);
            overlay.Tab.transform.localPosition = centre;

            // Centred on the glyph's own shape, then tuned per symbol in mockup pixels: the flat '^' and
            // '~' are bigger to read as heavy as '&', '|' is smaller to keep clear of the tab's edges, and
            // '^' drops a little because its mesh centre sits above its visible middle.
            float size = 3.9f, drop = 0f;
            if (symbol == "~")
            {
                size = 5.4f;
            }
            else if (symbol == "^")
            {
                size = 4.8f;
                drop = 1.5f;
            }
            else if (symbol == "|")
            {
                size = 3.3f;
            }
            else if (symbol.Length > 1)
            {
                size = 3.2f; // "1›" / "‹1" need two glyphs' width
            }

            var label = CreateSymbol(root, symbol, size, TextAlignmentOptions.CenterGeoAligned);
            label.rectTransform.sizeDelta = new Vector2(TabWidth, TabHeight) / CellPx;
            label.transform.localPosition = centre + new Vector3(0f, -drop / CellPx, 0f);
            overlay.Symbol = label;
            overlay.SymbolText = symbol;
            return overlay;
        }

        TextMeshPro CreateSymbol(Transform root, string text, float size, TextAlignmentOptions alignment)
        {
            var gameObject = new GameObject("Gate Symbol");
            gameObject.transform.SetParent(root, false);
            var label = gameObject.AddComponent<TextMeshPro>();
            if (symbolFont != null)
            {
                label.font = symbolFont;
            }

            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.GetComponent<MeshRenderer>().sortingOrder = OverlaySymbolOrder;
            return label;
        }

        void UpdateOverlays(ulong bits)
        {
            if (overlays == null)
            {
                return;
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    var overlay = overlays[row, column];
                    if (overlay == null)
                    {
                        continue;
                    }

                    bool bit = BoardBits.GetCell(bits, row, column);
                    Color ink = bit ? Lcd.Light : Lcd.Ink;
                    // The frame marks only empty cells; the tab flips so it reads on a bit too.
                    overlay.Frame.enabled = !bit;
                    overlay.Tab.color = ink;
                    if (overlay.Bar != null)
                    {
                        overlay.Bar.color = ink;
                    }

                    overlay.Symbol.color = bit ? Lcd.Ink : Lcd.Light;
                }
            }
        }
    }
}
