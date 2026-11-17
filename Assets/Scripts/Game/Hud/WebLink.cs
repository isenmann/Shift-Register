using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// Opens a web page in the system browser. The web build opens a new tab instead, so the game keeps
    /// running in its own (see ShiftRegisterClipboard.jslib).
    /// </summary>
    public static class WebLink
    {
        /// <summary>The public gallery of share codes (Web/ in the repository).</summary>
        public const string SharedLevelsUrl = "https://shift-register-levels.devplayrepeat.workers.dev";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SROpenTab(string url);

        public static void Open(string url) => SROpenTab(url);
#else
        public static void Open(string url) => Application.OpenURL(url);
#endif
    }
}
