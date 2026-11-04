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
        Camera targetCamera, backdrop;

        void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            var area = new Rect(0f, 0f, Screen.width, Screen.height);
            if (Application.isPlaying && DeviceMode.IsMobile && targetCamera.targetTexture == null)
            {
                area = Screen.safeArea;
                bool inset = area.width < Screen.width || area.height < Screen.height;
                if (inset)
                {
                    EnsureBackdrop();
                }

                if (backdrop != null)
                {
                    backdrop.enabled = inset;
                }
            }

            if (targetCamera.targetTexture == null)
            {
                targetCamera.rect = new Rect(area.x / Screen.width, area.y / Screen.height, area.width / Screen.width,
                    area.height / Screen.height);
            }

            ScreenLayout.SetScreen(targetCamera.pixelWidth, targetCamera.pixelHeight);
            var frame = ScreenLayout.FrameSize;
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = Mathf.Max(frame.y * 0.5f, frame.x * 0.5f / targetCamera.aspect);
        }

        /// <summary>A camera behind the main one that only clears the whole screen to the background colour.</summary>
        void EnsureBackdrop()
        {
            if (backdrop != null)
            {
                return;
            }

            var gameObject = new GameObject("Backdrop Camera");
            gameObject.transform.SetParent(transform, false);
            backdrop = gameObject.AddComponent<Camera>();
            backdrop.depth = targetCamera.depth - 1f;
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = targetCamera.backgroundColor;
            backdrop.cullingMask = 0;
            backdrop.orthographic = true;
            backdrop.rect = new Rect(0f, 0f, 1f, 1f);
        }
    }
}
