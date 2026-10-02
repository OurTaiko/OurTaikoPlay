using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class PlayOptionsFlowTests
    {
        [UnityTest]
        public IEnumerator PlaySceneAppliesTheSavedOptions()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.course = "Oni";
            song.chart = new TextAsset("TITLE:Options\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n"
                + string.Concat(Enumerable.Repeat("1111,\n", 8)) + "#END");
            PlayOptions.Shared = new PlayOptions { display = true, inverse = true, speed = 20, neiro = PlayOptions.Mute };
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                var notes = play.Session.Chart.Notes;
                Assert.That(notes.All(n => n.Kind == NoteKind.Ka && !n.Display), Is.True, "あべこべ swaps, ドロン hides.");
                Assert.That(notes[0].ScrollX, Is.EqualTo(2));
                Assert.That(play.don == null && play.ka == null, Is.True, "無音 has no hit sounds.");
                Assert.That(play.modifierBadges.Count, Is.EqualTo(3), "Speed, doron and abekobe badges.");
                // At x2.0 the first note is on screen half a second before it is due.
                float deadline = Time.realtimeSinceStartup + 10;
                while (play.RenderedTime < -0.4) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
                Assert.That(play.noteLayer.Cast<Transform>().Any(t => t.gameObject.activeSelf), Is.False);
                SceneFlowTests.Capture("PlayOptionsBadges.png");
                Assert.That(play.Session.Hit(true, notes[0].Time), Is.EqualTo(Judgment.Good), "Hidden notes are still judged.");
                play.Back();
                yield return WaitForScene(SceneSwitcher.MenuScene);
            }
            finally
            {
                PlayOptions.Shared = new PlayOptions();
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
