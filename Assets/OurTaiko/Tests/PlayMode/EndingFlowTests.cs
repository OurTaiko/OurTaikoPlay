using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class EndingFlowTests
    {
        [UnityTest] public IEnumerator FailPlaysBeforeResult() => Check(0);
        [UnityTest] public IEnumerator ClearPlaysBeforeResult() => Check(1);
        [UnityTest] public IEnumerator FullComboPlaysBeforeResult() => Check(2);
        [UnityTest] public IEnumerator DonderfulPlaysBeforeResult() => Check(3);

        [UnityTest]
        public IEnumerator NaturalAutoplayCompletionRunsEndingBeforeResult()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "NaturalEnding";
            song.chart = new TextAsset("TITLE:Ending\nBPM:120\nCOURSE:Easy\nLEVEL:1\n#START\n1111,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.Play(song, "Easy", true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                float deadline = Time.realtimeSinceStartup + 15;
                while (!play.IsFinished)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(play.Result.AutoPlay, Is.True);
                Assert.That(play.Result.ResultCrown, Is.EqualTo(Crown.DonderfulCombo));
                Assert.That(play.ending.gameObject.activeSelf, Is.True);
                Assert.That(switcher.IsSwitching, Is.False);
                var dancerFrames = new HashSet<Sprite>();
                var fireFrames = new HashSet<Sprite>();
                var rainbowFrames = new HashSet<Sprite>();
                var dancerImage = play.dancers[0].GetComponent<UnityEngine.UI.Image>();
                double songTime = play.SongTime;
                int version = play.Session.Version;
                double observeUntil = GameTimeline.FrameTime + .45;
                while (GameTimeline.FrameTime < observeUntil)
                {
                    dancerFrames.Add(dancerImage.sprite);
                    fireFrames.Add(play.soulGauge.fire.sprite);
                    rainbowFrames.Add(play.soulGauge.rainbowA.sprite);
                    Assert.That(play.Session.Version, Is.EqualTo(version), "Presentation must not advance judgment after the result is captured.");
                    Assert.That(play.SongTime, Is.EqualTo(songTime));
                    yield return null;
                }
                Assert.That(dancerFrames.Count, Is.GreaterThan(1), "Dancers must keep animating during the ending.");
                Assert.That(fireFrames.Count, Is.GreaterThan(1), "Soul fire must keep animating during the ending.");
                Assert.That(rainbowFrames.Count, Is.GreaterThan(1), "The full gauge must keep cycling during the ending.");
                yield return new WaitForSecondsRealtime(3.05f);
                TestCapture.Capture("EndingNatural-Donderful.png");
                yield return WaitForScene(SceneSwitcher.ResultScene);
                Assert.That(switcher.LastResult.Good, Is.EqualTo(4));
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        static IEnumerator Check(int kind)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "EndingFlow" + kind;
            song.chart = new TextAsset("TITLE:Ending\nBPM:120\nCOURSE:Easy\nLEVEL:1\n#START\n"
                + string.Concat(Enumerable.Repeat("1111,\n", 25)) + "#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.Play(song, "Easy", false);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var session = play.Session;
                for (int i = 0; i < session.Chart.Notes.Count; i++)
                {
                    if (kind == 0) continue;
                    session.Hit(false, session.Chart.Notes[i].Time + (i == 0 ? kind == 1 ? .11 : kind == 2 ? .05 : 0 : 0));
                }
                session.Advance(session.Chart.Duration + 1, false);
                play.SendMessage("Finish");
                var result = play.Result;
                Assert.That((int)result.ResultCrown, Is.EqualTo(kind));
                Assert.That(play.IsFinished, Is.True);
                Assert.That(play.ending.gameObject.activeSelf, Is.True);
                Assert.That(play.pauseButton.interactable, Is.False);
                Assert.That(play.drumPad.enabled, Is.False);
                Assert.That(switcher.IsSwitching, Is.False, "The curtain must wait for the ending.");
                double started = GameTimeline.FrameTime;
                var record = play.Record;
                int inputs = record.Inputs.Count;
                int score = session.Score;
                play.Hit(false, false); play.TogglePause();
                Assert.That(record.Inputs.Count, Is.EqualTo(inputs));
                Assert.That(session.Score, Is.EqualTo(score));
                yield return new WaitForSecondsRealtime(1.2f);
                Assert.That(play.hitFace.IsPlaying || play.hitRing.IsPlaying, Is.False,
                    "Existing hit effects must expire instead of freezing over the ending.");
                Assert.That(play.noteArcs.ActiveCount, Is.Zero, "Existing flying notes must finish their animation.");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.GameScene));
                TestCapture.Capture("Ending-" + kind + ".png");
                TestCapture.Capture("Ending720-" + kind + ".png", 1280, 720);
                while (GameTimeline.FrameTime - started < play.ending.Duration - .1)
                {
                    Assert.That(switcher.IsSwitching, Is.False);
                    yield return null;
                }
                TestCapture.Capture("EndingFinal-" + kind + ".png");
                yield return WaitForScene(SceneSwitcher.ResultScene);
                Assert.That(switcher.LastResult, Is.SameAs(result));
                Assert.That(Object.FindFirstObjectByType<ResultScene>().Result, Is.SameAs(result));
                switcher.Restart();
                yield return WaitForScene(SceneSwitcher.GameScene);
                var restarted = Object.FindFirstObjectByType<PlayScene>();
                Assert.That(restarted.IsFinished, Is.False);
                Assert.That(restarted.ending.gameObject.activeSelf, Is.False);
                Assert.That(restarted.pauseButton.interactable, Is.True);
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
