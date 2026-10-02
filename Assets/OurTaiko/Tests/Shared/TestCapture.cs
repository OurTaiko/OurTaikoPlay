using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OurTaiko.Tests
{
    // Renders the active scene's Canvas through the main camera into TestResults/<name>.
    public static class TestCapture
    {
        public static Canvas SceneCanvas() => Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Single(canvas => canvas.gameObject.scene == SceneManager.GetActiveScene());

        public static void Capture(string name, int width = 1920, int height = 1080, System.Action<Camera> verify = null)
        {
            var canvas = SceneCanvas(); var camera = Camera.main;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            var previous = RenderTexture.active; var previousTarget = camera.targetTexture;
            var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera; float previousDistance = canvas.planeDistance;
            var target = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                scaler.SendMessage("Handle"); Canvas.ForceUpdateCanvases(); camera.Render();
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
            }
        }
    }
}
