using System;
using NUnit.Framework;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;

namespace ShiftRegister.Tests
{
    public class SynthTests
    {
        [Test]
        public void Hz_A4AndOctaves()
        {
            Assert.That(Synth.Hz(0), Is.EqualTo(440f).Within(0.01f));
            Assert.That(Synth.Hz(12), Is.EqualTo(880f).Within(0.01f));
            Assert.That(Synth.Hz(-12), Is.EqualTo(220f).Within(0.01f));
        }

        [Test]
        public void Render_LengthCoversTheLastNote()
        {
            var samples = Synth.Render(Note.Of(Wave.Square, 440f, 0f, 0.1f), Note.Of(Wave.Square, 660f, 0.2f, 0.1f));
            Assert.That(samples.Length, Is.EqualTo((int)Math.Ceiling(0.3 * Synth.SampleRate)).Within(1));
        }

        [TestCase(Wave.Square)]
        [TestCase(Wave.Triangle)]
        [TestCase(Wave.Noise)]
        public void Render_NormalizedAndQuietAtTheEnds(Wave wave)
        {
            var samples = Synth.Render(Note.Glide(wave, 300f, 600f, 0f, 0.2f));
            float max = 0f;
            foreach (float s in samples) max = Math.Max(max, Math.Abs(s));
            Assert.That(max, Is.EqualTo(Synth.Peak).Within(1e-4f));
            Assert.That(Math.Abs(samples[0]), Is.LessThan(0.05f), "starts without a click");
            Assert.That(Math.Abs(samples[samples.Length - 1]), Is.LessThan(0.05f), "ends without a click");
        }

        [Test]
        public void Render_LaterNoteStartsSilent()
        {
            var samples = Synth.Render(Note.Of(Wave.Square, 440f, 0.1f, 0.1f));
            int start = (int)(0.1f * Synth.SampleRate);
            for (int i = 0; i < start; i++) Assert.That(samples[i], Is.EqualTo(0f));
        }

        [Test]
        public void Envelope_ZeroOutsideTheNote()
        {
            Assert.That(Synth.Envelope(-0.01f, 0.1f), Is.EqualTo(0f));
            Assert.That(Synth.Envelope(0.1f, 0.1f), Is.EqualTo(0f));
            Assert.That(Synth.Envelope(0.02f, 0.1f), Is.GreaterThan(0f));
        }

        [Test]
        public void EverySoundRenders()
        {
            foreach (Sound s in Enum.GetValues(typeof(Sound)))
            {
                var samples = SoundBank.Render(s);
                Assert.That(samples.Length, Is.GreaterThan(Synth.SampleRate / 100), s.ToString());
                Assert.That(samples.Length, Is.LessThan(Synth.SampleRate), s + " stays under a second");
                Assert.That(SoundBank.Gain(s), Is.InRange(0.05f, 1f), s.ToString());
            }
        }

        [Test]
        public void ShiftNotes_OnePerLineRising()
        {
            Assert.That(SoundBank.ShiftNotes.Length, Is.EqualTo(BoardBits.Size));
            for (int i = 1; i < SoundBank.ShiftNotes.Length; i++)
                Assert.That(SoundBank.ShiftNotes[i], Is.GreaterThan(SoundBank.ShiftNotes[i - 1]));
            for (int line = 0; line < BoardBits.Size; line++)
            {
                Assert.That(SoundBank.Shift(Axis.Row, line, true).Length, Is.GreaterThan(0));
                Assert.That(SoundBank.Shift(Axis.Column, line, false).Length, Is.GreaterThan(0));
            }
        }
    }
}
