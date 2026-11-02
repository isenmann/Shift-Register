using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>Keeps the whole reference frame (<see cref="ScreenLayout.FrameSize"/>) visible at any aspect ratio.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFit : MonoBehaviour
    {
        Camera cam;

        void LateUpdate()
        {
            if (cam == null) cam = GetComponent<Camera>();
            var frame = ScreenLayout.FrameSize;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(frame.y * 0.5f, frame.x * 0.5f / cam.aspect);
        }
    }
}
