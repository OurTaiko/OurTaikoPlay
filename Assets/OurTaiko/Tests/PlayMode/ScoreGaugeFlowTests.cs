using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class ScoreGaugeFlowTests
    {
        [UnityTest]
        public IEnumerator ShinuchiHudGaugeResultAndRestartShareTheSameSessionState()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.course = "Easy";
            song.chart = new TextAsset("TITLE:Shinuchi Gauge\nBPM:120\nCOURSE:Easy\nLEVEL:1\nBALLOON:2\n#START\n"
                + string.Concat(Enumerable.Repeat("1111,\n", 9)) + "7008,\n"
                + string.Concat(Enumerable.Repeat("1111,\n", 16)) + "#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                Assert.That(play.IsPaused, Is.True);
                play.pausePanel.SetActive(false);
                var session = play.Session;
                Assert.That(session.BaseScore, Is.EqualTo(10000));
                Assert.That(session.ClearThreshold, Is.EqualTo(0.6));

                // Drive exact judgments while the clock is paused; the real event handlers update the saved scene UI.
                for (int i = 0; i < 36; i++) session.Hit(false, session.Chart.Notes[i].Time);
                Assert.That(session.GaugePoints, Is.EqualTo(6000));
                Assert.That(session.IsClear && play.soulGauge.IsClear, Is.True);
                Assert.That(play.soulGauge.FilledCells, Is.EqualTo(30));
                Assert.That(play.score.text, Is.EqualTo("0360000"));
                play.soulGauge.ShowTime(play.SongTime + 0.225);
                Assert.That(play.soulGauge.cellFade.enabled, Is.True);
                Assert.That(play.soulGauge.cellFade.color.a, Is.EqualTo(0.5f).Within(0.001));

                session.Hit(false, 18.1); session.Hit(false, 18.1);
                Assert.That(session.Score, Is.EqualTo(360200));
                Assert.That(play.score.text, Is.EqualTo("0360200"));
                Assert.That(session.GaugePoints, Is.EqualTo(6000));
                Assert.That(play.soulGauge.cellFade.enabled, Is.True);
                Assert.That(play.soulGauge.cellFade.color.a, Is.EqualTo(0.5f).Within(0.001), "A balloon pop must not restart or cancel the cell fade.");

                Assert.That(session.Hit(false, 20.11), Is.EqualTo(Judgment.Bad));
                Assert.That(session.IsClear || play.soulGauge.IsClear, Is.False);
                Assert.That(play.soulGauge.FilledCells, Is.EqualTo(29));
                Assert.That(play.soulGauge.clearLabel.sprite.name, Is.EqualTo("clear_dark_ja"));
                session.Advance(session.Chart.Duration, true);
                Assert.That(session.GaugePoints, Is.EqualTo(10000));
                Assert.That(play.soulGauge.FilledCells, Is.EqualTo(50));
                Assert.That(play.soulGauge.IsFull, Is.True);
                Assert.That(play.score.text, Is.EqualTo("0990200"));
                play.SendMessage("Finish");
                Assert.That(play.IsFinished && play.resultPanel.activeSelf, Is.True);
                Assert.That(play.resultText.text, Does.StartWith("CLEAR!").And.Contains("990,200"));

                play.Restart();
                yield return WaitForScene(SceneSwitcher.GameScene);
                var restarted = Object.FindFirstObjectByType<PlayScene>();
                Assert.That(restarted.Session.Score, Is.Zero);
                Assert.That(restarted.Session.GaugePoints, Is.Zero);
                Assert.That(restarted.score.text, Is.EqualTo("0000000"));
                Assert.That(restarted.soulGauge.FilledCells, Is.Zero);
                Assert.That(restarted.soulGauge.IsClear, Is.False);
                restarted.Back();
                yield return WaitForScene(SceneSwitcher.MenuScene);
            }
            finally
            {
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
