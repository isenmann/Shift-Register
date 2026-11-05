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
        enum Layer { Ground = 0, Ghost = 1, Bit = 2, Eyes = 3, Pin = 4 }

        sealed class Cell
        {
            public SpriteRenderer Ground;
            public SpriteRenderer Ghost;
            /// <summary>The bit body plus its eyes; moved as one by later animations.</summary>
            public GameObject Bit;
            /// <summary>The bit's left and right eye.</summary>
            public SpriteRenderer[] Eyes;
            /// <summary>The ^ ^ of a solved level, shown instead of the eyes.</summary>
            public SpriteRenderer[] HappyEyes;
            public readonly Face Face = new Face();
            public SpriteRenderer Pin;
        }

        // Shapes in mockup pixels (a cell is 46 px).
        const float CellPx = 46f, RadiusPx = 8f;
        const float EyeWidth = 6f, EyeHeight = 8f, EyeRadius = 2f, EyeX = 8f, EyeY = 5f;

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
            ClearMoveHint();
            ResetAnimationState();
            rules = levelRules;
            if (cellsRoot != null)
            {
                DestroySafe(cellsRoot.gameObject);
            }

            cellsRoot = new GameObject("Cells").transform;
            cellsRoot.SetParent(transform, false);

            var ground = ProceduralSprites.RoundedRect(CellPx, CellPx, RadiusPx);
            var wall = ProceduralSprites.Hatched(CellPx, RadiusPx, 4f, 2f, Lcd.Text3, Lcd.Card);
            var ghost = ProceduralSprites.RoundedOutline(CellPx, CellPx, RadiusPx, 2f, 5f);
            var pin = ProceduralSprites.RoundedOutline(CellPx - 6f, CellPx - 6f, RadiusPx - 3f, 3f);
            var eye = ProceduralSprites.RoundedRect(EyeWidth, EyeHeight, EyeRadius);
            var happyEye = ProceduralSprites.Chevron(HappyEyeSize, HappyEyeStroke);

            cells = new Cell[BoardBits.Size, BoardBits.Size];
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    var root = new GameObject($"Cell {row + 1},{column + 1}").transform;
                    root.SetParent(cellsRoot, false);
                    root.localPosition = CellCenter(row, column);

                    bool isWall = rules.IsWall(row, column);
                    var cell = new Cell
                    {
                        Ground = Renderer(root, "Ground", isWall ? wall : ground, isWall ? Color.white
                        : (Color)Lcd.Cell, Layer.Ground),
                        Ghost = Renderer(root, "Ghost", ghost, Lcd.Text3, Layer.Ghost)
                    };

                    var bit = Renderer(root, "Bit", ground, Lcd.Ink, Layer.Bit);
                    cell.Bit = bit.gameObject;
                    cell.Eyes = new[]
                    {
                        Renderer(bit.transform, "Eye L", eye, Lcd.Light, Layer.Eyes),
                        Renderer(bit.transform, "Eye R", eye, Lcd.Light, Layer.Eyes)
                    };
                    ApplyEyes(cell.Eyes, Vector2.zero, 0f);
                    cell.HappyEyes = new[]
                    {
                        Renderer(bit.transform, "Happy L", happyEye, Lcd.Light, Layer.Eyes),
                        Renderer(bit.transform, "Happy R", happyEye, Lcd.Light, Layer.Eyes)
                    };
                    cell.HappyEyes[0].transform.localPosition = new Vector2(-EyeX, EyeY) / CellPx;
                    cell.HappyEyes[1].transform.localPosition = new Vector2(EyeX, EyeY) / CellPx;
                    cell.HappyEyes[0].enabled = cell.HappyEyes[1].enabled = false;

                    cell.Pin = Renderer(root, "Pin", pin, Lcd.Light, Layer.Pin);
                    cell.Pin.enabled = rules.IsPinned(row, column);
                    cells[row, column] = cell;
                }
            }

            BuildOverlays();
        }

        /// <summary>
        /// Shows a board state at once (stopping any animation); empty target cells get a dashed ghost.
        /// </summary>
        public void Show(ulong bits)
        {
            StopAnimation();
            ClearFaces();
            settledBits = bits;
            ShowNow(bits, bits);
        }

        /// <summary>
        /// Static bits for <paramref name="bits"/>, ghosts computed from <paramref name="ghostsFrom"/>.
        /// </summary>
        void ShowNow(ulong bits, ulong ghostsFrom)
        {
            if (cells == null)
            {
                return;
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    var cell = cells[row, column];
                    cell.Bit.SetActive(BoardBits.GetCell(bits, row, column));
                    cell.Bit.transform.localPosition = Vector3.zero;
                    cell.Bit.transform.localScale = Vector3.one;
                    cell.Ghost.enabled = !BoardBits.GetCell(ghostsFrom, row, column)
                        && BoardBits.GetCell(rules.Target, row, column);
                }
            }

            UpdateOverlays(bits);
        }

        public bool IsBitShown(int row, int column) => cells[row, column].Bit.activeSelf;
        public bool IsGhostShown(int row, int column) => cells[row, column].Ghost.enabled;

        SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, Color color, Layer layer)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = (int)layer;
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
