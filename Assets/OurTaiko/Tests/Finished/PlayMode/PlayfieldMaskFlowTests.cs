using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OurTaiko.Tests
{
    public sealed class PlayfieldMaskFlowTests
    {
        // Issue #43: whatever the Game view's shape, the bars and the 1920x1080 viewport tile the screen.
        [UnityTest]
        public IEnumerator BarsCoverEverythingOutsideTheViewport()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestSongs.Select();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            yield return null;
            var play = Object.FindAnyObjectByType<PlayScene>();
            // Let notes pass the judge circle and slide off the lane's left edge.
            yield return new WaitForSecondsRealtime(3);
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
            if (!Application.isBatchMode)
            {
                System.IO.Directory.CreateDirectory("TestResults");
                ScreenCapture.CaptureScreenshot("TestResults/PlayfieldMask.png");
                yield return null;
                yield return null;
            }

            var mask = Object.FindAnyObjectByType<PlayfieldMask>();
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.gameObject.scene, Is.EqualTo(play.gameObject.scene));
            Assert.That(mask.Viewport.name, Is.EqualTo(PlayfieldMask.ViewportName));
            var canvas = mask.GetComponent<Canvas>();
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.sortingOrder, Is.GreaterThan(play.GetComponentInParent<Canvas>()?.sortingOrder ?? 0));

            var root = mask.Viewport.GetComponentInParent<Canvas>().rootCanvas;
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var corners = new Vector3[4];
            mask.Viewport.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            float covered = (max.x - min.x) * (max.y - min.y);
            foreach (var image in mask.GetComponentsInChildren<Image>())
            {
                Assert.That(image.color, Is.EqualTo(Color.black));
                Assert.That(image.raycastTarget, Is.False);
                var size = image.rectTransform.rect.size;
                covered += size.x * size.y;
                // No bar reaches into the viewport.
                var bar = new Rect(image.rectTransform.anchoredPosition, size);
                var inside = Rect.MinMaxRect(min.x + 1, min.y + 1, max.x - 1, max.y - 1);
                Assert.That(bar.width == 0 || bar.height == 0 || !bar.Overlaps(inside), Is.True, image.name);
            }
            Assert.That(covered, Is.EqualTo((float)Screen.width * Screen.height).Within(Screen.width + Screen.height + 4f));
        }
    }
}
