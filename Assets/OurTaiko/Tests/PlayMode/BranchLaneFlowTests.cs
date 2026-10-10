using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class BranchLaneFlowTests
    {
        [UnityTest]
        public IEnumerator PracticeLeadInDisplaysItsOwnRouteImmediately([Values(2, 4, 7)] int targetMeasure)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            const string bars = "1000,\n1000,\n1000,\n1000,\n1000,\n";
            song.chart = new TextAsset("TITLE:Practice Branch Lead In\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n0000,\n"
                + "#BRANCHSTART p,-1,0\n#N\n" + bars + "#E\n" + bars + "#M\n" + bars
                + "#BRANCHEND\n1000,\n1000,\n1000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.PracticeMode = true;
                switcher.Play(song, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 25;
                while (switcher.IsInputBlocked || SceneManager.GetActiveScene().name != SceneSwitcher.PracticeScene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                yield return null;
                play.MovePractice(1); play.MovePractice(1);
                play.ConfirmPractice(); yield return null;
                for (int i = 0; i < targetMeasure; i++) play.MovePractice(1);
                Assert.That(play.Practice.Target, Is.EqualTo(targetMeasure).Within(1e-6));
                play.ConfirmPractice(); yield return null;
                play.ConfirmPractice();
                while (play.IsPaused) yield return null;
                Assert.That(play.SongTime, Is.EqualTo(targetMeasure - 2).Within(.01));
                var lane = play.branchLane;
                bool startsInBranch = targetMeasure > 2;
                Assert.That(lane.currentLabel.sprite, Is.SameAs(startsInBranch ? lane.masterLabel : lane.normalLabel),
                    "The initial lane must describe the rewound position, even if the target is in a different section.");
                Assert.That(lane.currentLabel.color.a, Is.EqualTo(1).Within(.01));
                Assert.That(lane.levelChange.enabled, Is.False, "Seeking must not leave an upgrade animation waiting on the target time.");
                while (play.SongTime < targetMeasure)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    Assert.That(play.Session.Good + play.Session.Ok + play.Session.Bad + play.Session.Rolls, Is.Zero);
                    // Away from an actual section boundary, both the label and its opacity must be settled.
                    double t = play.SongTime;
                    if (t < 1 || t >= 1.4 && t < 6 || t >= 6.4)
                    {
                        bool inBranch = t >= 1 && t < 6;
                        Assert.That(lane.currentLabel.sprite, Is.SameAs(inBranch ? lane.masterLabel : lane.normalLabel));
                        Assert.That(lane.currentLabel.color.a, Is.EqualTo(1).Within(.01));
                        Assert.That(lane.background.enabled, Is.EqualTo(inBranch));
                    }
                    yield return null;
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator CommonMeasuresRestoreNormalLabelAndBackground([Values(false, true)] bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            const string routes = "\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND\n";
            song.chart = new TextAsset("TITLE:Common Branch Lane\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n0000,\n"
                + "#BRANCHSTART p,-1,0" + routes + "0000,\n0000,\n#BRANCHSTART p,0,101" + routes + "0000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.PracticeMode = practice;
                switcher.Play(song, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 30;
                string target = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                while (switcher.IsInputBlocked || SceneManager.GetActiveScene().name != target)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                var lane = play.branchLane;
                void Check(BranchRoute route)
                {
                    Assert.That(lane.currentLabel.sprite, Is.SameAs(route == BranchRoute.Normal ? lane.normalLabel
                        : route == BranchRoute.Expert ? lane.expertLabel : lane.masterLabel));
                    Assert.That(lane.background.enabled, Is.EqualTo(route != BranchRoute.Normal));
                    if (route != BranchRoute.Normal)
                        Assert.That(lane.background.sprite, Is.SameAs(route == BranchRoute.Expert ? lane.expertBackground : lane.masterBackground));
                }
                Check(BranchRoute.Normal);
                if (practice)
                {
                    yield return null;
                    play.MovePractice(1); play.MovePractice(1); // Fix the route to Master.
                    Check(BranchRoute.Normal);
                    play.ConfirmPractice(); yield return null;
                    play.MovePractice(1); Check(BranchRoute.Master);
                    play.MovePractice(1); Check(BranchRoute.Normal);
                    play.MovePractice(-1); Check(BranchRoute.Master);
                    play.MovePractice(-1); Check(BranchRoute.Normal);
                    play.ConfirmPractice(); yield return null;
                    play.ConfirmPractice();
                    while (play.IsPaused) yield return null;
                }
                double[] checkpoints = { .1, 1.1, 2.1, 3.9, 4.1, 5.1 };
                BranchRoute[] expected = { BranchRoute.Normal, BranchRoute.Master, BranchRoute.Normal,
                    BranchRoute.Normal, practice ? BranchRoute.Master : BranchRoute.Expert, BranchRoute.Normal };
                for (int i = 0; i < checkpoints.Length; i++)
                {
                    while (play.SongTime < checkpoints[i])
                    {
                        Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                        yield return null;
                    }
                    Check(expected[i]);
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
