using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class SettingsOverlayFlowTests
    {
        static SettingManager Settings => SettingManager.EnsureInstance();

        [TearDown]
        public void RestoreDefaults()
        {
            StopCapture();
            if (SettingsOverlay.Instance != null) Object.Destroy(SettingsOverlay.Instance.gameObject);
            Settings.UseUnsaved(new GameSettings());
        }

        static IEnumerator WaitUntil(Func<bool> done, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(until), "Timed out.");
                yield return null;
            }
        }

        // Entry with 1P joined and the ゲーム設定 board selected.
        static IEnumerator OpenEntrySettingsBoard(Action<EntryScene> use)
        {
            Settings.UseUnsaved(new GameSettings());
            // The engine started with the player's settings file; run it with the defaults.
            var apply = AudioEngine.EnsureInstance().ApplyPendingSettingsAsync();
            yield return WaitUntil(() => apply.IsCompleted, 10);
            if (apply.IsFaulted) throw apply.Exception;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            yield return new WaitForSecondsRealtime(0.2f);
            entry.Don();
            yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
            yield return new WaitForSecondsRealtime(0.8f);
            for (int i = 0; i < 3; i++) { entry.Ka(1); yield return new WaitForSecondsRealtime(0.6f); }
            Assert.That(entry.Modes[entry.Flow.SelectedMode].Title, Is.EqualTo("ゲーム設定"));
            use(entry);
        }

        // Captures render the overlay through its own camera at 1920x1080, as it is laid out on a
        // 16:9 screen (the batchmode screen is not 16:9), over Entry rendered by the main camera.
        static Camera overlayCamera;
        static RenderTexture overlayTarget;

        static void StartCapture()
        {
            overlayTarget = new RenderTexture(1920, 1080, 24);
            overlayCamera = new GameObject("OverlayCaptureCamera").AddComponent<Camera>();
            overlayCamera.enabled = false;
            overlayCamera.clearFlags = CameraClearFlags.Depth;
            overlayCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            overlayCamera.targetTexture = overlayTarget;
            var canvas = SettingsOverlay.Instance.View.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = overlayCamera; canvas.planeDistance = 1;
        }

        static void StopCapture()
        {
            if (overlayCamera != null) Object.Destroy(overlayCamera.gameObject);
            if (overlayTarget != null) Object.Destroy(overlayTarget);
        }

        static void Capture(string name)
        {
            var scene = TestCapture.SceneCanvas();
            var camera = Camera.main;
            var previous = (scene.renderMode, scene.worldCamera, scene.planeDistance, camera.targetTexture);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = overlayTarget;
                scene.renderMode = RenderMode.ScreenSpaceCamera; scene.worldCamera = camera; scene.planeDistance = 1;
                scene.GetComponent<CanvasScaler>().SendMessage("Handle");
                Canvas.ForceUpdateCanvases();
                UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
                camera.Render();
                overlayCamera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = overlayTarget;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                RenderTexture.active = active;
                Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/" + name, texture.EncodeToPNG());
            }
            finally
            {
                (scene.renderMode, scene.worldCamera, scene.planeDistance, camera.targetTexture) = previous;
                scene.GetComponent<CanvasScaler>().SendMessage("Handle");
                Object.Destroy(texture);
            }
        }

        static IEnumerator Settle() { yield return new WaitForSecondsRealtime(0.35f); }

        [UnityTest]
        public IEnumerator TheSettingsBoardOpensTheOverlayOverEntry()
        {
            EntryScene entry = null;
            yield return OpenEntrySettingsBoard(e => entry = e);
            entry.Don();
            Assert.That(SettingsOverlay.IsOpen, Is.True);
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.True, "Entry ignores its keys under the overlay.");
            Assert.That(entry.Flow.IsSelected, Is.False, "Entry stays on the mode list.");
            var overlay = SettingsOverlay.Instance;
            var nav = overlay.Navigator;
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
            StartCapture();
            yield return Settle();
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.EntryScene));
            Capture("SettingsOverlayRail.png");

            // Play: adjust Offset A with ka and keep it with don; it is saved at once.
            overlay.Move(2);
            overlay.Confirm();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
            Assert.That(nav.Row.Id, Is.EqualTo("audio-offset"));
            overlay.Confirm();
            for (int i = 0; i < 12; i++) overlay.Move(-1);
            yield return Settle();
            Capture("SettingsOverlayAdjust.png");
            overlay.Confirm();
            Assert.That(Settings.Settings.play.audioOffsetMs, Is.EqualTo(-12));

            overlay.Move(4);
            overlay.Confirm();
            Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.False, "Don flips the drum pad.");
            yield return Settle();
            Capture("SettingsOverlayPlay.png");

            // Leaving the rows and closing returns to Entry's mode list, which takes keys again
            // from the next frame on.
            overlay.Back();
            overlay.Move(10);
            Assert.That(nav.OnClose, Is.True);
            overlay.Confirm();
            Assert.That(SettingsOverlay.IsOpen, Is.False);
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.True, "The closing key does not reach Entry.");
            yield return null;
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.False);
            yield return Settle();
            Assert.That(overlay.View.IsShown, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.EntryScene));
        }

        [UnityTest]
        public IEnumerator PracticeTimingReviewToggleIsOnByDefaultAndAppliesImmediately()
        {
            yield return OpenEntrySettingsBoard(e => e.Don());
            var overlay = SettingsOverlay.Instance;
            overlay.Move(2); overlay.Confirm();
            var nav = overlay.Navigator;
            overlay.Move(nav.Page.Rows.ToList().FindIndex(r => r.Id == "practice-timing-review"));
            Assert.That(nav.Row.Label, Is.EqualTo("Practice Timing Review"));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("On"));
            StartCapture();
            yield return Settle();
            Capture("SettingsPracticeTimingReview.png");
            overlay.Confirm();
            Assert.That(Settings.Settings.play.practiceTimingReview, Is.False);
            Assert.That(GameSettings.FromJson(Settings.Settings.ToJson()).play.practiceTimingReview, Is.False);
            overlay.Confirm();
            Assert.That(Settings.Settings.play.practiceTimingReview, Is.True);
        }

        [UnityTest]
        public IEnumerator EveryPageShows()
        {
            yield return OpenEntrySettingsBoard(e => e.Don());
            StartCapture();
            var overlay = SettingsOverlay.Instance;
            var nav = overlay.Navigator;
            foreach (var page in nav.Pages.ToArray())
            {
                overlay.Confirm();
                yield return Settle();
                Capture("SettingsOverlay-" + page.Id + ".png");
                overlay.Back();
                overlay.Move(1);
            }
            // The language list's side sheet and the username's card.
            overlay.Move(-10);
            overlay.Confirm();
            overlay.Confirm();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Sheet));
            overlay.Move(1);
            yield return Settle();
            Capture("SettingsOverlaySheet.png");
            overlay.Back();
            overlay.Back();
            overlay.Move(5);
            overlay.Confirm();
            overlay.Confirm();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Typing));
            yield return Settle();
            Capture("SettingsOverlayTyping.png");
        }
    }
}
