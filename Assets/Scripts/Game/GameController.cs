using ShiftRegister.Core;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Game
{
    /// <summary>Loads a level from the catalog and shows it. Input and HUD hook in here in later steps.</summary>
    public sealed class GameController : MonoBehaviour
    {
        [SerializeField] LevelCatalog catalog;
        [SerializeField] BoardView boardView;
        [Tooltip("0-based chapter to load on start.")]
        [SerializeField] int startChapter = 1;
        [Tooltip("0-based level within the chapter.")]
        [SerializeField] int startLevel;

        public Board Board { get; private set; }
        public LevelData Level { get; private set; }

        void Start() => Load(startChapter, startLevel);

        public void Load(int chapter, int level)
        {
            var file = catalog.chapters[chapter].levels[level];
            Level = LevelJson.Parse(file);
            Board = new Board(Level);

            boardView.transform.localPosition = ScreenLayout.BoardCenter;
            boardView.Build(Board.Rules);
            boardView.Show(Board.Bits);
        }
    }
}
