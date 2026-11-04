using UnityEngine;

namespace ShiftRegister.Game.Progress
{
    /// <summary>Saves <see cref="PlayerProgress"/> as JSON in PlayerPrefs (IndexedDB in web builds).</summary>
    public static class ProgressStore
    {
        public const string Key = "ShiftRegister.Progress.v1";

        public static PlayerProgress Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json))
            {
                return new PlayerProgress();
            }

            try
            {
                return JsonUtility.FromJson<PlayerProgress>(json) ?? new PlayerProgress();
            }
            catch (System.ArgumentException e)
            {
                Debug.LogWarning($"Saved progress could not be read and was reset: {e.Message}");
                return new PlayerProgress();
            }
        }

        public static void Save(PlayerProgress progress)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(progress));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
