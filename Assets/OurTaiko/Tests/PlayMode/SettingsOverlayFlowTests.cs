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

        [UnityTest]
        public IEnumerator SidebarSwitchUpdatesTheVisibleRailAndSurvivesReopening()
        {
            Settings.UseUnsaved(new GameSettings());
            var overlay = SettingsOverlay.Open();
            yield return null;
            void CheckRail()
            {
                var entries = overlay.View.GetComponentsInChildren<PointerRelay>()
                    .Where(r => r.name.StartsWith("Category")).ToArray();
                var labels = entries.Select(e => e.GetComponentInChildren<TMPro.TMP_Text>().text);
                Assert.That(labels, Is.EqualTo(overlay.Navigator.Pages.Select(p => p.Title)));
                foreach (var entry in entries)
                {
                    var rect = (RectTransform)entry.transform;
                    Assert.That(rect.anchorMin.y, Is.EqualTo(rect.anchorMax.y));
                    Assert.That(rect.rect.height, Is.EqualTo(102f).Within(0.01f));
                }
                for (int i = 1; i < entries.Length; i++)
                {
                    var above = (RectTransform)entries[i - 1].transform;
                    var below = (RectTransform)entries[i].transform;
                    Assert.That(above.TransformPoint(above.rect.min).y, Is.GreaterThan(below.TransformPoint(below.rect.max).y));
                }
            }
            CheckRail();
            overlay.View.TapPage(overlay.Navigator.Pages.Count - 1);
            int controls = overlay.Navigator.Page.Rows.ToList().FindIndex(row => row.Id == "show-controls");
            overlay.View.SetRow(controls, 0);
            CheckRail();
            overlay.Close();
            overlay = SettingsOverlay.Open();
            Assert.That(overlay.Navigator.Pages.Select(p => p.Id), Does.Not.Contain("controls"));
            CheckRail();
            overlay.View.TapPage(overlay.Navigator.Pages.Count - 1);
            overlay.View.SetRow(controls, 1);
            Assert.That(overlay.Navigator.Pages.Select(p => p.Id), Does.Contain("controls"));
            CheckRail();
            overlay.Close();
        }

        [UnityTest]
        public IEnumerator DefaultSidebarShowsEntireCloseButtonBelowCategoryViewport()
        {
            Settings.UseUnsaved(new GameSettings());
            var overlay = SettingsOverlay.Open();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var close = (RectTransform)overlay.View.GetComponentsInChildren<PointerRelay>()
                .Single(button => button.GetComponentInChildren<TMPro.TMP_Text>()?.text == L10n.Get("Common", "close")
                    && button.name == "Close").transform;
            var rail = (RectTransform)overlay.View.transform.Find("Panel/Rail");
            var viewport = (RectTransform)rail.Find("Rail Viewport");
            Assert.That(close.parent, Is.EqualTo(rail));
            var corners = new Vector3[4];
            close.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Assert.That(rail.rect.Contains(rail.InverseTransformPoint(corner)), Is.True);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(Screen.safeArea.Contains(screen), Is.True);
            }
            float viewportBottom = viewport.TransformPoint(viewport.rect.min).y;
            Assert.That(corners[1].y, Is.LessThan(viewportBottom));
            overlay.Close();
        }

        [UnityTest]
        public IEnumerator SidebarFadeAppearsOnlyWhenMoreCategoriesAreBelow()
        {
            Settings.UseUnsaved(new GameSettings());
            var overlay = SettingsOverlay.Open();
            yield return null;
            var viewport = (RectTransform)overlay.View.transform.Find("Panel/Rail/Rail Viewport");
            var fade = viewport.GetComponentInChildren<RailScrollFade>(true);
            var scroll = viewport.GetComponent<SettingsScroll>();
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 663);
            scroll.Scrolled(0);
            Assert.That(fade.isActiveAndEnabled, Is.True);
            Assert.That(fade.raycastTarget, Is.False);

            scroll.Scrolled(1000);
            Assert.That(fade.isActiveAndEnabled, Is.False);
            scroll.Scrolled(-48);
            Assert.That(fade.isActiveAndEnabled, Is.True);

            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 800);
            yield return null;
            Assert.That(fade.isActiveAndEnabled, Is.False);
            overlay.Close();
        }

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
            for (int i = 0; i < 5; i++) { entry.Ka(1); yield return new WaitForSecondsRealtime(0.6f); }
            Assert.That(entry.Modes[entry.Flow.SelectedMode].Title, Is.EqualTo(L10n.Get("Menu", "entry.settings.title")));
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
            bool drumPad = Settings.Settings.play.singlePlayerDrumPad;
            overlay.Confirm();
            Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.EqualTo(!drumPad), "Don flips the drum pad.");
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
            Assert.That(nav.Row.Label, Is.EqualTo(L10n.Get("Settings", "play.practiceReview")));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo(L10n.Get("Settings", "value.on")));
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
