using UnityEngine;

namespace ShiftRegister.Game.Settings
{
    /// <summary>Saves <see cref="GameSettings"/> as JSON in PlayerPrefs, apart from the progress.</summary>
    public static class SettingsStore
    {
        public const string Key = "ShiftRegister.Settings.v1";

        public static GameSettings Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json))
            {
                return new GameSettings();
            }

            try
            {
                var settings = JsonUtility.FromJson<GameSettings>(json) ?? new GameSettings();
                settings.StepVolume(0); // clamp a hand-edited value
                return settings;
            }
            catch (System.ArgumentException exception)
            {
                Debug.LogWarning($"Saved options could not be read and were reset: {exception.Message}");
                return new GameSettings();
            }
        }

        public static void Save(GameSettings settings)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(settings));
            PlayerPrefs.Save();
        }
    }
}
