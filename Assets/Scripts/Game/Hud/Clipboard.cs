using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The system clipboard. Pasting takes two steps because the web build asks the player in a small
    /// HTML box (see ShiftRegisterClipboard.jslib): call <see cref="BeginPaste"/>, then poll
    /// <see cref="TryFinishPaste"/>.
    /// </summary>
    public static class Clipboard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SRClipboardCopy(string text);
        [DllImport("__Internal")] static extern void SRClipboardAskPaste();
        [DllImport("__Internal")] static extern int SRClipboardPasteState();
        [DllImport("__Internal")] static extern string SRClipboardTakePaste();

        public static void Copy(string text) => SRClipboardCopy(text);

        public static void BeginPaste()
        {
            // The box needs the keys, which the game otherwise keeps from the page.
            WebGLInput.captureAllKeyboardInput = false;
            SRClipboardAskPaste();
        }

        public static bool TryFinishPaste(out string text)
        {
            text = "";
            if (SRClipboardPasteState() == 0)
            {
                return false;
            }

            text = SRClipboardTakePaste();
            WebGLInput.captureAllKeyboardInput = true;
            return true;
        }
#else
        public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;

        public static void BeginPaste() { }

        public static bool TryFinishPaste(out string text)
        {
            text = GUIUtility.systemCopyBuffer ?? "";
            return true;
        }
#endif
    }
}
