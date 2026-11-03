using System;

namespace ShiftRegister.Game.Audio
{
    public enum Wave { Square, Triangle, Noise }

    /// <summary>One synthesized note: a waveform gliding from one pitch to another under a pluck envelope.</summary>
    public struct Note
    {
        public Wave Wave;
        public float FromHz, ToHz;
        /// <summary>Seconds after the start of the sound.</summary>
        public float Start;
        public float Duration;
        public float Volume;
        /// <summary>Square duty cycle (0.125, 0.25, 0.5); ignored by the other waves.</summary>
        public float Duty;
        /// <summary>Pitch wobble depth as a fraction of the pitch (0 = none), at <see cref="VibratoHz"/>.</summary>
        public float Vibrato;
        public float VibratoHz;

        public static Note Of(Wave wave, float hz, float start, float duration, float volume = 1f, float duty = 0.5f) =>
            new Note { Wave = wave, FromHz = hz, ToHz = hz, Start = start, Duration = duration, Volume = volume, Duty = duty };

        public static Note Glide(Wave wave, float fromHz, float toHz, float start, float duration, float volume = 1f, float duty = 0.5f) =>
            new Note { Wave = wave, FromHz = fromHz, ToHz = toHz, Start = start, Duration = duration, Volume = volume, Duty = duty };
    }

    /// <summary>
    /// A tiny chiptune synth: renders notes into mono samples. No Unity dependencies, so the sounds are
    /// built in code (nothing to license) and the maths is testable.
    /// </summary>
    public static class Synth
    {
        public const int SampleRate = 44100;
        const float Attack = 0.003f;
        /// <summary>Peak level after mixing; leaves headroom for overlapping sounds.</summary>
        public const float Peak = 0.6f;

        /// <summary>Semitones above A4 (440 Hz) to Hz.</summary>
        public static float Hz(float semitonesFromA4) => 440f * (float)Math.Pow(2.0, semitonesFromA4 / 12.0);

        public static float[] Render(params Note[] notes)
        {
            float length = 0f;
            foreach (var n in notes) length = Math.Max(length, n.Start + n.Duration);
            var samples = new float[Math.Max(1, (int)Math.Ceiling(length * SampleRate))];

            uint noise = 0x9E3779B9u;
            foreach (var n in notes)
            {
                int first = (int)(n.Start * SampleRate);
                int count = (int)(n.Duration * SampleRate);
                double phase = 0.0;
                float noiseValue = 0f;
                for (int i = 0; i < count && first + i < samples.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float u = t / n.Duration;
                    // Exponential glide sounds even across octaves.
                    float hz = n.FromHz * (float)Math.Pow(n.ToHz / n.FromHz, u);
                    if (n.Vibrato > 0f) hz *= 1f + n.Vibrato * (float)Math.Sin(2.0 * Math.PI * n.VibratoHz * t);

                    double before = phase;
                    phase += hz / SampleRate;
                    phase -= Math.Floor(phase);

                    float v;
                    switch (n.Wave)
                    {
                        case Wave.Square:
                            v = phase < n.Duty ? 1f : -1f;
                            break;
                        case Wave.Triangle:
                            v = (float)(phase < 0.5 ? 4.0 * phase - 1.0 : 3.0 - 4.0 * phase);
                            break;
                        default:
                            // Sample-and-hold noise: a new random value every cycle, so the pitch colours it.
                            if (phase < before)
                            {
                                noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                                noiseValue = (noise & 0xFFFF) / 32767.5f - 1f;
                            }
                            v = noiseValue;
                            break;
                    }

                    samples[first + i] += v * n.Volume * Envelope(t, n.Duration);
                }
            }

            Smooth(samples);
            Normalize(samples);
            return samples;
        }

        /// <summary>Quick attack, exponential decay, and a short fade so every note ends at zero.</summary>
        public static float Envelope(float t, float duration)
        {
            if (t < 0f || t >= duration) return 0f;
            float attack = Math.Min(1f, t / Attack);
            float decay = (float)Math.Exp(-3.0 * t / duration);
            float release = Math.Min(1f, (duration - t) / 0.01f);
            return attack * decay * release;
        }

        /// <summary>A gentle one-pole low-pass that takes the edge off the square waves.</summary>
        static void Smooth(float[] samples)
        {
            const float a = 0.35f;
            float y = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                y += a * (samples[i] - y);
                samples[i] = y;
            }
        }

        static void Normalize(float[] samples)
        {
            float max = 0f;
            foreach (float s in samples) max = Math.Max(max, Math.Abs(s));
            if (max <= 0f) return;
            float k = Peak / max;
            for (int i = 0; i < samples.Length; i++) samples[i] *= k;
        }
    }
}
