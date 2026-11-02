using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Game
{
    /// <summary>Owns the level being played: loads it, applies moves, undo and reset, and refreshes the view.</summary>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] LevelCatalog catalog;
        [SerializeField] BoardView boardView;
        [SerializeField] BoardControls controls;
        [Tooltip("0-based chapter to load on start.")]
        [SerializeField] int startChapter = 1;
        [Tooltip("0-based level within the chapter.")]
        [SerializeField] int startLevel;

        public Board Board { get; private set; }
        public LevelData Level { get; private set; }
        public int ChapterIndex { get; private set; }
        public LevelCatalog.Chapter Chapter => catalog.chapters[ChapterIndex];

        /// <summary>Raised after every board change: a move, undo, reset or a new level.</summary>
        public event Action Changed;

        /// <summary>Raised after a successful move, with what happened.</summary>
        public event Action<MoveOutcome> Moved;

        void Start() => Load(startChapter, startLevel);

        public void Load(int chapter, int level)
        {
            var file = catalog.chapters[chapter].levels[level];
            ChapterIndex = chapter;
            Level = LevelJson.Parse(file);
            Board = new Board(Level);

            boardView.transform.localPosition = ScreenLayout.BoardCenter;
            boardView.Build(Board.Rules);
            if (controls != null) controls.Build(Board.Rules);
            Refresh();
        }

        public bool TryShift(Move move)
        {
            if (Board == null || !Board.TryMove(move, out var outcome)) return false;
            Refresh();
            Moved?.Invoke(outcome);
            if (Board.IsSolved) Debug.Log($"{Level.id} solved in {Board.MoveCount} moves (par {Level.par}).");
            else if (Board.IsRuleBroken) Debug.Log($"{Level.id}: rule broken, {Board.Lost} bits lost. Undo to continue.");
            return true;
        }

        /// <summary>Shifts the line part under a cursor cell; false on a wall cell or a locked line.</summary>
        public bool TryShiftAt(int row, int column, Direction direction) =>
            Board != null && InputMapping.TryCursorMove(Board.Rules, row, column, direction, out var move) && TryShift(move);

        public void Undo()
        {
            if (Board != null && Board.Undo()) Refresh();
        }

        public void ResetLevel()
        {
            if (Board == null) return;
            Board.Reset();
            Refresh();
        }

        void Refresh()
        {
            boardView.Show(Board.Bits);
            Changed?.Invoke();
        }
    }
}
