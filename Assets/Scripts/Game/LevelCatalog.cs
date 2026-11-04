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

        /// <summary>The level after (chapter, level) in play order, skipping empty chapters.</summary>
        public bool TryNext(int chapter, int level, out int nextChapter, out int nextLevel)
        {
            nextChapter = chapter;
            nextLevel = level + 1;
            while (nextChapter < chapters.Count)
            {
                if (nextLevel < chapters[nextChapter].levels.Count)
                {
                    return true;
                }

                nextChapter++;
                nextLevel = 0;
            }

            return false;
        }

        /// <summary>The level before (chapter, level) in play order, skipping empty chapters.</summary>
        public bool TryPrevious(int chapter, int level, out int previousChapter, out int previousLevel)
        {
            previousChapter = chapter;
            previousLevel = level - 1;
            while (previousChapter >= 0)
            {
                if (previousLevel >= 0 && previousLevel < chapters[previousChapter].levels.Count)
                {
                    return true;
                }

                previousChapter--;
                if (previousChapter >= 0)
                {
                    previousLevel = chapters[previousChapter].levels.Count - 1;
                }
            }

            return false;
        }

        /// <summary>The first level in play order.</summary>
        public bool TryFirst(out int chapter, out int level) => TryNext(0, -1, out chapter, out level);

        /// <summary>Finds a level by its id (parses each level file; the catalog is small).</summary>
        public bool TryFind(string id, out int chapter, out int level)
        {
            foreach (var (c, l, file) in AllLevels())
            {
                if (file != null && LevelJson.Parse(file).id == id)
                {
                    chapter = c;
                    level = l;
                    return true;
                }
            }

            chapter = level = -1;
            return false;
        }

        /// <summary>Every level file with its 0-based chapter and level index.</summary>
        public IEnumerable<(int chapter, int level, TextAsset file)> AllLevels()
        {
            for (int c = 0; c < chapters.Count; c++)
            {
                var levels = chapters[c].levels;
                for (int l = 0; l < levels.Count; l++)
                {
                    yield return (c, l, levels[l]);
                }
            }
        }
    }
}
