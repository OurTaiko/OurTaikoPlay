using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SceneFlowTests
    {
        [UnityTest] public IEnumerator BranchNormalCanBePlayed() => PlayBranch(BranchRoute.Normal);
        [UnityTest] public IEnumerator BranchExpertCanBePlayed() => PlayBranch(BranchRoute.Expert);
        [UnityTest] public IEnumerator BranchMasterAutoPlayCompletes() => PlayBranch(BranchRoute.Master);

        static IEnumerator PlayBranch(BranchRoute expected)
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var menu = Object.FindFirstObjectByType<LaunchMenu>();
            var song = menu.songs.Single(s => s.name == "BranchTraining");
            SceneSwitcher.Instance.Play(song, expected == BranchRoute.Master);
            yield return WaitForScene(SceneSwitcher.GameScene);
            var play = Object.FindFirstObjectByType<PlayScene>();
            float deadline = Time.realtimeSinceStartup + 40;
            int hits = 0;
            while (play.Session.BranchHistory.Count == 0)
            {
                if (play.IsPaused) play.TogglePause();
                if (expected == BranchRoute.Expert && hits < 2 && play.SongTime >= hits * 2)
                {
                    play.Hit(false, false); hits++;
                }
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(play.Session.CurrentBranch, Is.EqualTo(expected));
            var branch = play.branchLane;
            Assert.That(branch.gameObject.activeInHierarchy, Is.True);
            Assert.That(branch.currentLabel.sprite.name, Is.EqualTo(expected.ToString().ToLowerInvariant()));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(1071, -43)));
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(1));
            Assert.That(branch.background.enabled, Is.EqualTo(expected != BranchRoute.Normal));
            if (expected != BranchRoute.Normal)
            {
                Assert.That(branch.background.sprite.name, Is.EqualTo(expected.ToString().ToLowerInvariant() + "_bg"));
                Assert.That(branch.background.color.a, Is.EqualTo(0.5f));
            }
            Assert.That(branch.transform.GetSiblingIndex(), Is.LessThan(play.noteLayer.parent.GetSiblingIndex()));
            Assert.That(branch.transform.parent.parent.Find("BranchPanel"), Is.Null);
            Assert.That(play.Session.Chart.Notes.Select((n, i) => n.BranchId != 0 || n.Route == expected || !play.noteLayer.GetChild(i).gameObject.activeSelf).All(x => x), Is.True);
            Assert.That(play.Session.Chart.Notes.Select((n, i) => n.BranchId == 0 && n.Route == expected && play.noteLayer.GetChild(i).gameObject.activeSelf).Any(x => x), Is.True);
            Assert.That(play.Session.Chart.Bars.Select((n, i) => n.BranchId != 0 || n.Route == expected || !play.barLayer.GetChild(i).gameObject.activeSelf).All(x => x), Is.True);
            Capture("Branch" + expected + ".png");
            play.TogglePause(); double pausedAt = play.SongTime;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(play.SongTime, Is.EqualTo(pausedAt).Within(0.001));
            Assert.That(play.Session.CurrentBranch, Is.EqualTo(expected));
            play.TogglePause();
            if (expected == BranchRoute.Master)
            {
                while (!play.IsFinished)
                {
                    if (play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                Assert.That(play.Session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
                Assert.That(play.Session.Bad, Is.Zero);
                Assert.That(play.Session.Good, Is.EqualTo(play.Session.Chart.Notes.Count(n => !n.IsLong && play.Session.IsActive(n))));
                Assert.That(play.resultPanel.activeSelf, Is.True);
            }
            play.Restart(); yield return WaitForScene(SceneSwitcher.GameScene);
            var restarted = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(restarted.Session.BranchHistory.Count, Is.Zero);
            Assert.That(restarted.Session.Score, Is.Zero);
            Assert.That(restarted.branchLane.currentLabel.sprite.name, Is.EqualTo("normal"));
            Assert.That(restarted.branchLane.background.enabled, Is.False);
            restarted.Back(); yield return WaitForScene(SceneSwitcher.MenuScene);
        }

        [UnityTest]
        public IEnumerator BranchLaneTransitionsMatchOriginalSkin()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var menu = Object.FindFirstObjectByType<LaunchMenu>();
            SceneSwitcher.Instance.Play(menu.songs.Single(s => s.name == "BranchTraining"), true);
            yield return WaitForScene(SceneSwitcher.GameScene);
            var play = Object.FindFirstObjectByType<PlayScene>();
            if (!play.IsPaused) play.TogglePause();
            var branch = play.branchLane;
            branch.Select(BranchRoute.Expert, 0);
            branch.ShowTime(0.05);
            Assert.That(branch.previousLabel.sprite.name, Is.EqualTo("normal"));
            Assert.That(branch.previousLabel.rectTransform.anchoredPosition.y, Is.EqualTo(-58).Within(0.001));
            Assert.That(branch.currentLabel.color.a, Is.Zero);
            Assert.That(branch.levelChange.sprite.name, Is.EqualTo("level_up"));
            branch.ShowTime(0.1665);
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(0.5f).Within(0.001));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition.y, Is.EqualTo(-60.5f).Within(0.001));
            float frozenAlpha = branch.currentLabel.color.a;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(frozenAlpha), "Pause must freeze the branch transition.");
            branch.ShowTime(1.4);
            Assert.That(branch.previousLabel.enabled, Is.False);
            Assert.That(branch.levelChange.enabled, Is.False);
            branch.Select(BranchRoute.Expert, 2);
            Assert.That(branch.levelChange.enabled, Is.False, "Selecting the same route must not replay the animation.");
            branch.Select(BranchRoute.Master, 3);
            branch.ShowTime(4.4);
            Assert.That(branch.background.sprite.name, Is.EqualTo("master_bg"));
            Assert.That(branch.background.color.a, Is.EqualTo(0.5f));
            branch.Select(BranchRoute.Normal, 5);
            branch.ShowTime(5.05);
            Assert.That(branch.levelChange.sprite.name, Is.EqualTo("level_down"));
            Assert.That(branch.previousLabel.sprite.name, Is.EqualTo("master"));
            Assert.That(branch.previousLabel.rectTransform.anchoredPosition.y, Is.EqualTo(-28).Within(0.001));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition.y, Is.EqualTo(27).Within(0.001));
            Assert.That(branch.background.enabled, Is.False);
            branch.ShowTime(6.4);
            Assert.That(branch.currentLabel.sprite.name, Is.EqualTo("normal"));
            Assert.That(branch.currentLabel.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(1071, -43)));
            Assert.That(branch.currentLabel.color.a, Is.EqualTo(1));
            play.Back(); yield return WaitForScene(SceneSwitcher.MenuScene);
        }

        [UnityTest]
        public IEnumerator RenderedNoteTravelsAtOriginalSkinSpeed()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            var rect = (RectTransform)play.noteLayer.GetChild(0);
            float deadline = Time.realtimeSinceStartup + 5;
            do
            {
                if (play.IsPaused) play.TogglePause();
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            } while (!rect.gameObject.activeSelf || play.RenderedTime < -1);
            // DSP time can advance by an audio block after Update; sample the timestamp
            // actually used by the renderer together with its matching position.
            double timeBefore = play.RenderedTime;
            float xBefore = rect.anchoredPosition.x;
            yield return new WaitForSecondsRealtime(0.15f);
            double elapsed = play.RenderedTime - timeBefore;
            Assert.That(elapsed, Is.GreaterThan(0));
            // Original skin: screen width 1280, judge x 414; default song starts at BPM 160 / SCROLL 1.
            double expectedDistance = elapsed * 160 / 240 * (1280 - 414);
            Assert.That(xBefore - rect.anchoredPosition.x, Is.EqualTo(expectedDistance).Within(0.01));
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }

        [UnityTest]
        public IEnumerator MenuPlayPauseResumeRestartAndReturn()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var menu = Object.FindFirstObjectByType<LaunchMenu>();
            Assert.That(menu, Is.Not.Null);
            Capture("MenuScene.png");
            SceneSwitcher.Instance.Play(menu.songs[0], true);
            yield return WaitForScene(SceneSwitcher.GameScene);
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session.Chart.Notes.Count, Is.GreaterThan(50));
            Assert.That(play.branchLane.gameObject.activeSelf, Is.False, "Non-branch charts must not show route labels or tints.");
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            if (play.IsPaused) play.TogglePause();
            yield return new WaitForSecondsRealtime(2.4f);
            Assert.That(play.Session.Good, Is.GreaterThan(0));
            Assert.That(play.music.isPlaying, Is.True);
            Assert.That(play.music.time, Is.EqualTo(play.SongTime).Within(0.15));
            play.TogglePause(); double pausedAt = play.SongTime;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(play.SongTime, Is.EqualTo(pausedAt).Within(0.001));
            Assert.That(play.music.isPlaying, Is.False);
            play.TogglePause();
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(play.music.time, Is.EqualTo(play.SongTime).Within(0.15));
            Capture("PlayScene.png");
            play.Restart();
            yield return WaitForScene(SceneSwitcher.GameScene);
            var restarted = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(restarted, Is.Not.SameAs(play));
            Assert.That(restarted.Session.Score, Is.Zero);
            Assert.That(Object.FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            restarted.Back();
            yield return WaitForScene(SceneSwitcher.MenuScene);
            Assert.That(Object.FindFirstObjectByType<PlayScene>(), Is.Null);
            Assert.That(Object.FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PlaySceneCanBeOpenedDirectly()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 2;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session, Is.Not.Null);
            Assert.That(SceneSwitcher.Instance, Is.Not.Null);
            Assert.That(Application.targetFrameRate, Is.EqualTo(120));
            Assert.That(QualitySettings.vSyncCount, Is.Zero);
            Assert.That(UnityEngine.Rendering.OnDemandRendering.renderFrameInterval, Is.EqualTo(1));
            var fps = Object.FindFirstObjectByType<FpsCounter>();
            Assert.That(fps, Is.Not.Null);
            if (!play.IsPaused) play.TogglePause();
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(fps.FramesPerSecond, Is.GreaterThan(0));
                var label = fps.GetComponent<TMPro.TMP_Text>();
                Assert.That(label.text, Does.StartWith("FPS ").And.Not.Contains("--"));
                Assert.That(label.raycastTarget, Is.False);
            }
            finally { Time.timeScale = originalTimeScale; }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            Assert.That(Object.FindObjectsByType<FpsCounter>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }

        static void Capture(string name)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>(); var camera = Camera.main;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/" + name, texture.EncodeToPNG());
            RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; camera.targetTexture = null;
            Object.Destroy(texture); Object.Destroy(target);
        }
    }
}
