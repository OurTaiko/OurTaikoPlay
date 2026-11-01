using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongLanguageFlowTests
    {
        [UnityTest]
        public IEnumerator LanguageMenuPersistsAndSongNamesUseTheChosenTranslationThroughoutPlay()
        {
            var settings = SettingManager.EnsureInstance();
            var previous = settings.Settings.Clone();
            string path = Path.Combine(Application.temporaryCachePath, "language-" + System.Guid.NewGuid() + ".json");
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "LanguageTestSong";
            song.chart = new TextAsset("TITLE:Base\nSUBTITLE:--Base subtitle\nTITLEJA:日本語の曲名\nSUBTITLEJA:--日本語の副題\nTITLEZH:中文歌名\nTITLEKO:Korean title\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n1000,\n#END");
            try
            {
                settings.Load(path);
                // The engine started with the player's settings file; closing would reload Entry to apply them.
                var apply = AudioEngine.EnsureInstance().ApplyPendingSettingsAsync();
                while (!apply.IsCompleted) yield return null;
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var overlay = SettingsOverlay.Open();
                var nav = overlay.Navigator;
                Assert.That(nav.Page.Title, Is.EqualTo("General"));
                Assert.That(nav.Page.Rows[0].Label, Is.EqualTo("Song Title Language"));
                overlay.Confirm(); overlay.Confirm(); overlay.Move(4);
                Assert.That(nav.SheetOptions[nav.SheetIndex], Is.EqualTo("Korean"));
                overlay.View.TapSheet(nav.SheetIndex);
                Assert.That(settings.Settings.general.language, Is.EqualTo("ko"));
                Assert.That(GameSettings.FromJson(File.ReadAllText(path)).general.language, Is.EqualTo("ko"));
                Assert.That(nav.Page.Rows[0].Label, Is.EqualTo("Song Title Language"), "The interface is not translated.");
                overlay.Back(); overlay.Back();
                Assert.That(SettingsOverlay.IsOpen, Is.False);
                yield return null; // the closing key's frame still blocks input
                Assert.That(song.ReadDisplayInfo().Title, Is.EqualTo("Korean title"));
                Assert.That(song.ReadDisplayInfo().Subtitle, Is.EqualTo("Base subtitle"), "Missing translations use the original, not Japanese.");
                Assert.That(song.ReadInfo().Title, Is.EqualTo("Base"));

                SceneSwitcher.Instance.Play(song, "Oni", true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindAnyObjectByType<PlayScene>();
                Assert.That(play.title.text, Is.EqualTo("Korean title"));
                Assert.That(play.subtitle.text, Does.StartWith("Base subtitle"));
                Assert.That(play.Session.Chart.Title, Is.EqualTo("Base"));
                Assert.That(SceneSwitcher.Instance.Curtain.title.text, Is.EqualTo("Korean title"));
                float deadline = Time.realtimeSinceStartup + 15;
                while (SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene)
                {
                    if (play != null && play.IsPaused) play.Resume();
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                yield return null;
                var result = Object.FindAnyObjectByType<ResultScene>();
                Assert.That(result.view.songTitle.text, Is.EqualTo("Korean title"));
                Assert.That(result.Result.ChartKey, Is.EqualTo(song.name));
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                settings.UseUnsaved(previous);
                if (File.Exists(path)) File.Delete(path);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (SceneManager.GetActiveScene().name != scene || SceneSwitcher.Instance.IsInputBlocked)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            yield return null;
        }
    }
}
