using ShiftRegister.Core;
using ShiftRegister.Game.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShiftRegister.Game.Input
{
    /// <summary>
    /// Keyboard and pointer controls. A cursor cell picks the row and column:
    /// arrows / WASD move it, Shift + arrow shifts the line part under it.
    /// The pointer moves the cursor (hover or tap) and presses the arrow buttons.
    /// Z / Backspace undoes, R resets.
    /// </summary>
    public sealed class BoardInput : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] BoardControls controls;
        [SerializeField] Camera cam;

        int row = 4, column = 3;
        int hovered = -1;
        Vector2 lastPointer = new Vector2(float.NaN, float.NaN);

        public int CursorRow => row;
        public int CursorColumn => column;

        void Update()
        {
            if (game == null || game.Board == null) return;
            HandlePointer();
            HandleKeyboard();
            controls.Highlight(hovered, row, column);
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            Direction? d = null;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) d = Direction.Left;
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) d = Direction.Right;
            else if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) d = Direction.Up;
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) d = Direction.Down;

            if (d.HasValue)
            {
                if (kb.shiftKey.isPressed) game.TryShiftAt(row, column, d.Value);
                else MoveCursor(d.Value);
            }

            if (kb.zKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) game.Undo();
            if (kb.rKey.wasPressedThisFrame) game.ResetLevel();
        }

        void MoveCursor(Direction d)
        {
            switch (d)
            {
                case Direction.Left: column = Mathf.Max(0, column - 1); break;
                case Direction.Right: column = Mathf.Min(BoardBits.Size - 1, column + 1); break;
                case Direction.Up: row = Mathf.Max(0, row - 1); break;
                case Direction.Down: row = Mathf.Min(BoardBits.Size - 1, row + 1); break;
            }
        }

        void HandlePointer()
        {
            var p = Pointer.current;
            if (p == null || cam == null)
            {
                hovered = -1;
                return;
            }

            Vector2 screen = p.position.ReadValue();
            bool moved = screen != lastPointer;
            lastPointer = screen;
            bool pressed = p.press.wasPressedThisFrame;

            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            Vector2 local = controls.transform.InverseTransformPoint(world);
            hovered = controls.HitButton(local);

            if (hovered >= 0)
            {
                var b = controls.Buttons[hovered];
                if (pressed && b.Enabled) game.TryShift(b.Move);
            }
            else if ((moved || pressed) && BoardControls.HitCell(local, out int r, out int c))
            {
                row = r;
                column = c;
            }
        }
    }
}
