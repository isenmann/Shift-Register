using ShiftRegister.Core;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// Draws the 8×8 board: empty cells, walls, target ghosts, bits with eyes, pins, and (in
    /// BoardView.Overlays.cs) gate cells and 1-IN edges.
    /// The board's local origin is its centre; one cell is one unit.
    /// </summary>
    public sealed partial class BoardView : MonoBehaviour
    {
        // Shapes in mockup pixels (a cell is 46 px).
        const float CellPx = 46f, RadiusPx = 8f;
        const float EyeW = 6f, EyeH = 8f, EyeRadius = 2f, EyeX = 8f, EyeY = 5f;

        enum Layer { Ground = 0, Ghost = 1, Bit = 2, Eyes = 3, Pin = 4 }

        sealed class Cell
        {
            public SpriteRenderer Ground;
            public SpriteRenderer Ghost;
            /// <summary>The bit body plus its eyes; moved as one by later animations.</summary>
            public GameObject Bit;
            public SpriteRenderer Pin;
        }

        [Tooltip("Unlit sprite material, so the palette shows exactly. Empty = the renderer's default.")]
        [SerializeField] Material spriteMaterial;

        Cell[,] cells;
        Transform cellsRoot;
        LevelRules rules;

        public LevelRules Rules => rules;

        /// <summary>Centre of a cell in the board's local space (row 0 at the top).</summary>
        public static Vector2 CellCenter(int row, int column) =>
            new Vector2((column - 3.5f) * ScreenLayout.Pitch, (3.5f - row) * ScreenLayout.Pitch);

        public void Build(LevelRules levelRules)
        {
            ResetAnimationState();
            rules = levelRules;
            if (cellsRoot != null) DestroySafe(cellsRoot.gameObject);
            cellsRoot = new GameObject("Cells").transform;
            cellsRoot.SetParent(transform, false);

            var ground = ProceduralSprites.RoundedRect(CellPx, CellPx, RadiusPx);
            var wall = ProceduralSprites.Hatched(CellPx, RadiusPx, 4f, 2f, Lcd.Text3, Lcd.Card);
            var ghost = ProceduralSprites.RoundedOutline(CellPx, CellPx, RadiusPx, 2f, 5f);
            var pin = ProceduralSprites.RoundedOutline(CellPx - 6f, CellPx - 6f, RadiusPx - 3f, 3f);
            var eye = ProceduralSprites.RoundedRect(EyeW, EyeH, EyeRadius);

            cells = new Cell[BoardBits.Size, BoardBits.Size];
            for (int r = 0; r < BoardBits.Size; r++)
            for (int c = 0; c < BoardBits.Size; c++)
            {
                var root = new GameObject($"Cell {r + 1},{c + 1}").transform;
                root.SetParent(cellsRoot, false);
                root.localPosition = CellCenter(r, c);

                bool isWall = rules.IsWall(r, c);
                var cell = new Cell
                {
                    Ground = Renderer(root, "Ground", isWall ? wall : ground, isWall ? Color.white : (Color)Lcd.Cell, Layer.Ground),
                    Ghost = Renderer(root, "Ghost", ghost, Lcd.Text3, Layer.Ghost)
                };

                var bit = Renderer(root, "Bit", ground, Lcd.Ink, Layer.Bit);
                cell.Bit = bit.gameObject;
                Renderer(bit.transform, "Eye L", eye, Lcd.Light, Layer.Eyes).transform.localPosition = new Vector2(-EyeX, EyeY) / CellPx;
                Renderer(bit.transform, "Eye R", eye, Lcd.Light, Layer.Eyes).transform.localPosition = new Vector2(EyeX, EyeY) / CellPx;

                cell.Pin = Renderer(root, "Pin", pin, Lcd.Light, Layer.Pin);
                cell.Pin.enabled = rules.IsPinned(r, c);
                cells[r, c] = cell;
            }
            BuildOverlays();
        }

        /// <summary>Shows a board state at once (stopping any animation); empty target cells get a dashed ghost.</summary>
        public void Show(ulong bits)
        {
            StopAnimation();
            settledBits = bits;
            ShowNow(bits, bits);
        }

        /// <summary>Static bits for <paramref name="bits"/>, ghosts computed from <paramref name="ghostsFrom"/>.</summary>
        void ShowNow(ulong bits, ulong ghostsFrom)
        {
            if (cells == null) return;
            for (int r = 0; r < BoardBits.Size; r++)
            for (int c = 0; c < BoardBits.Size; c++)
            {
                var cell = cells[r, c];
                cell.Bit.SetActive(BoardBits.GetCell(bits, r, c));
                cell.Bit.transform.localPosition = Vector3.zero;
                cell.Bit.transform.localScale = Vector3.one;
                cell.Ghost.enabled = !BoardBits.GetCell(ghostsFrom, r, c) && BoardBits.GetCell(rules.Target, r, c);
            }
            UpdateOverlays(bits);
        }

        public bool IsBitShown(int row, int column) => cells[row, column].Bit.activeSelf;
        public bool IsGhostShown(int row, int column) => cells[row, column].Ghost.enabled;

        SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, Color color, Layer layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = (int)layer;
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
