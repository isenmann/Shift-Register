using ShiftRegister.Core;
using UnityEngine;

namespace ShiftRegister.Game
{
    /// <summary>Reads level JSON files (see Assets/Levels/README.md). Fields left out keep their defaults.</summary>
    public static class LevelJson
    {
        public static LevelData Parse(string json)
        {
            var data = new LevelData();
            JsonUtility.FromJsonOverwrite(json, data);
            return data;
        }

        public static LevelData Parse(TextAsset asset)
        {
            var data = Parse(asset.text);
            if (string.IsNullOrEmpty(data.id)) data.id = asset.name;
            return data;
        }
    }
}
