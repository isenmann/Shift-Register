using UnityEngine;

namespace ShiftRegister.Game.Input
{
    /// <summary>
    /// Mobile mode: the native Android (or iOS) app, or the web build in a phone or tablet browser.
    /// It brings swipe input and the portrait layouts; desktop browsers keep the keyboard and mouse UI.
    /// </summary>
    public static class DeviceMode
    {
        /// <summary>Set by the editor's "simulate mobile" switch; null = detect.</summary>
        public static bool? Override;

        public static bool IsMobile => Override ?? Detect();

        static bool Detect()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                case RuntimePlatform.IPhonePlayer:
                    return true;
                case RuntimePlatform.WebGLPlayer:
                    // True in mobile browsers (Android and iOS phones and tablets).
                    return Application.isMobilePlatform;
                default:
                    return false;
            }
        }
    }
}
