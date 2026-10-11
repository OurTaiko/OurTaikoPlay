using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class PlayScoreRankFlowTests
    {
        [UnityTest] public IEnumerator SinglePlayShowsRankOnScoringAndClearsOnRestart() => Check(false);
        [UnityTest] public IEnumerator PracticeShowsRankOnScoringAndClearsOnSeek() => Check(true);

        static IEnumerator Check(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Play Score Rank\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n"
                + "1111,\n1111,\n1111,\n1111,\n1111,\n1000,\n0000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.PracticeMode = practice;
                switcher.Play(song, "Oni", false);
                string scene = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                double deadline = Time.realtimeSinceStartupAsDouble + 20;
                while (SceneManager.GetActiveScene().name != scene || switcher.IsInputBlocked)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                yield return null;
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var badge = play.scoreCounter.scoreRank;
                Assert.That(badge, Is.Not.Null);
                Assert.That(badge.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(((RectTransform)badge.transform).anchoredPosition, Is.EqualTo(new Vector2(152, 118)));
                Assert.That(badge.rank.image.rectTransform.sizeDelta, Is.EqualTo(Vector2.one * 208));
                Assert.That(badge.GetComponent<Canvas>().overrideSorting, Is.True);
                Assert.That(badge.GetComponent<Canvas>().sortingOrder,
                    Is.GreaterThan(play.judgeCounter.GetComponentInParent<Canvas>().sortingOrder));
                Assert.That((int)badge.rank.DisplayedRank, Is.Zero);
                Assert.That(badge.rank.image.enabled, Is.False);
                Assert.That(play.Session.BaseScore, Is.EqualTo(47620));
                Assert.That(play.Session.KiwamiThreshold, Is.EqualTo(1000020));
                for (int i = 0; i < 21; i++)
                {
                    play.Session.Hit(false, play.Session.Chart.Notes[i].Time);
                    int expected = i < 10 ? 0 : i < 12 ? 1 : i < 14 ? 2 : i < 16 ? 3 : i < 18 ? 4 : i == 18 ? 5 : i == 19 ? 6 : 7;
                    Assert.That((int)badge.rank.DisplayedRank, Is.EqualTo(expected), $"After hit {i + 1}");
                    Assert.That(badge.rank.image.enabled, Is.EqualTo(expected > 0));
                }
                Assert.That(badge.rank.image.sprite.name, Is.EqualTo("s86"));
                Assert.That(badge.rank.image.raycastTarget, Is.False);
                Assert.That(badge.rank.group.blocksRaycasts, Is.False);
                Assert.That(badge.rank.animationView, Is.Null, "HUD uses its own clip, not the result ceremony.");
                yield return new WaitForSecondsRealtime(.5f);
                Assert.That(badge.rank.group.alpha, Is.EqualTo(1).Within(.001));
                TestCapture.Capture("PlayScoreRank-" + scene + ".png");
                TestCapture.Capture("PlayScoreRank720-" + scene + ".png", 1280, 720);

                // Deterministic clock samples: same-rank increments do not restart the
                // presentation; another rank does, including after a practice score reset.
                badge.ShowScore(0, 100);
                Assert.That(badge.rank.image.enabled, Is.False);
                badge.ShowScore(500000, 100);
                badge.ShowTime(100.25);
                Assert.That(badge.rank.group.alpha, Is.EqualTo(1).Within(.001));
                badge.ShowScore(599999, 102);
                badge.ShowTime(103);
                Assert.That(badge.rank.group.alpha, Is.Zero);
                badge.ShowScore(600000, 103);
                badge.ShowTime(103.25);
                Assert.That(badge.rank.group.alpha, Is.EqualTo(1).Within(.001));
                Assert.That(badge.rank.image.sprite.name, Is.EqualTo("s76"));
                play.scoreCounter.Show(1000000, play.Session.KiwamiThreshold);
                Assert.That((int)badge.rank.DisplayedRank, Is.EqualTo(6));
                play.scoreCounter.Show(1000020, play.Session.KiwamiThreshold);
                Assert.That((int)badge.rank.DisplayedRank, Is.EqualTo(7));
                if (practice)
                {
                    play.MovePractice(1);
                    Assert.That(play.scoreCounter.Text, Is.EqualTo("0"));
                    Assert.That((int)badge.rank.DisplayedRank, Is.Zero);
                    Assert.That(badge.rank.image.enabled, Is.False);
                    play.Session.Hit(false, play.Session.Chart.Notes[4].Time);
                    Assert.That(play.Session.Score, Is.EqualTo(47620));
                    Assert.That(play.Session.KiwamiThreshold, Is.EqualTo(1000020));
                    Assert.That((int)badge.rank.DisplayedRank, Is.Zero);
                }
                else
                {
                    play.Restart();
                    deadline = Time.realtimeSinceStartupAsDouble + 20;
                    do
                    {
                        Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline));
                        yield return null;
                    } while (play != null || switcher.IsInputBlocked);
                    var restarted = Object.FindFirstObjectByType<PlayScene>();
                    Assert.That((int)restarted.scoreCounter.scoreRank.rank.DisplayedRank, Is.Zero);
                    Assert.That(restarted.scoreCounter.scoreRank.rank.image.enabled, Is.False);
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
