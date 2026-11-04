using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.CustomLevels;
using ShiftRegister.Game.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The RANDOM screen: pick a difficulty and the game makes a level for it. Up and down choose, Enter makes
    /// the level, Esc cancels and then goes back. Three arrangements like the other menus. Making a level
    /// runs in slices, as the web build has no threads; it can take a moment on a slow device.
    /// </summary>
    public sealed partial class MenuView
    {
        const double RandomBudgetMs = 8.0;

        static readonly RandomDifficulty[] Difficulties = { RandomDifficulty.Easy, RandomDifficulty.Medium, RandomDifficulty.Hard };

        /// <summary>The RANDOM screen in one arrangement.</summary>
        sealed class RandomUi
        {
            public readonly HudButton[] Buttons = new HudButton[Difficulties.Length];
            public TextMeshProUGUI Status;
        }

        /// <summary>Where one arrangement puts things; all in its own mockup pixels.</summary>
        sealed class RandomLayout
        {
            public float HeadingX, HeadingY, HeadingW, HeadingH, HeadingSize;
            public float BackX, BackY, BackW, BackH;
            public float ButtonX, ButtonY, ButtonW, ButtonH, ButtonSize, RowPitch;
            /// <summary>Beside the button, or under it where the screen is narrow.</summary>
            public float TextX, TextDy, TextW, TextH, TextSize;
            public float NoteX, NoteY, NoteW, NoteH, NoteSize;
            public float StatusY, StatusH, StatusSize;
            public bool Outer, Keys;
        }

        readonly RandomSession randomSession = new RandomSession();
        int randomFocus, randomEnteredFrame;

        static readonly RandomLayout RandomDesktop = new RandomLayout
        {
            Outer = true,
            Keys = true,
            HeadingX = 56,
            HeadingY = 36,
            HeadingW = 400,
            HeadingH = 48,
            HeadingSize = 34,
            BackX = 1124,
            BackY = 38,
            BackW = 100,
            BackH = 44,
            ButtonX = 56,
            ButtonY = 130,
            ButtonW = 200,
            ButtonH = 72,
            ButtonSize = 20,
            RowPitch = 92,
            TextX = 280,
            TextDy = 0,
            TextW = 900,
            TextH = 72,
            TextSize = 17,
            NoteX = 56,
            NoteY = 424,
            NoteW = 1168,
            NoteH = 52,
            NoteSize = 15,
            StatusY = 490,
            StatusH = 28,
            StatusSize = 17
        };

        static readonly RandomLayout RandomPhoneLandscape = new RandomLayout
        {
            Outer = true,
            HeadingX = 16,
            HeadingY = 8,
            HeadingW = 300,
            HeadingH = 36,
            HeadingSize = 24,
            BackX = 815,
            BackY = 8,
            BackW = 84,
            BackH = 40,
            ButtonX = 16,
            ButtonY = 58,
            ButtonW = 170,
            ButtonH = 60,
            ButtonSize = 16,
            RowPitch = 70,
            TextX = 200,
            TextDy = 0,
            TextW = 699,
            TextH = 60,
            TextSize = 15,
            NoteX = 16,
            NoteY = 272,
            NoteW = 883,
            NoteH = 44,
            NoteSize = 14,
            StatusY = 330,
            StatusH = 28,
            StatusSize = 16
        };

        static readonly RandomLayout RandomPhonePortrait = new RandomLayout
        {
            HeadingX = 16,
            HeadingY = 28,
            HeadingW = 280,
            HeadingH = 36,
            HeadingSize = 26,
            BackX = 312,
            BackY = 24,
            BackW = 84,
            BackH = 44,
            ButtonX = 16,
            ButtonY = 130,
            ButtonW = 380,
            ButtonH = 64,
            ButtonSize = 18,
            RowPitch = 144,
            TextX = 16,
            TextDy = 70,
            TextW = 380,
            TextH = 66,
            TextSize = 15,
            NoteX = 16,
            NoteY = 570,
            NoteW = 380,
            NoteH = 88,
            NoteSize = 14,
            StatusY = 670,
            StatusH = 28,
            StatusSize = 16
        };

        void BuildRandom(Screens s, RandomLayout k)
        {
            var root = s.Random;
            var left = Outer(s, root, "Left", k.Outer ? -1f : 0f, 0f);
            var right = Outer(s, root, "Right", k.Outer ? 1f : 0f, 0f);
            var ui = new RandomUi();
            s.RandomUi = ui;

            Ui.Text(left, "Heading", k.HeadingX, k.HeadingY, k.HeadingW, k.HeadingH, displayBold, k.HeadingSize, Lcd.Ink, text: "RANDOM");
            HudButton.Create(right, "BACK", k.BackX, k.BackY, k.BackW, k.BackH, display, 14, () => flow.Show(GameScreen.Title));

            for (int i = 0; i < Difficulties.Length; i++)
            {
                int index = i;
                float y = k.ButtonY + i * k.RowPitch;
                ui.Buttons[i] = HudButton.Create(left, DifficultyName(Difficulties[i]), k.ButtonX, y, k.ButtonW, k.ButtonH, display, k.ButtonSize, () => MakeRandom(index));
                var text = Ui.Text(left, "About", k.TextX, y + k.TextDy, k.TextW, k.TextH, mono, k.TextSize, Lcd.Text2,
                    k.TextDy > 0f ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft, DifficultyText(Difficulties[i]));
                text.textWrappingMode = TextWrappingModes.Normal;
            }

            var note = Ui.Text(left, "Note", k.NoteX, k.NoteY, k.NoteW, k.NoteH, mono, k.NoteSize, Lcd.Text3, TextAlignmentOptions.TopLeft,
                "Every level is new and can be solved. They earn no stars and unlock nothing. After you beat one, SAVE keeps it in MY LEVELS; otherwise it is gone when you leave.");
            note.textWrappingMode = TextWrappingModes.Normal;
            ui.Status = Ui.Text(left, "Status", k.NoteX, k.StatusY, k.NoteW, k.StatusH, mono, k.StatusSize, Lcd.Ink);

            if (k.Keys)
            {
                Ui.Text(left, "Keys", 56, 690, 1168, 20, mono, 13, Lcd.Text3, text: "Up and down choose · Enter make a level · Esc back");
            }
        }

        static string DifficultyName(RandomDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RandomDifficulty.Easy:
                    return "EASY";
                case RandomDifficulty.Medium:
                    return "MEDIUM";
                default:
                    return "HARD";
            }
        }

        static string DifficultyText(RandomDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RandomDifficulty.Easy:
                    return "A plain board. 3 to 5 moves.";
                case RandomDifficulty.Medium:
                    return "Walls, pins or wrapping rows. 5 to 7 moves.";
                default:
                    return "Longer, and rows may be chained. Par is often a known solve, not the shortest, so try to beat it.";
            }
        }

        void EnterRandom(bool arriving)
        {
            if (arriving)
            {
                randomEnteredFrame = Time.frameCount;
            }

            RefreshRandom();
        }

        /// <summary>Runs on phones too, which have no keyboard.</summary>
        void UpdateRandom(Keyboard kb)
        {
            if (randomSession.IsGenerating)
            {
                if (randomSession.Tick(RandomBudgetMs))
                {
                    OnRandomFinished();
                }

                return;
            }
            // The key that opened this screen must not also act on it.
            if (kb == null || Time.frameCount - randomEnteredFrame <= 1)
            {
                return;
            }

            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)
            {
                StepRandom(-1);
            }
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)
            {
                StepRandom(1);
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                Sfx.Play(Sound.Click);
                MakeRandom(randomFocus);
            }
        }

        void StepRandom(int delta)
        {
            int next = Mathf.Clamp(randomFocus + delta, 0, Difficulties.Length - 1);
            if (next == randomFocus)
            {
                return;
            }

            Sfx.Play(Sound.Tick);
            randomFocus = next;
            RefreshRandom();
        }

        void MakeRandom(int index)
        {
            randomFocus = index;
            randomSession.Start(Difficulties[index], Random.Range(int.MinValue, int.MaxValue));
            RefreshRandom();
        }

        /// <summary>NEXT RANDOM on the solved banner: the same difficulty again.</summary>
        public void MakeRandomAgain() => MakeRandom(System.Array.IndexOf(Difficulties, randomSession.Difficulty));

        /// <summary>Esc while a level is being made stops that. False if nothing was being made.</summary>
        public bool CancelRandom()
        {
            if (!built || !randomSession.Cancel())
            {
                return false;
            }

            RefreshRandom();
            return true;
        }

        void OnRandomFinished()
        {
            if (randomSession.Failed)
            {
                Sfx.Play(Sound.Blocked);
                RefreshRandom();
                return;
            }

            flow.PlayRandom(randomSession.Level);
        }

        void RefreshRandom()
        {
            bool busy = randomSession.IsGenerating;
            string article = randomSession.Difficulty == RandomDifficulty.Easy ? "an" : "a";
            string making = $"Making {article} {DifficultyName(randomSession.Difficulty).ToLowerInvariant()} level...";
            string status = busy ? (DeviceMode.IsMobile ? making : making + " Esc cancels.")
                : randomSession.Failed ? "No level came out this time. Try again."
                : "";

            foreach (var s in Both)
            {
                var ui = s.RandomUi;
                ui.Status.text = status;
                for (int i = 0; i < ui.Buttons.Length; i++)
                {
                    ui.Buttons[i].Selected = i == randomFocus;
                    ui.Buttons[i].Disabled = busy;
                }
            }
        }
    }
}
