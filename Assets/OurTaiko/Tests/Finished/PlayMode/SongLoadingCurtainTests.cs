using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongLoadingCurtainTests
    {
        [UnityTest]
        public IEnumerator PlayClosesTheCurtainLoadsBehindItAndOpensOverThePlayScene()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene); yield return null;
            var switcher = SceneSwitcher.Instance;
            var curtain = switcher.Curtain;
            Assert.That(curtain, Is.Not.Null, "The SceneSwitcher prefab carries the song loading curtain.");
            Assert.That(curtain.IsVisible, Is.False);
            var song = Object.FindFirstObjectByType<SongSelectScene>().songs.Single(s => s.name == TestSongs.TripleHelix);
            // A local song plays its WAVE file; drop any decode left by an earlier test.
            Assert.That(song.audioPath, Does.EndWith("TRIPLE HELIX.ogg"));
            song.SetPreparedAudio(null);

            // SongLoadingScene counts its minimum stay from its Start; the scene becomes active just
            // before that, while the switch itself only ends a frame and 50 ms later.
            float parkedAt = float.NaN;
            void OnActiveScene(Scene previous, Scene next)
            {
                if (next.name == SceneSwitcher.SongLoadingScene) parkedAt = Time.realtimeSinceStartup;
            }
            SceneManager.activeSceneChanged += OnActiveScene;
            switcher.Play(song);
            Assert.That(switcher.IsSwitching && curtain.IsVisible && !curtain.IsClosed, Is.True);
            Assert.That(curtain.title.text, Is.EqualTo("TRIPLE HELIX"));
            foreach (var text in new[] { curtain.title, curtain.subtitle })
            {
                Assert.That(text.font, Is.SameAs(SkinUi.Font), "All text uses the one UI font.");
                // Properties, not identity: TMP uses a per-page copy for glyphs on later atlas pages.
                var material = text.fontSharedMaterial;
                Assert.That(material.GetColor(ShaderUtilities.ID_OutlineColor), Is.EqualTo(Color.black));
                Assert.That(material.GetFloat(ShaderUtilities.ID_OutlineWidth), Is.EqualTo(SkinUi.OutlineWidth));
                Assert.That(material.GetFloat(ShaderUtilities.ID_FaceDilate), Is.EqualTo(SkinUi.OutlineWidth),
                    "The border must stay outside the white glyph.");
            }
            yield return WaitUntil(() => curtain.Frame >= 30);
            Assert.That(SceneSwitcher.CurrentScene, Is.EqualTo(SceneSwitcher.SongSelectScene), "The close runs over the old scene.");
            Capture("CurtainClosing.png");

            yield return WaitUntil(() => SceneSwitcher.CurrentScene == SceneSwitcher.SongLoadingScene && !switcher.IsSwitching);
            SceneManager.activeSceneChanged -= OnActiveScene;
            Assert.That(float.IsNaN(parkedAt), Is.False);
            var loader = Object.FindFirstObjectByType<SongLoadingScene>();
            Assert.That(switcher.IsCurtainClosed && switcher.IsCovered && switcher.IsInputBlocked, Is.True);
            Assert.That((curtain.Frame, curtain.InfoAlpha), Is.EqualTo((55.0, 1f)), "Parked on loading_song frame 55.");
            Assert.That(curtain.rainbow.enabled && !curtain.curtainLeft.enabled && !curtain.curtainRight.enabled, Is.True);
            Assert.That(curtain.don.color.a, Is.EqualTo(1).Within(1e-4));
            Assert.That(Vector2.Distance(curtain.don.rectTransform.anchoredPosition, new Vector2(160, -786)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(curtain.katsu.rectTransform.anchoredPosition, new Vector2(1760, -786)), Is.LessThan(0.01f));
            Assert.That(curtain.glow.color.a, Is.EqualTo(0.3008f).Within(1e-4));
            Assert.That(curtain.stars.All(s => s.enabled && Mathf.Approximately(s.color.a, 1)), Is.True);
            Assert.That(curtain.band.rectTransform.sizeDelta, Is.EqualTo(new Vector2(1600, 256)));
            yield return WaitUntil(() => loader.IsLoaded);
            Assert.That(song.HasPreparedAudio, Is.True, "The native song is ready before play.");
            Capture("SongLoading.png");

            yield return WaitUntil(() => SceneSwitcher.CurrentScene == SceneSwitcher.GameScene);
            Assert.That(Time.realtimeSinceStartup - parkedAt, Is.GreaterThanOrEqualTo(loader.minimumSeconds - 0.05f),
                "The parked title stays up for the minimum time.");
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(switcher.TakePreparedChart(song, null), Is.Null, "The play scene took the prepared chart.");
            Assert.That(play.Session, Is.Not.Null);
            Assert.That(play.Session.Chart.Title, Is.EqualTo("TRIPLE HELIX"));
            yield return WaitUntil(() => curtain.Frame >= 75);
            Assert.That(curtain.IsVisible && !curtain.IsClosed && switcher.IsInputBlocked, Is.True, "The open runs over the play scene.");
            Assert.That(curtain.InfoAlpha, Is.EqualTo(0));
            Capture("CurtainOpening.png");
            yield return WaitUntil(() => !switcher.IsSwitching);
            Assert.That(curtain.IsVisible || switcher.IsInputBlocked, Is.False);
            Assert.That(switcher.ReturnScene, Is.EqualTo(SceneSwitcher.SongSelectScene));

            // Restarting from the play scene keeps the ordinary fade.
            play.Restart();
            yield return null;
            Assert.That(switcher.IsSwitching && !curtain.IsVisible, Is.True);
            yield return WaitUntil(() => !switcher.IsSwitching);
            Assert.That(SceneSwitcher.CurrentScene, Is.EqualTo(SceneSwitcher.GameScene));
            Assert.That(Object.FindFirstObjectByType<PlayScene>().Session.Chart.Title, Is.EqualTo("TRIPLE HELIX"));
        }

        [UnityTest]
        public IEnumerator HalvesMapOntoTheArcadeFrames()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
            var curtain = SceneSwitcher.Instance.Curtain;
            curtain.ShowTime(0, true);
            Assert.That((curtain.Frame, curtain.InfoAlpha), Is.EqualTo((5.0, 0f)));
            Assert.That(curtain.curtainLeft.enabled && !curtain.rainbow.enabled, Is.True);
            Assert.That(curtain.curtainLeft.rectTransform.sizeDelta.x, Is.EqualTo(480));
            curtain.ShowTime(SongTransition.FadeDelay, true);
            Assert.That(curtain.InfoAlpha, Is.EqualTo(0), "The title waits for song_info_fade's delay.");
            curtain.ShowTime(SongTransition.SceneSeconds, true);
            Assert.That((curtain.Frame, curtain.InfoAlpha), Is.EqualTo((55.0, 1f)));
            curtain.ShowTime(0, false);
            Assert.That((curtain.Frame, curtain.InfoAlpha), Is.EqualTo((60.0, 1f)));
            curtain.ShowTime(SongTransition.FadeOutSeconds, false);
            Assert.That(curtain.InfoAlpha, Is.EqualTo(0));
            Assert.That(curtain.band.rectTransform.sizeDelta, Is.EqualTo(new Vector2(2400, 25.6f)), "The band squashes shut.");
            curtain.ShowTime(SongTransition.SceneSeconds, false);
            Assert.That(curtain.Frame, Is.EqualTo(109));
            Assert.That(curtain.curtainRight.enabled && curtain.don.color.a == 0, Is.True, "Curtains retract; don has flown off.");
            curtain.Hide();
        }

        static IEnumerator WaitUntil(System.Func<bool> done, float seconds = 20)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done()) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
        }

        // Renders the scene canvases and the global curtain canvas through the scene camera.
        static void Capture(string name)
        {
            var camera = Camera.main;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                foreach (var canvas in canvases)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                    canvas.GetComponent<UnityEngine.UI.CanvasScaler>()?.SendMessage("Handle");
                }
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/" + name, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                foreach (var canvas in canvases)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                    canvas.GetComponent<UnityEngine.UI.CanvasScaler>()?.SendMessage("Handle");
                }
                Canvas.ForceUpdateCanvases();
                Object.Destroy(texture); Object.Destroy(target);
            }
        }
    }
}
