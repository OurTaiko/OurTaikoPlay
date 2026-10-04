using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class ScoreRankFlowTests
    {
        [UnityTest]
        public IEnumerator RankAppearsInAllThreePlacesAndClearsOnUnplayedSongs()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(.6f);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            var song = select.FocusedSong;
            var board = select.view.songBoards.Single(b => b.song == song);
            Assert.That(board.scoreRank.DisplayedRank, Is.Zero);
            ScoreStore.Shared.Save(new PlayResult { ChartKey = song.name, Difficulty = Difficulty.Oni,
                Score = 960000, IsClear = true, Good = 890, Ok = 15, Bad = 0 });
            yield return null;
            Assert.That(board.scoreRank.DisplayedRank, Is.EqualTo(6));
            Assert.That(board.scoreRank.image.sprite.name, Is.EqualTo("s84"));
            Assert.That(board.scoreRank.image.raycastTarget, Is.False);
            TestCapture.Capture("ScoreRankSongBoard.png");
            select.Right();
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(select.view.songBoards.Single(b => b.song == select.FocusedSong).scoreRank.DisplayedRank, Is.Zero);
            select.Left();
            yield return new WaitForSecondsRealtime(.6f);
            select.Confirm();
            yield return new WaitForSecondsRealtime(1.4f);
            var card = select.view.cards[(int)Difficulty.Oni].scoreRank;
            Assert.That(card.DisplayedRank, Is.EqualTo(6));
            Assert.That(card.image.sprite, Is.SameAs(board.scoreRank.image.sprite));
            Assert.That(((RectTransform)card.transform).anchorMin, Is.EqualTo(new Vector2(0, 1)));
            Assert.That(((RectTransform)card.transform).anchoredPosition, Is.EqualTo(new Vector2(80, -42)));
            TestCapture.Capture("ScoreRankCourse.png");
            var resultData = new PlayResult { Title = "SCORE RANK", ChartKey = song.name, Difficulty = Difficulty.Oni,
                Score = 960000, Good = 890, Ok = 15, Rolls = 12, GaugePoints = 9000, IsClear = true };
            var switcher = SceneSwitcher.Instance;
            switcher.ShowResult(resultData);
            for (int i = 0; i < 600 && SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene; i++) yield return null;
            var result = Object.FindFirstObjectByType<ResultScene>();
            Assert.That(result, Is.Not.Null);
            double deadline = Time.realtimeSinceStartupAsDouble + 25;
            while (!result.Sequence.RankAtMs.HasValue || result.Sequence.Now < result.Sequence.RankAtMs.Value + 400)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline));
                yield return null;
            }
            Assert.That(result.view.scoreRank.DisplayedRank, Is.EqualTo(6));
            Assert.That(result.view.scoreRank.animationView.layers.Any(i => i.enabled && i.sprite.name == "s84"), Is.True);
            Assert.That(result.view.crown.enabled, Is.False, "Rank appears before the crown.");
            TestCapture.Capture("ScoreRankResultAnimation.png");
            result.Don();
            yield return null; yield return null;
            Assert.That(result.Sequence.Skipped, Is.True);
            Assert.That(result.view.scoreRank.animationView.layers.Count(i => i.enabled), Is.EqualTo(1));
            Assert.That(result.view.scoreRank.DisplayedRank, Is.EqualTo(6));
            Assert.That(((RectTransform)result.view.scoreRank.transform).anchorMin, Is.EqualTo(new Vector2(0, 1)));
            yield return new WaitForSecondsRealtime(.8f);
            TestCapture.Capture("ScoreRankResult.png");
        }
    }
}
