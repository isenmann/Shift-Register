using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// Keeps the whole visible frame (<see cref="ScreenLayout.FrameSize"/>) on screen at any aspect
    /// ratio and tells <see cref="ScreenLayout"/> the size of the area the game draws into. On phones
    /// and tablets that area is the safe area, so camera cutouts and rounded corners never cover the
    /// game; a background camera paints the strips outside it in the LCD colour.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFit : MonoBehaviour
    {
        Camera cam, backdrop;

        void LateUpdate()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (Screen.width <= 0 || Screen.height <= 0) return;

            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            if (Application.isPlaying && DeviceMode.IsMobile && cam.targetTexture == null)
            {
                area = Screen.safeArea;
                bool inset = area.width < Screen.width || area.height < Screen.height;
                if (inset) EnsureBackdrop();
                if (backdrop != null) backdrop.enabled = inset;
            }
            if (cam.targetTexture == null)
                cam.rect = new Rect(area.x / Screen.width, area.y / Screen.height, area.width / Screen.width, area.height / Screen.height);

            ScreenLayout.SetScreen(cam.pixelWidth, cam.pixelHeight);
            var frame = ScreenLayout.FrameSize;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(frame.y * 0.5f, frame.x * 0.5f / cam.aspect);
        }

        /// <summary>A camera behind the main one that only clears the whole screen to the background colour.</summary>
        void EnsureBackdrop()
        {
            if (backdrop != null) return;
            var go = new GameObject("Backdrop Camera");
            go.transform.SetParent(transform, false);
            backdrop = go.AddComponent<Camera>();
            backdrop.depth = cam.depth - 1f;
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = cam.backgroundColor;
            backdrop.cullingMask = 0;
            backdrop.orthographic = true;
            backdrop.rect = new Rect(0f, 0f, 1f, 1f);
        }
    }
}
