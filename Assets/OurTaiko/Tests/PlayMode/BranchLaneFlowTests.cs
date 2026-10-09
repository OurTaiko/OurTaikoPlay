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
                    Assert.That(play.IsPaused, Is.False);
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
