using System;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShiftRegister.Game
{
    public enum GameScreen { Title, Chapters, Playing, Options }

    /// <summary>
    /// Switches between the title screen, chapter select and the level being played, all in one scene.
    /// Esc steps back: level → chapters → title, options → title. M mutes anywhere.
    /// </summary>
    public sealed class ScreenFlow : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] BoardInput input;
        [Tooltip("The in-level HUD canvas.")]
        [SerializeField] GameObject hud;
        [SerializeField] MenuView menu;

        public GameScreen Current { get; private set; } = GameScreen.Title;

        HudView hudView;

        /// <summary>Raised after the screen changes.</summary>
        public event Action<GameScreen> Changed;

        void Start()
        {
            hudView = hud.GetComponentInChildren<HudView>(true);
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
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.mKey.wasPressedThisFrame) game.ChangeSettings(s => s.muted = !s.muted);
            if (!kb.escapeKey.wasPressedThisFrame) return;
            if (Current == GameScreen.Playing && hudView != null && hudView.HelpOpen)
            {
                hudView.CloseHelp();
                return;
            }
            if (Current == GameScreen.Playing) Show(GameScreen.Chapters);
            else if (Current == GameScreen.Chapters || Current == GameScreen.Options) Show(GameScreen.Title);
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
        /// True where the game can close its own window: desktop builds and the editor. A browser tab
        /// or a phone app is left by the browser or the system instead.
        /// </summary>
        public static bool CanQuit =>
            Application.platform != RuntimePlatform.WebGLPlayer && !Application.isMobilePlatform && !DeviceMode.IsMobile;

        /// <summary>QUIT on the title screen. Progress is already saved after every solved level.</summary>
        public void Quit()
        {
            if (!CanQuit) return;
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
            game.BoardView.gameObject.SetActive(playing);
            input.enabled = playing;
            hud.SetActive(playing);
            menu.Show(screen);
            Changed?.Invoke(screen);
        }
    }
}
