using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class ScoreAdditionFlowTests
    {
        [UnityTest] public IEnumerator SinglePlayShowsActualScoreIncreases() => Check(false);
        [UnityTest] public IEnumerator PracticeShowsIncreasesAndClearsThemOnSeek() => Check(true);

        static IEnumerator Check(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Score Addition\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:2,2\n#START\n"
                + "1110,\n5008,\n7008,\n9008,\n0000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                var switcher = SceneSwitcher.Instance; switcher.PracticeMode = practice;
                switcher.Play(song, "Oni", false);
                float deadline = Time.realtimeSinceStartup + 20;
                string scene = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                while (SceneManager.GetActiveScene().name != scene || switcher.IsInputBlocked)
                { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
                yield return null;
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                // Hide the ordinary pause menu only for inspecting the saved score UI underneath.
                play.pausePanel.SetActive(false);
                var counter = play.scoreCounter;
                Assert.That(counter.additionTemplate, Is.Not.Null);
                Assert.That(counter.Additions.Count, Is.Zero, "The initial total is not an earned score.");
                var notes = play.Session.Chart.Notes;
                play.Session.Hit(false, notes[0].Time);
                Assert.That(counter.Additions.Single(a => a.gameObject.activeSelf).Text, Is.EqualTo(play.Session.BaseScore.ToString()));
                yield return new WaitForSecondsRealtime(.2f);
                TestCapture.Capture("ScoreAddition-" + scene + ".png");
                TestCapture.Capture("ScoreAddition720-" + scene + ".png", 1280, 720);
                int before = play.Session.Score;
                play.Session.Hit(false, notes[1].Time + .05);
                var active = counter.Additions.Where(a => a.gameObject.activeSelf).ToArray();
                Assert.That(active.Length, Is.EqualTo(2), "Successive hits each have their own animation.");
                Assert.That(active[1].Text, Is.EqualTo((play.Session.Score - before).ToString()));
                play.Session.Hit(false, notes[2].Time + .09);
                Assert.That(counter.Additions.Count(a => a.gameObject.activeSelf), Is.EqualTo(2), "Bad judgments award no points.");
                play.Session.Hit(false, notes[3].Time);
                play.Session.Hit(false, notes[4].Time);
                play.Session.Hit(false, notes[4].Time + .01);
                play.Session.Hit(false, notes[5].Time);
                play.Session.Hit(false, notes[5].Time + .01);
                Assert.That(counter.Additions.Count(a => a.gameObject.activeSelf && a.Text == "100"), Is.EqualTo(5));
                Assert.That(play.Session.Resolved[4] && play.Session.Resolved[5], Is.True);
                Assert.That(counter.Text, Is.EqualTo(play.Session.Score.ToString()));
                Assert.That(counter.Additions.All(a => a.digits.All(d => !d.raycastTarget)), Is.True);
                yield return new WaitForSecondsRealtime(.2f);
                var visible = counter.Additions.First(a => a.gameObject.activeSelf);
                Assert.That(visible.GetComponent<Canvas>().overrideSorting, Is.True, "Floating digits must render above JudgeCounter.");
                Assert.That(visible.GetComponent<CanvasGroup>().alpha, Is.GreaterThan(.99f));
                Assert.That(visible.digits[0].color, Is.EqualTo((Color)new Color32(254, 102, 0, 255)));
                if (practice)
                {
                    play.MovePractice(1);
                    Assert.That(counter.Additions.All(a => !a.gameObject.activeSelf), Is.True, "Practice seek clears old additions.");
                    Assert.That(counter.Text, Is.EqualTo("0"));
                }
                else
                {
                    yield return new WaitForSecondsRealtime(.3f);
                    Assert.That(counter.Additions.All(a => !a.gameObject.activeSelf), Is.True);
                }
                int pooled = counter.Additions.Count;
                counter.Show(play.Session.Score + 100);
                Assert.That(counter.Additions.Count, Is.EqualTo(pooled), "Expired rows are reused.");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
