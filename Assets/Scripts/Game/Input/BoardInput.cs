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
    /// The pointer moves the cursor (hover or tap) and presses the arrow buttons; in mobile mode
    /// (<see cref="DeviceMode"/>) dragging a row or column shifts it (<see cref="SwipeTracker"/>).
    /// Z / Backspace undoes, R resets, T asks for a hint, Enter / Space / N goes to the next level once solved.
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
        // First shift after 40 % of a cell, then one per cell dragged.
        readonly SwipeTracker swipe = new SwipeTracker(ScreenLayout.Pitch, ScreenLayout.Pitch * 0.4f);

        public int CursorRow => row;
        public int CursorColumn => column;

        void OnEnable()
        {
            // The click that opened the level (e.g. on a menu row) must not also press the board.
            enabledFrame = Time.frameCount;
            hovered = -1;
            lastPointer = new Vector2(float.NaN, float.NaN);
            swipe.End();
            SetIdleLife(true);
        }

        void OnDisable() => SetIdleLife(false);

        /// <summary>Bits blink and nap only while this script takes input: not in menus, the editor or help.</summary>
        void SetIdleLife(bool on)
        {
            if (game != null && game.BoardView != null)
            {
                game.BoardView.IdleLife = on;
            }
        }

        void Update()
        {
            if (game == null || game.Board == null)
            {
                return;
            }

            int oldRow = row, oldColumn = column, oldHovered = hovered;
            HandlePointer();
            HandleKeyboard();
            controls.Highlight(hovered, row, column);

            if (row != oldRow || column != oldColumn || hovered != oldHovered || IsAnyKeyOrPressThisFrame())
            {
                game.BoardView.WakeUp();
            }

            // Hovering an arrow outlines that line only (see BoardControls.Highlight), so only it counts then.
            game.BoardView.SetPeek(game.Board, hovered >= 0 ? -1 : row, column, AimedMove());
        }

        /// <summary>The move an arrow under the pointer would make, or the next one of a swipe under way.</summary>
        Move? AimedMove()
        {
            if (swipe.Active && swipe.LastDirection.HasValue)
            {
                return InputMapping.TryCursorMove(game.Board.Rules, swipe.Row, swipe.Column,
                    swipe.LastDirection.Value, out var next) ? next : (Move?)null;
            }

            if (hovered >= 0 && hovered < controls.Buttons.Count && controls.Buttons[hovered].Enabled)
            {
                return controls.Buttons[hovered].Move;
            }

            return null;
        }

        /// <summary>Tiny pointer jitter doesn't count as input; a new cell, a key or a press does.</summary>
        static bool IsAnyKeyOrPressThisFrame()
        {
            var keyboard = Keyboard.current;
            var pointer = Pointer.current;
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                || (pointer != null && pointer.press.wasPressedThisFrame);
        }

        void HandleKeyboard()
        {
            var keyboard = Keyboard.current;
            // The key that closed the help panel or opened the level must not also act on the board.
            if (keyboard == null || Time.frameCount == enabledFrame)
            {
                return;
            }

            Direction? direction = null;
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                direction = Direction.Left;
            }
            else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                direction = Direction.Right;
            }
            else if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
            {
                direction = Direction.Up;
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                direction = Direction.Down;
            }

            if (direction.HasValue)
            {
                if (keyboard.shiftKey.isPressed)
                {
                    game.TryShiftAt(row, column, direction.Value);
                }
                else
                {
                    MoveCursor(direction.Value);
                }
            }

            if (keyboard.zKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
            {
                game.Undo();
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                game.ResetLevel();
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                game.RequestHint();
            }

            bool next = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                || keyboard.spaceKey.wasPressedThisFrame || keyboard.nKey.wasPressedThisFrame;
            if (next && game.Board.IsSolved && game.NextLevel())
            {
                Sfx.Play(Sound.Click);
            }

            if (keyboard.kKey.wasPressedThisFrame && game.IsRandom && game.Board.IsSolved && game.SaveRandom())
            {
                Sfx.Play(Sound.Click);
            }

            // Testing shortcuts (editor and development builds): jump between levels without solving them.
            if (Debug.isDebugBuild)
            {
                if (keyboard.pageDownKey.wasPressedThisFrame)
                {
                    game.NextLevel();
                }

                if (keyboard.pageUpKey.wasPressedThisFrame)
                {
                    game.PreviousLevel();
                }
            }
        }

        void MoveCursor(Direction direction)
        {
            int oldRow = row, oldColumn = column;
            switch (direction)
            {
                case Direction.Left:
                    column = Mathf.Max(0, column - 1);
                    break;
                case Direction.Right:
                    column = Mathf.Min(BoardBits.Size - 1, column + 1);
                    break;
                case Direction.Up:
                    row = Mathf.Max(0, row - 1);
                    break;
                case Direction.Down:
                    row = Mathf.Min(BoardBits.Size - 1, row + 1);
                    break;
            }

            if (row != oldRow || column != oldColumn)
            {
                Sfx.Play(Sound.Tick);
            }
        }

        void HandlePointer()
        {
            var pointer = Pointer.current;
            if (pointer == null || cam == null)
            {
                hovered = -1;
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Feed(pointer.position.ReadValue(), pointer.press.isPressed, pointer.press.wasPressedThisFrame, overUi);
        }

        /// <summary>
        /// One frame of pointer input: hover, taps on cells and arrow buttons, and (mobile) swipes.
        /// Public so play-mode checks can drive it without input devices.
        /// </summary>
        public void Feed(Vector2 screen, bool down, bool justPressed, bool overUi)
        {
            if (cam == null)
            {
                return;
            }

            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            Vector2 local = controls.transform.InverseTransformPoint(world);

            // A swipe keeps going even if the finger crosses a HUD element.
            if (swipe.Active)
            {
                if (!down)
                {
                    swipe.End();
                    return;
                }

                Direction? direction;
                while ((direction = swipe.Step(local)).HasValue)
                {
                    game.TryShiftAt(swipe.Row, swipe.Column, direction.Value);
                }

                return;
            }

            if (Time.frameCount == enabledFrame || overUi)
            {
                hovered = -1;
                return;
            }

            bool moved = screen != lastPointer;
            lastPointer = screen;
            bool pressed = justPressed;
            hovered = controls.HitButton(local);

            if (hovered >= 0)
            {
                var button = controls.Buttons[hovered];
                if (pressed && button.Enabled)
                {
                    game.TryShift(button.Move);
                }
                else if (pressed)
                {
                    Sfx.Play(Sound.Blocked);
                }
            }
            else if ((moved || pressed) && BoardControls.HitCell(local, out int hitRow, out int hitColumn))
            {
                row = hitRow;
                column = hitColumn;
                if (pressed && DeviceMode.IsMobile)
                {
                    swipe.Begin(local, hitRow, hitColumn);
                }
            }
        }
    }
}
