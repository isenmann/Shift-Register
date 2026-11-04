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
            var settings = new GameSettings();
            Assert.That(settings.muted, Is.False);
            Assert.That(settings.reducedMotion, Is.False);
            Assert.That(settings.EffectiveVolume, Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void EffectiveVolume_ZeroWhenMuted()
        {
            var settings = new GameSettings { volume = 5, muted = true };
            Assert.That(settings.EffectiveVolume, Is.EqualTo(0f));
            settings.muted = false;
            Assert.That(settings.EffectiveVolume, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void StepVolume_Clamps()
        {
            var settings = new GameSettings { volume = 9 };
            settings.StepVolume(5);
            Assert.That(settings.volume, Is.EqualTo(GameSettings.VolumeSteps));
            settings.StepVolume(-20);
            Assert.That(settings.volume, Is.EqualTo(0));
            settings.volume = 99;
            Assert.That(settings.EffectiveVolume, Is.EqualTo(1f), "a bad saved value is clamped");
        }

        [Test]
        public void Json_RoundTrip()
        {
            var settings = new GameSettings { volume = 3, muted = true, reducedMotion = true };
            var back = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(settings));
            Assert.That(back.volume, Is.EqualTo(3));
            Assert.That(back.muted, Is.True);
            Assert.That(back.reducedMotion, Is.True);
        }
    }
}
