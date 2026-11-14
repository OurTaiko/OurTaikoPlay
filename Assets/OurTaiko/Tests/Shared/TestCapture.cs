using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OurTaiko.Tests
{
    // Renders the active scene's Canvas (or the given one) through the main camera into
    // TestResults/<name>; a scene without a camera borrows a temporary one. The play scenes'
    // PlayfieldMask canvas only letterboxes the screen, so it is not the scene's canvas.
    public static class TestCapture
    {
        public static Canvas SceneCanvas() => Object.FindObjectsByType<Canvas>()
            .Single(canvas => canvas.isRootCanvas && canvas.gameObject.scene == SceneManager.GetActiveScene()
                && !canvas.TryGetComponent<PlayfieldMask>(out _));

        // For Capture's verify, with a capture of another shape than 16:9 (a tablet's, a phone's):
        // the scene's 1920x1080 stage is whole and in the middle of the screen, as large as fits.
        // Throws otherwise (this assembly has no NUnit).
        public static System.Action<Camera> StageIsCentred(RectTransform stage) => camera =>
        {
            var corners = new Vector3[4];
            stage.GetWorldCorners(corners);
            Vector2 low = camera.WorldToViewportPoint(corners[0]), high = camera.WorldToViewportPoint(corners[2]);
            Vector2 centre = (low + high) / 2, size = high - low;
            bool centred = Mathf.Abs(centre.x - 0.5f) < 0.002f && Mathf.Abs(centre.y - 0.5f) < 0.002f;
            bool fits = Mathf.Abs(Mathf.Max(size.x, size.y) - 1) < 0.002f && Mathf.Min(size.x, size.y) <= 1.002f;
            if (!centred || !fits)
                throw new System.InvalidOperationException("The stage is not whole in the middle of a " + camera.pixelWidth + "x" + camera.pixelHeight
                    + " screen: it covers " + low.ToString("F3") + " to " + high.ToString("F3") + " of it");
        };

        public static void Capture(string name, int width = 1920, int height = 1080, System.Action<Camera> verify = null, Canvas canvas = null)
        {
            canvas ??= SceneCanvas();
            var camera = Camera.main;
            var borrowed = camera == null ? new GameObject("CaptureCamera", typeof(Camera)) : null;
            if (borrowed != null) { camera = borrowed.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; }
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            var previous = RenderTexture.active; var previousTarget = camera.targetTexture;
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; float previousDistance = canvas.planeDistance;
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                scaler.SendMessage("Handle"); Canvas.ForceUpdateCanvases();
                // Manual camera renders bypass the normal late-frame SortingGroup update.
                UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
                camera.Render();
                verify?.Invoke(camera);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/" + name, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = previousTarget;
                canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousDistance;
                scaler.SendMessage("Handle"); Canvas.ForceUpdateCanvases();
                Object.Destroy(texture); Object.Destroy(target);
                if (borrowed != null) Object.Destroy(borrowed);
            }
        }
    }
}
