using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftRegister.Game
{
    /// <summary>The game's chapters in play order, each listing its level JSON files in play order.</summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "Shift Register/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Chapter
        {
            public string name = "";
            [TextArea] public string description = "";
            public List<TextAsset> levels = new List<TextAsset>();
        }

        public List<Chapter> chapters = new List<Chapter>();

        /// <summary>Every level file with its 0-based chapter and level index.</summary>
        public IEnumerable<(int chapter, int level, TextAsset file)> AllLevels()
        {
            for (int c = 0; c < chapters.Count; c++)
            {
                var levels = chapters[c].levels;
                for (int l = 0; l < levels.Count; l++)
                    yield return (c, l, levels[l]);
            }
        }
    }
}
