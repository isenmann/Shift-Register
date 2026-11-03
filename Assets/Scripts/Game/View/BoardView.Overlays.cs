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
        [Tooltip("Font for gate symbols (JetBrains Mono).")]
        [SerializeField] TMP_FontAsset symbolFont;

        const int OverlayFrameOrder = 5, OverlaySymbolOrder = 6;
        /// <summary>Gate tab in mockup pixels: sits in the cell's bottom-right corner, below the bit's eyes.</summary>
        const float TabW = 21f, TabH = 19f, TabRadius = 6.5f;

        sealed class Overlay
        {
            public SpriteRenderer Frame;
            public SpriteRenderer Bar;
            /// <summary>The corner tab behind the symbol.</summary>
            public SpriteRenderer Tab;
            public TextMeshPro Symbol;
            /// <summary>The symbol without rich-text tags.</summary>
            public string Raw;
        }

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

        public string OverlaySymbol(int row, int column) => overlays?[row, column]?.Raw;

        void BuildOverlays()
        {
            overlays = new Overlay[BoardBits.Size, BoardBits.Size];
            var data = rules.Data;

            foreach (var g in data.gates ?? System.Array.Empty<RowGate>())
                for (int c = 0; c < BoardBits.Size; c++)
                    if ((g.mask & (0x80 >> c)) != 0 && overlays[g.row, c] == null)
                        overlays[g.row, c] = CreateOverlay(g.row, c, GateSymbol(g.op), 0);

            foreach (int r in data.fillLeftRows ?? System.Array.Empty<int>())
                if (overlays[r, 0] == null) overlays[r, 0] = CreateOverlay(r, 0, "1›", -1);
            foreach (int r in data.fillRightRows ?? System.Array.Empty<int>())
                if (overlays[r, BoardBits.Size - 1] == null) overlays[r, BoardBits.Size - 1] = CreateOverlay(r, BoardBits.Size - 1, "‹1", 1);
        }

        /// <param name="barSide">-1 bar on the left edge, 1 on the right, 0 none (a gate). The tab sits in
        /// the bottom corner away from the bar.</param>
        Overlay CreateOverlay(int r, int c, string symbol, int barSide)
        {
            var root = cellsRoot.Find($"Cell {r + 1},{c + 1}");
            cells[r, c].Ground.color = Lcd.Light;
            var o = new Overlay
            {
                Frame = Renderer(root, "Gate Frame", ProceduralSprites.RoundedOutline(38f, 38f, 5f, 2f, 4f), Lcd.Ink, Layer.Bit)
            };
            o.Frame.sortingOrder = OverlayFrameOrder;

            if (barSide != 0)
            {
                o.Bar = Renderer(root, "Edge Bar", ProceduralSprites.RoundedRect(4f, 30f, 2f), Lcd.Ink, Layer.Bit);
                o.Bar.sortingOrder = OverlayFrameOrder;
                o.Bar.transform.localPosition = new Vector3(barSide * 20f / CellPx, 0f, 0f);
            }

            float side = barSide == 1 ? -1f : 1f;
            o.Tab = Renderer(root, "Gate Tab", ProceduralSprites.CornerTab(TabW, TabH, TabRadius, RadiusPx), Lcd.Ink, Layer.Bit);
            o.Tab.sortingOrder = OverlayFrameOrder;
            o.Tab.flipX = side < 0f;
            var centre = new Vector3(side * (CellPx - TabW) * 0.5f / CellPx, -(CellPx - TabH) * 0.5f / CellPx, 0f);
            o.Tab.transform.localPosition = centre;

            // Centred on the glyph's own shape, then tuned per symbol in mockup pixels: the flat '^' and
            // '~' are bigger to read as heavy as '&', '|' is smaller to keep clear of the tab's edges, and
            // '^' drops a little because its mesh centre sits above its visible middle.
            float size = 3.9f, drop = 0f;
            if (symbol == "~") size = 5.4f;
            else if (symbol == "^") { size = 4.8f; drop = 1.5f; }
            else if (symbol == "|") size = 3.3f;
            else if (symbol.Length > 1) size = 3.2f; // "1›" / "‹1" need two glyphs' width
            var t = CreateSymbol(root, symbol, size, TextAlignmentOptions.CenterGeoAligned);
            t.rectTransform.sizeDelta = new Vector2(TabW, TabH) / CellPx;
            t.transform.localPosition = centre + new Vector3(0f, -drop / CellPx, 0f);
            o.Symbol = t;
            o.Raw = symbol;
            return o;
        }

        TextMeshPro CreateSymbol(Transform root, string text, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject("Gate Symbol");
            go.transform.SetParent(root, false);
            var t = go.AddComponent<TextMeshPro>();
            if (symbolFont != null) t.font = symbolFont;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = alignment;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.GetComponent<MeshRenderer>().sortingOrder = OverlaySymbolOrder;
            return t;
        }

        void UpdateOverlays(ulong bits)
        {
            if (overlays == null) return;
            for (int r = 0; r < BoardBits.Size; r++)
            for (int c = 0; c < BoardBits.Size; c++)
            {
                var o = overlays[r, c];
                if (o == null) continue;
                bool bit = BoardBits.GetCell(bits, r, c);
                Color ink = bit ? Lcd.Light : Lcd.Ink;
                // The frame marks only empty cells; the tab flips so it reads on a bit too.
                o.Frame.enabled = !bit;
                o.Tab.color = ink;
                if (o.Bar != null) o.Bar.color = ink;
                o.Symbol.color = bit ? Lcd.Ink : Lcd.Light;
            }
        }
    }
}
