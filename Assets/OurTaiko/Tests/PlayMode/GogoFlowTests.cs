using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class GogoFlowTests
    {
        [UnityTest] public IEnumerator NormalPlayRendersGogo() => Exercise(false);
        [UnityTest] public IEnumerator PracticeRendersGogoWhileSeeking() => Exercise(true);

        IEnumerator Exercise(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:GOGO Visual Test\nBPM:240\nCOURSE:Oni\nLEVEL:5\n#START\n1000,\n#GOGOSTART\n1000,\n1000,\n#GOGOEND\n1000,\n#END");
            try
            {
                SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.PracticeMode = practice;
                SceneSwitcher.Instance.Play(song, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 20;
                do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name !=
                    (practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene));
                var play = Object.FindAnyObjectByType<PlayScene>();
                while (play.Session == null) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                if (!play.IsPaused) play.TogglePause();
                play.enabled = false; // Keep the clock still while exercising the actual scene rendering path.
                play.pausePanel.SetActive(false);
                if (practice) play.practiceView.panel.SetActive(false);
                var render = typeof(PlayScene).GetMethod("RenderNotes", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (double time in new[] { 0.5, 1.04, 1.2, 1.6, 3.0, 1.04, 0.5 })
                {
                    render.Invoke(play, new object[] { time });
                    Assert.That(play.gogo.fire.enabled, Is.EqualTo(time >= 1 && time < 3));
                    Assert.That(play.gogo.explosions.Any(i => i.enabled), Is.EqualTo(time >= 1 && time < 1.5));
                    if (time == 1.2 || time == 1.6)
                    {
                        yield return null;
                        TestCapture.Capture((practice ? "GogoPractice-" : "GogoPlay-") + time.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                    }
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }
    }
}
