using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class ResultScoreFlowTests
    {
        [UnityTest]
        public IEnumerator LocalScoreIsSavedOnlyOnEnteringResultAndOnlyOnce()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "Result Persistence Test";
            song.chart = new TextAsset("TITLE:Result Persistence Test\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.Play(song, "Oni", false);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                play.TogglePause();
                Assert.That(play.IsPaused, Is.True);
                play.Session.Advance(play.Session.Chart.Duration, true);
                Assert.That(play.Session.Score, Is.GreaterThan(0));
                play.SendMessage("Finish");
                Assert.That(ScoreStore.Shared.Get(song.name, Difficulty.Oni), Is.Null,
                    "Finishing gameplay must not write a score while ResultScene is still loading.");
                yield return WaitForScene(SceneSwitcher.ResultScene);
                var result = switcher.LastResult;
                Assert.That(ScoreStore.Shared.Get(song.name, Difficulty.Oni).score, Is.EqualTo(result.Score));
                Assert.That(result.PreviousBest, Is.Zero);
                int revision = ScoreStore.Shared.Revision;

                switcher.SwitchScene(SceneSwitcher.ResultScene);
                yield return WaitForScene(SceneSwitcher.ResultScene);
                Assert.That(ScoreStore.Shared.Revision, Is.EqualTo(revision));
                Assert.That(result.PreviousBest, Is.Zero, "Reopening must not replace the pre-play best with this run.");

                switcher.ShowResult(new PlayResult { ChartKey = song.name, Difficulty = Difficulty.Oni,
                    Score = result.Score + 1, AutoPlay = true }, song);
                yield return WaitForScene(SceneSwitcher.ResultScene);
                Assert.That(ScoreStore.Shared.Revision, Is.EqualTo(revision), "Auto play remains unsaved.");
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
            while (SceneSwitcher.Instance.IsInputBlocked || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
