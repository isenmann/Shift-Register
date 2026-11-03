using System;
using System.Collections.Generic;
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

        /// <summary>Raised when a move was asked for but cannot happen (locked line, wall, broken or solved board).</summary>
        public event Action Blocked;

        /// <summary>Raised after an undo that took a move back.</summary>
        public event Action Undone;

        /// <summary>Raised after a reset that undid at least one move.</summary>
        public event Action Restarted;

        /// <summary>One catalog level, parsed once at startup.</summary>
        public sealed class LevelEntry
        {
            public int Chapter;
            public int Index;
            /// <summary>Position in play order.</summary>
            public int Order;
            public LevelData Data;
        }

        readonly List<LevelEntry> levels = new List<LevelEntry>();

        /// <summary>Every catalog level in play order.</summary>
        public IReadOnlyList<LevelEntry> Levels => levels;

        /// <summary>True when testing a fixed level: skip the title screen and load it directly.</summary>
        public bool StartsInLevel => overrideStart;

        void Awake()
        {
            Progress = ProgressStore.Load();
            foreach (var (c, l, file) in catalog.AllLevels())
                if (file != null)
                    levels.Add(new LevelEntry { Chapter = c, Index = l, Order = levels.Count, Data = LevelJson.Parse(file) });
        }

        /// <summary>Loads the inspector's test level (see <see cref="StartsInLevel"/>).</summary>
        public void LoadStartLevel() => Load(startChapter, startLevel);

        public LevelEntry Entry(int chapter, int level) => levels.Find(e => e.Chapter == chapter && e.Index == level);

        public bool IsUnlocked(int chapter, int level)
        {
            var entry = Entry(chapter, level);
            if (entry == null) return false;
            var solved = new List<bool>(levels.Count);
            foreach (var e in levels) solved.Add(Progress.IsSolved(e.Data.id));
            return Unlocks.IsUnlocked(solved, entry.Order);
        }

        /// <summary>Where PLAY goes: the last level played, else the first unsolved open level, else the first.</summary>
        public (int chapter, int level) ResumeTarget()
        {
            var last = levels.Find(e => e.Data.id == Progress.lastLevelId);
            if (last != null) return (last.Chapter, last.Index);
            foreach (var e in levels)
                if (!Progress.IsSolved(e.Data.id) && IsUnlocked(e.Chapter, e.Index)) return (e.Chapter, e.Index);
            return levels.Count > 0 ? (levels[0].Chapter, levels[0].Index) : (0, 0);
        }

        public void Load(int chapter, int level)
        {
            var file = catalog.chapters[chapter].levels[level];
            previewing = false;
            ChapterIndex = chapter;
            LevelIndex = level;
            Show(LevelJson.Parse(file));

            if (Progress != null && Progress.lastLevelId != Level.id)
            {
                Progress.lastLevelId = Level.id;
                ProgressStore.Save(Progress);
            }
        }

        /// <summary>
        /// Plays a level that is not in the catalog (level design, visual checks). Keeps the current
        /// chapter for the HUD; solves are not saved.
        /// </summary>
        public void Preview(LevelData data)
        {
            previewing = true;
            Show(data);
        }

        bool previewing;

        void Show(LevelData data)
        {
            Level = data;
            Board = new Board(Level);
            LastResult = null;

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
            if (Board == null) return false;
            if (!Board.TryMove(move, out var outcome))
            {
                Blocked?.Invoke();
                return false;
            }

            if (Board.IsSolved)
            {
                var result = Progress != null && !previewing
                    ? Progress.Record(Level.id, Board.MoveCount, Level.par)
                    : new LevelResult(Board.MoveCount, Level.par, Scoring.Stars(Board.MoveCount, Level.par), true, 0);
                if (Progress != null && !previewing) ProgressStore.Save(Progress);
                LastResult = result;
                Solved?.Invoke(result);
            }

            boardView.Play(outcome, Board.IsSolved, Board.IsRuleBroken);
            Changed?.Invoke();
            Moved?.Invoke(outcome);
            return true;
        }

        /// <summary>Shifts the line part under a cursor cell; false on a wall cell or a locked line.</summary>
        public bool TryShiftAt(int row, int column, Direction direction)
        {
            if (Board == null) return false;
            if (InputMapping.TryCursorMove(Board.Rules, row, column, direction, out var move)) return TryShift(move);
            Blocked?.Invoke();
            return false;
        }

        public void Undo()
        {
            if (Board == null || !Board.Undo()) return;
            Refresh();
            Undone?.Invoke();
        }

        public void ResetLevel()
        {
            if (Board == null) return;
            bool moved = Board.MoveCount > 0;
            Board.Reset();
            Refresh();
            if (moved) Restarted?.Invoke();
        }

        void Refresh()
        {
            boardView.Show(Board.Bits);
            Changed?.Invoke();
        }
    }
}
