using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SceneFlowTests
    {
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
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
            yield return null;
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session, Is.Not.Null);
            Assert.That(SceneSwitcher.Instance, Is.Not.Null);
            play.TogglePause();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
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
