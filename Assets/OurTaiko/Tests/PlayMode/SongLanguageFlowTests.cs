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
        public IEnumerator LanguageMenuPersistsAndSongNamesUseJapaneseFallbackThroughoutPlay()
        {
            var settings = SettingManager.EnsureInstance();
            var previous = settings.Settings.Clone();
            string path = Path.Combine(Application.temporaryCachePath, "language-" + System.Guid.NewGuid() + ".json");
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "LanguageTestSong";
            song.chart = new TextAsset("TITLE:Base\nTITLEJA:日本語の曲名\nSUBTITLEJA:--日本語の副題\nTITLEZH:中文歌名\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n1000,\n#END");
            try
            {
                settings.Load(path);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
                yield return null;
                var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
                Assert.That(scene.Menu.CurrentType.Label, Is.EqualTo("General"));
                Assert.That(scene.view.itemRows[0].label.text, Is.EqualTo("Language"));
                TestCapture.Capture("SettingsGeneral.png");
                scene.Don(); scene.Don(); scene.Ka(4);
                Assert.That(scene.view.choiceRows[2].label.text, Is.EqualTo("Korean"));
                TestCapture.Capture("SettingsLanguage.png");
                scene.view.choiceRows[2].click.Clicked();
                Assert.That(settings.Settings.general.language, Is.EqualTo("ko"));
                Assert.That(GameSettings.FromJson(File.ReadAllText(path)).general.language, Is.EqualTo("ko"));
                Assert.That(scene.view.itemRows[0].label.text, Is.EqualTo("Language"), "The interface is not translated.");
                Assert.That(song.ReadDisplayInfo().Title, Is.EqualTo("日本語の曲名"));
                Assert.That(song.ReadInfo().Title, Is.EqualTo("Base"));

                SceneSwitcher.Instance.Play(song, "Oni", true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                Assert.That(play.title.text, Is.EqualTo("日本語の曲名"));
                Assert.That(play.subtitle.text, Does.StartWith("日本語の副題"));
                Assert.That(play.Session.Chart.Title, Is.EqualTo("Base"));
                Assert.That(SceneSwitcher.Instance.Curtain.title.text, Is.EqualTo("日本語の曲名"));
                float deadline = Time.realtimeSinceStartup + 15;
                while (SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene)
                {
                    if (play != null && play.IsPaused) play.Resume();
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                yield return null;
                var result = Object.FindFirstObjectByType<ResultScene>();
                Assert.That(result.view.songTitle.text, Is.EqualTo("日本語の曲名"));
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
