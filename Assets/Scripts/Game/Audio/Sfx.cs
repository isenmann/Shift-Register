using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.View;
using UnityEngine;

namespace ShiftRegister.Game.Audio
{
    /// <summary>
    /// Plays the synthesized sound effects. Gameplay sounds follow the game and board view events (so
    /// they line up with the animation, or play at once when it is off); menus call <see cref="Play"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class Sfx : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] ScreenFlow flow;

        static Sfx instance;

        AudioSource source;
        readonly Dictionary<Sound, AudioClip> clips = new Dictionary<Sound, AudioClip>();
        // [axis, line, bend up]
        readonly AudioClip[,,] shiftClips = new AudioClip[2, BoardBits.Size, 2];
        readonly Dictionary<AudioClip, int> lastFrame = new Dictionary<AudioClip, int>();

        /// <summary>Plays a sound if the audio object exists (it does not in edit-mode tests).</summary>
        public static void Play(Sound sound)
        {
            if (instance != null) instance.PlayClip(instance.clips[sound], SoundBank.Gain(sound));
        }

        /// <summary>Plays a line's shift note (the title intro uses them as a scale).</summary>
        public static void PlayShift(Axis axis, int line, bool bendUp)
        {
            if (instance != null)
                instance.PlayClip(instance.shiftClips[(int)axis, line, bendUp ? 1 : 0], SoundBank.Gain(Sound.Shift));
        }

        void Awake()
        {
            instance = this;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            foreach (Sound s in System.Enum.GetValues(typeof(Sound)))
                clips[s] = Clip(s.ToString(), SoundBank.Render(s));
            for (int axis = 0; axis < 2; axis++)
            for (int line = 0; line < BoardBits.Size; line++)
            for (int up = 0; up < 2; up++)
                shiftClips[axis, line, up] = Clip($"Shift {axis}-{line}-{up}", SoundBank.Shift((Axis)axis, line, up == 1));
        }

        void OnEnable()
        {
            if (game != null)
            {
                game.Moved += OnMoved;
                game.Undone += OnUndone;
                game.Restarted += OnRestarted;
                game.Blocked += OnBlocked;
                game.BoardView.GatesSwitched += OnGates;
                game.BoardView.Reacted += OnReacted;
            }
            if (flow != null) flow.Changed += OnScreen;
        }

        void OnDisable()
        {
            if (game != null)
            {
                game.Moved -= OnMoved;
                game.Undone -= OnUndone;
                game.Restarted -= OnRestarted;
                game.Blocked -= OnBlocked;
                game.BoardView.GatesSwitched -= OnGates;
                game.BoardView.Reacted -= OnReacted;
            }
            if (flow != null) flow.Changed -= OnScreen;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnMoved(MoveOutcome o)
        {
            var m = o.Move;
            bool up = m.Direction == Direction.Right || m.Direction == Direction.Up;
            PlayShift(m.Axis, m.Line, up);
            if (o.LostCount > 0) Play(Sound.Lost);
        }

        void OnGates(MoveOutcome o) => Play(Sound.Gate);

        void OnReacted(MoveOutcome o, bool solved, bool broken)
        {
            if (o.LostCount > 0) Play(Sound.Land);
            if (broken) Play(Sound.Broken);
            if (solved) Play(Sound.Solved);
        }

        void OnUndone() => Play(Sound.Undo);
        void OnRestarted() => Play(Sound.Reset);
        void OnBlocked() => Play(Sound.Blocked);

        void OnScreen(GameScreen screen)
        {
            // The first screen is shown at startup, not by the player.
            if (Time.timeSinceLevelLoad > 0.1f) Play(Sound.Click);
        }

        void PlayClip(AudioClip clip, float gain)
        {
            // One copy per frame: a button that also changes the screen clicks once.
            if (lastFrame.TryGetValue(clip, out int frame) && frame == Time.frameCount) return;
            lastFrame[clip] = Time.frameCount;
            source.PlayOneShot(clip, gain);
        }

        static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Synth.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
