using ShiftRegister.Core;

namespace ShiftRegister.Game.Audio
{
    public enum Sound
    {
        /// <summary>A line part moved; pitched by line (see <see cref="SoundBank.ShiftNotes"/>).</summary>
        Shift,
        /// <summary>Bits fell off the board and fly to the bit bucket.</summary>
        Lost,
        /// <summary>Lost bits landed in the bucket.</summary>
        Land,
        /// <summary>Gates switched bits on or off.</summary>
        Gate,
        /// <summary>A loss rule broke.</summary>
        Broken,
        Solved,
        Undo,
        Reset,
        /// <summary>A move that cannot happen (locked line, wall, broken board).</summary>
        Blocked,
        /// <summary>A button press or a screen change.</summary>
        Click,
        /// <summary>A cursor or menu focus step.</summary>
        Tick
    }

    /// <summary>
    /// The game's sound effects as synth recipes. Everything here is plain data, rendered by <see cref="Synth"/>.
    /// </summary>
    public static class SoundBank
    {
        /// <summary>C major pentatonic, C5 up to E6, in semitones from A4: one note per line.</summary>
        public static readonly int[] ShiftNotes = { 3, 5, 7, 10, 12, 15, 17, 19 };

        /// <summary>How loud each sound plays relative to the others (all are rendered to the same peak).</summary>
        public static float Gain(Sound sound) => sound switch
        {
            Sound.Shift => 0.45f,
            Sound.Lost => 0.5f,
            Sound.Land => 0.45f,
            Sound.Gate => 0.35f,
            Sound.Broken => 0.6f,
            Sound.Solved => 0.6f,
            Sound.Undo => 0.4f,
            Sound.Reset => 0.4f,
            Sound.Blocked => 0.5f,
            Sound.Click => 0.3f,
            _ => 0.18f
        };

        /// <summary>
        /// The shift blip: rows go up the scale from the bottom row, columns from the left column, so a
        /// line keeps its note. Rows are a thin square, columns a hollow triangle; the pitch bends a
        /// little in the direction of the move (right/up bend up).
        /// </summary>
        public static float[] Shift(Axis axis, int line, bool bendUp)
        {
            int index = axis == Axis.Row ? BoardBits.Size - 1 - line : line;
            float frequency = Synth.Hz(ShiftNotes[index]);
            float to = frequency * (bendUp ? 1.06f : 0.94f);
            var wave = axis == Axis.Row ? Wave.Square : Wave.Triangle;
            return Synth.Render(Note.Glide(wave, frequency, to, 0f, 0.09f, 1f, 0.25f));
        }

        public static float[] Render(Sound sound)
        {
            switch (sound)
            {
                case Sound.Shift:
                    return Shift(Axis.Row, 4, true);
                case Sound.Lost:
                    return Synth.Render(
                        Note.Glide(Wave.Square, 660f, 110f, 0f, 0.32f, 1f, 0.125f),
                        Note.Glide(Wave.Noise, 3000f, 400f, 0f, 0.12f, 0.25f));
                case Sound.Land:
                    return Synth.Render(
                        Note.Glide(Wave.Triangle, 180f, 70f, 0f, 0.12f),
                        Note.Of(Wave.Noise, 900f, 0f, 0.05f, 0.4f));
                case Sound.Gate:
                    return Synth.Render(
                        Note.Of(Wave.Square, Synth.Hz(19), 0f, 0.045f, 1f, 0.125f),
                        Note.Of(Wave.Square, Synth.Hz(26), 0.045f, 0.06f, 1f, 0.125f));
                case Sound.Broken:
                    return Synth.Render(
                        new Note
                        {
                            Wave = Wave.Square,
                            FromHz = 110f,
                            ToHz = 98f,
                            Duration = 0.4f,
                            Volume = 1f,
                            Duty = 0.5f,
                            Vibrato = 0.04f,
                            VibratoHz = 14f
                        },
                        Note.Glide(Wave.Square, 117f, 104f, 0f, 0.4f, 0.7f, 0.25f));
                case Sound.Solved:
                    return Synth.Render(
                        Note.Of(Wave.Square, Synth.Hz(3), 0.00f, 0.10f, 0.8f, 0.25f),
                        Note.Of(Wave.Square, Synth.Hz(7), 0.08f, 0.10f, 0.8f, 0.25f),
                        Note.Of(Wave.Square, Synth.Hz(10), 0.16f, 0.10f, 0.8f, 0.25f),
                        Note.Of(Wave.Square, Synth.Hz(15), 0.24f, 0.45f, 0.9f, 0.25f),
                        Note.Of(Wave.Triangle, Synth.Hz(-9), 0.24f, 0.45f, 0.8f));
                case Sound.Undo:
                    return Synth.Render(Note.Glide(Wave.Triangle, 740f, 520f, 0f, 0.09f));
                case Sound.Reset:
                    return Synth.Render(
                        Note.Of(Wave.Triangle, Synth.Hz(10), 0.00f, 0.07f),
                        Note.Of(Wave.Triangle, Synth.Hz(7), 0.05f, 0.07f),
                        Note.Of(Wave.Triangle, Synth.Hz(3), 0.10f, 0.12f));
                case Sound.Blocked:
                    return Synth.Render(
                        Note.Glide(Wave.Square, 120f, 80f, 0f, 0.08f, 1f, 0.5f),
                        Note.Of(Wave.Noise, 600f, 0f, 0.04f, 0.5f));
                case Sound.Click:
                    return Synth.Render(Note.Of(Wave.Square, 1320f, 0f, 0.035f, 1f, 0.25f));
                default:
                    return Synth.Render(Note.Of(Wave.Triangle, 2100f, 0f, 0.018f));
            }
        }
    }
}
