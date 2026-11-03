using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ShiftRegister.Game.Input
{
    /// <summary>
    /// Keyboard and pointer controls. A cursor cell picks the row and column:
    /// arrows / WASD move it, Shift + arrow shifts the line part under it.
    /// The pointer moves the cursor (hover or tap) and presses the arrow buttons.
    /// Z / Backspace undoes, R resets, Enter / Space / N goes to the next level once solved.
    /// </summary>
    public sealed class BoardInput : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] BoardControls controls;
        [SerializeField] Camera cam;

        int row = 4, column = 3;
        int hovered = -1;
        Vector2 lastPointer = new Vector2(float.NaN, float.NaN);
        int enabledFrame = -1;

        public int CursorRow => row;
        public int CursorColumn => column;

        void OnEnable()
        {
            // The click that opened the level (e.g. on a menu row) must not also press the board.
            enabledFrame = Time.frameCount;
            hovered = -1;
            lastPointer = new Vector2(float.NaN, float.NaN);
        }

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

            bool next = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                || kb.spaceKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame;
            if (next && game.Board.IsSolved && game.NextLevel()) Sfx.Play(Sound.Click);

            // Testing shortcuts (editor and development builds): jump between levels without solving them.
            if (Debug.isDebugBuild)
            {
                if (kb.pageDownKey.wasPressedThisFrame) game.NextLevel();
                if (kb.pageUpKey.wasPressedThisFrame) game.PreviousLevel();
            }
        }

        void MoveCursor(Direction d)
        {
            int oldRow = row, oldColumn = column;
            switch (d)
            {
                case Direction.Left: column = Mathf.Max(0, column - 1); break;
                case Direction.Right: column = Mathf.Min(BoardBits.Size - 1, column + 1); break;
                case Direction.Up: row = Mathf.Max(0, row - 1); break;
                case Direction.Down: row = Mathf.Min(BoardBits.Size - 1, row + 1); break;
            }
            if (row != oldRow || column != oldColumn) Sfx.Play(Sound.Tick);
        }

        void HandlePointer()
        {
            var p = Pointer.current;
            if (p == null || cam == null)
            {
                hovered = -1;
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Time.frameCount == enabledFrame || overUi)
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
                else if (pressed) Sfx.Play(Sound.Blocked);
            }
            else if ((moved || pressed) && BoardControls.HitCell(local, out int r, out int c))
            {
                row = r;
                column = c;
            }
        }
    }
}
