using ShiftRegister.Core;
using TMPro;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// Chapter 6 overlays, after the Logic-level mockup: gate cells get a light ground, an inset dashed
    /// frame and the op symbol in the corner; 1-IN edge cells get the same plus an ink bar on the side
    /// bits enter from. Frame and symbol turn light while a bit sits on the cell.
    /// </summary>
    public sealed partial class BoardView
    {
        [Tooltip("Font for gate symbols (JetBrains Mono).")]
        [SerializeField] TMP_FontAsset symbolFont;

        const int OverlayFrameOrder = 5, OverlaySymbolOrder = 6;

        sealed class Overlay
        {
            public SpriteRenderer Frame;
            public SpriteRenderer Bar;
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
            _ => "¬"
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

        /// <param name="barSide">-1 bar on the left edge, 1 on the right, 0 none.</param>
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

            var go = new GameObject("Gate Symbol");
            go.transform.SetParent(root, false);
            var t = go.AddComponent<TextMeshPro>();
            if (symbolFont != null) t.font = symbolFont;
            // '^' sits high in its glyph box; drop it so it stays clear of a bit's eyes.
            t.text = symbol == "^" ? "<voffset=-0.35em>^</voffset>" : symbol;
            t.fontSize = 3.4f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.BottomRight;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(0.6f, 0.62f);
            t.GetComponent<MeshRenderer>().sortingOrder = OverlaySymbolOrder;
            o.Symbol = t;
            o.Raw = symbol;
            return o;
        }

        void UpdateOverlays(ulong bits)
        {
            if (overlays == null) return;
            for (int r = 0; r < BoardBits.Size; r++)
            for (int c = 0; c < BoardBits.Size; c++)
            {
                var o = overlays[r, c];
                if (o == null) continue;
                Color ink = BoardBits.GetCell(bits, r, c) ? Lcd.Light : Lcd.Ink;
                o.Frame.color = ink;
                if (o.Bar != null) o.Bar.color = ink;
                o.Symbol.color = ink;
            }
        }
    }
}
