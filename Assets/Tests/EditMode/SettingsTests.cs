using NUnit.Framework;
using ShiftRegister.Game.Settings;
using UnityEngine;

namespace ShiftRegister.Tests
{
    public class SettingsTests
    {
        [Test]
        public void Defaults_SoundOnWithMotion()
        {
            var s = new GameSettings();
            Assert.That(s.muted, Is.False);
            Assert.That(s.reducedMotion, Is.False);
            Assert.That(s.EffectiveVolume, Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void EffectiveVolume_ZeroWhenMuted()
        {
            var s = new GameSettings { volume = 5, muted = true };
            Assert.That(s.EffectiveVolume, Is.EqualTo(0f));
            s.muted = false;
            Assert.That(s.EffectiveVolume, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void StepVolume_Clamps()
        {
            var s = new GameSettings { volume = 9 };
            s.StepVolume(5);
            Assert.That(s.volume, Is.EqualTo(GameSettings.VolumeSteps));
            s.StepVolume(-20);
            Assert.That(s.volume, Is.EqualTo(0));
            s.volume = 99;
            Assert.That(s.EffectiveVolume, Is.EqualTo(1f), "a bad saved value is clamped");
        }

        [Test]
        public void Json_RoundTrip()
        {
            var s = new GameSettings { volume = 3, muted = true, reducedMotion = true };
            var back = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(s));
            Assert.That(back.volume, Is.EqualTo(3));
            Assert.That(back.muted, Is.True);
            Assert.That(back.reducedMotion, Is.True);
        }
    }
}
