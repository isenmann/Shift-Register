using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.Progress;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Game
{
    /// <summary>
    /// Owns the level being played: loads it, applies moves, undo and reset, records solves in the
    /// saved progress, and moves on to the next level.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] LevelCatalog catalog;
        [SerializeField] BoardView boardView;
        [SerializeField] BoardControls controls;
        [Tooltip("Start at the level below instead of resuming the last level played (for testing).")]
        [SerializeField] bool overrideStart;
        [Tooltip("0-based chapter to load when overriding the start.")]
        [SerializeField] int startChapter = 1;
        [Tooltip("0-based level within the chapter.")]
        [SerializeField] int startLevel;

        public Board Board { get; private set; }
        public LevelData Level { get; private set; }
        public int ChapterIndex { get; private set; }
        public int LevelIndex { get; private set; }
        public LevelCatalog Catalog => catalog;
        public LevelCatalog.Chapter Chapter => catalog.chapters[ChapterIndex];
        public BoardView BoardView => boardView;
        public PlayerProgress Progress { get; private set; }

        /// <summary>The result of the latest solve of the current level, if it is solved.</summary>
        public LevelResult? LastResult { get; private set; }

        public bool HasNextLevel => catalog.TryNext(ChapterIndex, LevelIndex, out _, out _);

        /// <summary>Raised after every board change: a move, undo, reset or a new level.</summary>
        public event Action Changed;

        /// <summary>Raised after a successful move, with what happened.</summary>
        public event Action<MoveOutcome> Moved;

        /// <summary>Raised when a move solves the level (already recorded in <see cref="Progress"/>).</summary>
        public event Action<LevelResult> Solved;

        void Start()
        {
            Progress = ProgressStore.Load();
            int chapter, level;
            if (overrideStart) (chapter, level) = (startChapter, startLevel);
            else if (!catalog.TryFind(Progress.lastLevelId, out chapter, out level)) catalog.TryFirst(out chapter, out level);
            Load(chapter, level);
        }

        public void Load(int chapter, int level)
        {
            var file = catalog.chapters[chapter].levels[level];
            ChapterIndex = chapter;
            LevelIndex = level;
            Level = LevelJson.Parse(file);
            Board = new Board(Level);
            LastResult = null;

            if (Progress != null && Progress.lastLevelId != Level.id)
            {
                Progress.lastLevelId = Level.id;
                ProgressStore.Save(Progress);
            }

            boardView.transform.localPosition = ScreenLayout.BoardCenter;
            boardView.Build(Board.Rules);
            if (controls != null) controls.Build(Board.Rules);
            Refresh();
        }

        public bool NextLevel()
        {
            if (!catalog.TryNext(ChapterIndex, LevelIndex, out int c, out int l)) return false;
            Load(c, l);
            return true;
        }

        public bool PreviousLevel()
        {
            if (!catalog.TryPrevious(ChapterIndex, LevelIndex, out int c, out int l)) return false;
            Load(c, l);
            return true;
        }

        public bool TryShift(Move move)
        {
            if (Board == null || !Board.TryMove(move, out var outcome)) return false;

            if (Board.IsSolved)
            {
                var result = Progress != null
                    ? Progress.Record(Level.id, Board.MoveCount, Level.par)
                    : new LevelResult(Board.MoveCount, Level.par, Scoring.Stars(Board.MoveCount, Level.par), true, 0);
                if (Progress != null) ProgressStore.Save(Progress);
                LastResult = result;
                Solved?.Invoke(result);
            }

            boardView.Play(outcome, Board.IsSolved, Board.IsRuleBroken);
            Changed?.Invoke();
            Moved?.Invoke(outcome);
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
