using UnityEngine;

namespace ShiftRegister.Game.CustomLevels
{
    /// <summary>Saves the <see cref="CustomLevelLibrary"/> as JSON in PlayerPrefs (IndexedDB in web builds).</summary>
    public static class CustomLevelStore
    {
        public const string Key = "ShiftRegister.CustomLevels.v1";

        public static CustomLevelLibrary Load(string key = Key)
        {
            string json = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json))
            {
                return new CustomLevelLibrary();
            }

            try
            {
                var library = JsonUtility.FromJson<CustomLevelLibrary>(json) ?? new CustomLevelLibrary();
                int dropped = library.RemoveInvalid();
                if (dropped > 0)
                {
                    Debug.LogWarning($"{dropped} saved custom level(s) were damaged and were dropped.");
                }

                return library;
            }
            catch (System.ArgumentException exception)
            {
                Debug.LogWarning($"Saved custom levels could not be read: {exception.Message}");
                return new CustomLevelLibrary();
            }
        }

        public static void Save(CustomLevelLibrary library, string key = Key)
        {
            PlayerPrefs.SetString(key, JsonUtility.ToJson(library));
            PlayerPrefs.Save();
        }
    }
}
