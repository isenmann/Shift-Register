using System;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShiftRegister.Game
{
    public enum GameScreen { Title, Chapters, Playing, Options, MyLevels, Editor, Random }

    /// <summary>
    /// Switches between the title screen, chapter select and the level being played, all in one scene.
    /// Esc steps back: level → chapters → title, options → title, editor → my levels → title, random →
    /// title; a custom level goes back to where it was opened from. M mutes anywhere,
    /// except while typing in a text field.
    /// </summary>
    public sealed class ScreenFlow : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] BoardInput input;
        [Tooltip("The in-level HUD canvas.")]
        [SerializeField] GameObject hud;
        [SerializeField] MenuView menu;
        [SerializeField] LevelEditor.EditorView editor;

        HudView hudView;
        GameScreen customReturn = GameScreen.MyLevels;

        public GameScreen Current { get; private set; } = GameScreen.Title;

        /// <summary>Raised after the screen changes.</summary>
        public event Action<GameScreen> Changed;

        /// <summary>
        /// True where the game can close its own window: desktop builds and the editor. A browser tab
        /// or a phone app is left by the browser or the system instead.
        /// </summary>
        public static bool CanQuit =>
            Application.platform != RuntimePlatform.WebGLPlayer && !Application.isMobilePlatform
            && !DeviceMode.IsMobile;

        void Start()
        {
            hudView = hud.GetComponentInChildren<HudView>(true);
            game.LeaveCustom += LeaveLevel;
            game.NextRandom += NextRandom;
            if (game.StartsInLevel)
            {
                game.LoadStartLevel();
                Show(GameScreen.Playing);
            }
            else
            {
                Show(GameScreen.Title);
            }
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Ui.IsTyping)
            {
                return;
            }

            if (keyboard.mKey.wasPressedThisFrame)
            {
                game.ChangeSettings(settings => settings.muted = !settings.muted);
            }

            if (!keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (Current == GameScreen.Playing && hudView != null && hudView.HelpOpen)
            {
                hudView.CloseHelp();
                return;
            }

            if (Current == GameScreen.Editor)
            {
                if (!editor.CloseShare() && !editor.CancelSolve())
                {
                    Show(GameScreen.MyLevels);
                }

                return;
            }

            if (Current == GameScreen.MyLevels && menu.CloseShare())
            {
                return;
            }

            if (Current == GameScreen.Random && menu.CancelRandom())
            {
                return;
            }

            if (Current == GameScreen.Playing)
            {
                LeaveLevel();
            }
            else if (Current == GameScreen.Chapters || Current == GameScreen.Options || Current == GameScreen.MyLevels
            || Current == GameScreen.Random)
            {
                Show(GameScreen.Title);
            }
        }

        /// <summary>PLAY on the title screen: the last level played, or the first open unsolved one.</summary>
        public void Resume()
        {
            var (chapter, level) = game.ResumeTarget();
            Play(chapter, level);
        }

        public void Play(int chapter, int level)
        {
            game.Load(chapter, level);
            Show(GameScreen.Playing);
        }

        /// <summary>
        /// Leaves the level being played: a custom one to where it came from, any other to chapter select.
        /// </summary>
        public void LeaveLevel() => Show(game.IsCustom ? customReturn : GameScreen.Chapters);

        /// <summary>Plays one of the player's own levels; leaving it returns to <paramref name="returnTo"/>.</summary>
        public void PlayCustom(CustomLevels.CustomLevel entry, GameScreen returnTo)
        {
            customReturn = returnTo;
            game.PlayCustom(entry);
            Show(GameScreen.Playing);
        }

        /// <summary>Plays a finished random level; leaving it returns to the RANDOM screen.</summary>
        public void PlayRandom(CustomLevels.CustomLevel entry)
        {
            customReturn = GameScreen.Random;
            game.PlayRandom(entry);
            Show(GameScreen.Playing);
        }

        /// <summary>NEXT RANDOM on the solved banner: another level of the same difficulty.</summary>
        void NextRandom()
        {
            Show(GameScreen.Random);
            menu.MakeRandomAgain();
        }

        /// <summary>Opens one of the player's own levels in the editor.</summary>
        public void EditLevel(string id)
        {
            editor.SetLevel(id);
            Show(GameScreen.Editor);
        }

        /// <summary>QUIT on the title screen. Progress is already saved after every solved level.</summary>
        public void Quit()
        {
            if (!CanQuit)
            {
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Show(GameScreen screen)
        {
            Current = screen;
            bool playing = screen == GameScreen.Playing;
            bool editing = screen == GameScreen.Editor;
            game.BoardView.gameObject.SetActive(playing || editing);
            input.enabled = playing;
            hud.SetActive(playing);
            menu.Show(screen);
            if (editor != null)
            {
                editor.gameObject.SetActive(editing);
            }

            Changed?.Invoke(screen);
        }
    }
}
