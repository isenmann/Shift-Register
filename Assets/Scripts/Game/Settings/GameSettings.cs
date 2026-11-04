using System;
using UnityEngine;

namespace ShiftRegister.Game.Settings
{
    /// <summary>The player's options. Serialisable with JsonUtility (see <see cref="SettingsStore"/>).</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int VolumeSteps = 10;

        public int version = 1;
        /// <summary>Sound volume in steps of 1/<see cref="VolumeSteps"/> (0 = silent).</summary>
        public int volume = 8;
        public bool muted;
        /// <summary>
        /// Moves snap into place: no slides, flying bits, shakes, hops or popping banners. Bits still move their eyes.
        /// </summary>
        public bool reducedMotion;
        /// <summary>
        /// Bits blink, nap, peek and react while you play, and lost bits stay in the bucket. Off: plain faces at rest.
        /// Saves from before this option read as on.
        /// </summary>
        public bool bitLife = true;

        /// <summary>What the audio listener plays at: 0 when muted.</summary>
        public float EffectiveVolume => muted ? 0f : Mathf.Clamp(volume, 0, VolumeSteps) / (float)VolumeSteps;

        public void StepVolume(int delta) => volume = Mathf.Clamp(volume + delta, 0, VolumeSteps);
    }
}
