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
        public IEnumerator SongBoardAndDifficultyCardUseTheSameDynamicLineAsResults()
        {
            var store = ScoreStore.Shared;
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "rank-select-" + System.Guid.NewGuid() + ".sqlite3");
            SongDefinition song = null;
            TextAsset original = null;
            var chart = new TextAsset("TITLE:Dynamic Rank\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1110,\n#END");
            try
            {
                ScoreStore.Shared = new ScoreStore(path);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
                yield return new WaitForSecondsRealtime(.6f);
                var select = Object.FindFirstObjectByType<SongSelectScene>();
                song = select.Manager.FocusedSong; original = song.chart; song.chart = chart;
                var board = select.view.songBoards[System.Array.IndexOf(select.Manager.Songs, song)].scoreRank;
                var session = new PlaySession(song.Parse("Oni"));
                session.Advance(3, true);
                Assert.That(session.Score, Is.EqualTo(1000020));
                var resultData = PlayResult.From(session, song.name, false);
                Assert.That(resultData.KiwamiThreshold, Is.EqualTo(1000020));
                resultData.Score = 1000000;
                ScoreStore.Shared.Save(resultData);
                yield return null;
                Assert.That(board.DisplayedRank, Is.EqualTo(resultData.Rank));
                Assert.That(board.DisplayedRank, Is.EqualTo(ScoreRank.PurpleMiyabi));
                select.Manager.Confirm();
                yield return new WaitForSecondsRealtime(1.4f);
                var card = select.view.cards[(int)Difficulty.Oni].scoreRank;
                Assert.That(card.DisplayedRank, Is.EqualTo(resultData.Rank));
                Assert.That(card.image.sprite.name, Is.EqualTo("s84"));

                resultData.Score = session.Score;
                ScoreStore.Shared.Save(resultData);
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(card.DisplayedRank, Is.EqualTo(resultData.Rank));
                Assert.That(card.image.sprite.name, Is.EqualTo("s86"));
                // The focused board is released while the course panel is open.
                for (int i = 0; i < 6; i++) select.Manager.Left();
                Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Back));
                select.Manager.Confirm();
                yield return new WaitForSecondsRealtime(.6f);
                Assert.That(board.DisplayedRank, Is.EqualTo(ScoreRank.Kiwami));
                SceneSwitcher.Instance.ShowResult(resultData);
                double deadline = Time.realtimeSinceStartupAsDouble + 20;
                while (SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene || SceneSwitcher.Instance.IsInputBlocked)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                var result = Object.FindFirstObjectByType<ResultScene>();
                while (!result.Sequence.CanSkip)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                result.Don(); yield return null; yield return null;
                Assert.That(result.view.scoreRank.DisplayedRank, Is.EqualTo(ScoreRank.Kiwami));
            }
            finally
            {
                if (song != null) song.chart = original;
                Object.Destroy(chart);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                ScoreStore.Shared = store;
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [UnityTest]
        public IEnumerator RankAppearsInAllThreePlacesAndClearsOnUnplayedSongs()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(.6f);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            var song = select.Manager.FocusedSong;
            // Saved board i shows local song i.
            var board = select.view.songBoards[System.Array.IndexOf(select.Manager.Songs, song)];
            Assert.That((int)board.scoreRank.DisplayedRank, Is.Zero);
            ScoreStore.Shared.Save(new PlayResult { ChartKey = song.name, Difficulty = Difficulty.Oni,
                Score = 960000, IsClear = true, Good = 890, Ok = 15, Bad = 0 });
            yield return null;
            Assert.That((int)board.scoreRank.DisplayedRank, Is.EqualTo(6));
            Assert.That(board.scoreRank.image.sprite.name, Is.EqualTo("s84"));
            Assert.That(board.scoreRank.image.raycastTarget, Is.False);
            TestCapture.Capture("ScoreRankSongBoard.png");
            select.Manager.Right();
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That((int)select.view.songBoards[System.Array.IndexOf(select.Manager.Songs, select.Manager.FocusedSong)].scoreRank.DisplayedRank, Is.Zero);
            select.Manager.Left();
            yield return new WaitForSecondsRealtime(.6f);
            select.Manager.Confirm();
            yield return new WaitForSecondsRealtime(1.4f);
            var card = select.view.cards[(int)Difficulty.Oni].scoreRank;
            Assert.That((int)card.DisplayedRank, Is.EqualTo(6));
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
            Assert.That((int)result.view.scoreRank.DisplayedRank, Is.EqualTo(6));
            Assert.That(result.view.scoreRank.animationView.layers.Any(i => i.enabled && i.sprite.name == "s84"), Is.True);
            Assert.That(result.view.crown.enabled, Is.False, "Rank appears before the crown.");
            TestCapture.Capture("ScoreRankResultAnimation.png");
            result.Don();
            yield return null; yield return null;
            Assert.That(result.Sequence.Skipped, Is.True);
            Assert.That(result.view.scoreRank.animationView.layers.Count(i => i.enabled), Is.EqualTo(1));
            Assert.That((int)result.view.scoreRank.DisplayedRank, Is.EqualTo(6));
            Assert.That(((RectTransform)result.view.scoreRank.transform).anchorMin, Is.EqualTo(new Vector2(0, 1)));
            yield return new WaitForSecondsRealtime(.8f);
            TestCapture.Capture("ScoreRankResult.png");
        }
    }
}
