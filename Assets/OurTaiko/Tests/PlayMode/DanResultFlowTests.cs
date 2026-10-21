using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class DanResultFlowTests
    {
        static string Folder => Path.Combine(TestSongs.Root, "DanResultCharts");

        [SetUp]
        public void WriteCharts()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "a.tja"), "TITLE:Result A\nBPM:240\nCOURSE:Oni\nLEVEL:8\n#START\n1111,1111,\n#END\n");
            File.WriteAllText(Path.Combine(Folder, "b.tja"), "TITLE:Result B\nBPM:240\nCOURSE:Oni\nLEVEL:9\n#START\n1111,\n#END\n");
            LocalSongLibrary.EnsureInstance().UseRoot(TestSongs.Root);
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            LocalSongLibrary.EnsureInstance().UseRoot(TestSongs.Root);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        static DanCourseEntry Course(string exams, string server = null, string rewards = "")
        {
            var library = LocalSongLibrary.EnsureInstance();
            var songs = library.Songs.Concat(library.Folders.SelectMany(f => f.AllSongs())).ToList();
            string Chart(string title) => server == null ? "{\"course\":\"Oni\"}" : "{\"id\":\"" + title + "\",\"course\":\"Oni\"}";
            var course = DanCourse.Parse("{\"id\":\"local:result-test\",\"title\":\"名人\",\"color\":5,\"songs\":["
                + "{\"chart\":" + Chart("Result A") + ",\"title\":\"Result A\"},{\"chart\":" + Chart("Result B") + ",\"title\":\"Result B\",\"hidden\":true}],"
                + "\"exams\":" + exams + rewards + "}", server);
            // A server's songs by id: the test names them by their titles.
            var entry = new DanCourseResolver(songs, (_, id) => songs.FirstOrDefault(s => s.ReadDisplayInfo().Title == id)).Resolve(course);
            Assert.That(entry.IsPlayable, Is.True, entry.Error);
            return entry;
        }

        // A course played to its end (both songs) or failing out on the first.
        static DanPlayResult Played(DanCourseEntry entry, bool failOut)
        {
            var progress = new DanProgress(entry.Course, new[] { 8, 4 });
            var a = new DanSongStats { Good = failOut ? 0 : 8, Bad = failOut ? 8 : 0, Score = failOut ? 0 : 900000, MaxCombo = failOut ? 0 : 8 };
            progress.Update(a, failOut ? 0 : 100);
            progress.EndSong();
            var songs = new System.Collections.Generic.List<PlayResult> { new PlayResult { ChartKey = entry.Songs[0].Song.ScoreKey, Good = a.Good, Bad = a.Bad, Score = a.Score, MaxCombo = a.MaxCombo } };
            if (!failOut)
            {
                progress.Next();
                var b = new DanSongStats { Good = 4, Score = 800000, MaxCombo = 12 };
                progress.Update(b, 100);
                progress.EndSong();
                songs.Add(new PlayResult { ChartKey = entry.Songs[1].Song.ScoreKey, Good = 4, Score = 800000, MaxCombo = 12 });
            }
            var total = progress.Total;
            return new DanPlayResult
            {
                Run = new DanRun(entry), Progress = progress, Verdict = progress.Verdict(), Songs = songs.ToArray(),
                Score = total.Score, MaxCombo = total.MaxCombo, GaugePoints = failOut ? 0 : SoulGauge.MaximumPoints,
            };
        }

        [UnityTest]
        public IEnumerator ABetterPassShowsTheBestScorePillAndLongValuesKeepEveryDigit()
        {
            var entry = Course("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[10]},{\"type\":\"score\",\"range\":\"more\",\"value\":[1500000]}]");
            DanRecordStore.Shared.Save(DanRecordStore.Key(entry), "名人", 4, 1000000, 10, 10000, 2);
            var result = Played(entry, failOut: false);
            Assert.That(result.Verdict, Is.EqualTo(6));
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            SceneSwitcher.EnsureInstance().ShowDanResult(result);
            yield return WaitForScene(SceneSwitcher.DanResultScene);
            var screen = Object.FindFirstObjectByType<DanResultScene>();
            Assert.That(screen.Recorded, Is.True);
            Assert.That(screen.PreviousBest?.Score, Is.EqualTo(1000000));
            Assert.That(screen.voice.clip, Is.SameAs(screen.announce), "The announce, once, as the result opens.");

            // The drum waits 500 ms after each change.
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Songs), "The first hit lands the plates.");
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Songs), "Locked right after.");
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Totals));
            // The best score's pill drops in as the totals start to count.
            var view = screen.view;
            Assert.That(view.bestScore.gameObject.activeSelf, Is.True);
            // The glow swells off it once.
            yield return WaitUntil(() => view.bestScoreGlow.enabled, 4);
            TestCapture.Capture("DanResultBestScoreGlow.png");
            // A hit before the stamp lands everything.
            screen.Don();
            Assert.That(screen.Flow.Skipped, Is.True);
            yield return new WaitForSecondsRealtime(0.1f);
            var digits = view.bestScoreDigits.Where(d => d.enabled).ToArray();
            Assert.That(digits.Length, Is.EqualTo(6), "1,700,000 - 1,000,000 = 700000.");
            Assert.That(digits.Last().rectTransform.anchoredPosition.x, Is.EqualTo(844 - 459).Within(0.5f), "The last digit sits on the pill's own x.");
            var scoreRow = view.exams[1];
            Assert.That(scoreRow.all.gameObject.activeSelf, Is.True);
            Assert.That(string.Concat(scoreRow.allDigits.Where(d => d.enabled).Select(d => d.sprite.name)), Is.EqualTo("1700000"));
            Assert.That(view.plates[1].title.text, Is.EqualTo("Result B"), "Reached: its title shows.");
            TestCapture.Capture("DanResultBestScore.png");

            // Better than the 金合格 before: 昇段, with 合格 alone (a local course names no rank).
            Assert.That(screen.Celebrates, Is.True);
            Assert.That(screen.Congratulates, Is.False);
            yield return new WaitForSecondsRealtime(1.0f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Celebrating));
            yield return new WaitForSecondsRealtime(1.6f);
            Assert.That(view.celebration.gameObject.activeSelf, Is.True);
            Assert.That(view.totalsPage.gameObject.activeSelf, Is.False);
            Assert.That(view.glyphs.Select(g => g.gameObject.activeSelf), Is.EqualTo(new[] { false, false, true, true }));
            Assert.That(view.glyphs[2].sprite.name, Is.EqualTo("18"));
            Assert.That(view.shoudanBackground.sprite.name, Is.EqualTo("bg_r"), "金合格・全良.");
            Assert.That(view.nameplate.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
            TestCapture.Capture("DanResultCelebration.png");
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Celebrating), "3.5 s before a hit ends it.");
            yield return new WaitForSecondsRealtime(2.0f);
            screen.Don();
            yield return WaitForScene(SceneSwitcher.DanSelectScene);
        }

        [UnityTest]
        public IEnumerator TheHighestRanksFirstPassShowsThePopup()
        {
            var entry = Course("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[10]}]", "https://dan.test", ",\"rewards\":{\"dan\":24}");
            Assert.That(entry.Course.DanIndex, Is.EqualTo(24));
            var result = Played(entry, failOut: false);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            var switcher = SceneSwitcher.EnsureInstance();
            switcher.DanIndexMax = 24;
            switcher.ShowDanResult(result);
            yield return WaitForScene(SceneSwitcher.DanResultScene);
            var screen = Object.FindFirstObjectByType<DanResultScene>();
            Assert.That(screen.Celebrates && screen.Congratulates, Is.True);
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            yield return new WaitForSecondsRealtime(1.1f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Celebrating));
            yield return new WaitForSecondsRealtime(1.6f);
            var view = screen.view;
            Assert.That(view.glyphs.Select(g => g.sprite.name), Is.EqualTo(new[] { "16", "17", "18", "19" }), "達人 合格.");
            TestCapture.Capture("DanResultCelebrationRank.png");
            yield return new WaitForSecondsRealtime(2.0f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Congrats));
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.That(view.congratsText.sprite.name, Is.EqualTo("0"), "Japanese, as the rest of the screens.");
            Assert.That(view.congratsText.color.a, Is.EqualTo(1));
            TestCapture.Capture("DanResultCongrats.png");
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Congrats), "The popup takes no hit.");
            screen.Leave();
            yield return WaitForScene(SceneSwitcher.DanSelectScene);
        }

        [UnityTest]
        public IEnumerator AnUnreachedHiddenSongKeepsItsTitleBack()
        {
            var entry = Course("[{\"type\":\"bad\",\"range\":\"less\",\"value\":[1]}]");
            var result = Played(entry, failOut: true);
            Assert.That(result.Verdict, Is.Zero);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            SceneSwitcher.EnsureInstance().ShowDanResult(result);
            yield return WaitForScene(SceneSwitcher.DanResultScene);
            var screen = Object.FindFirstObjectByType<DanResultScene>();
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.That(screen.view.plates[1].unreached.enabled, Is.True);
            Assert.That(screen.view.plates[1].title.text, Is.EqualTo("？？？？？？"));
            Assert.That(screen.view.plates[1].banner.color.a, Is.EqualTo(0.45f).Within(1e-3));
            Assert.That(screen.view.plates[1].unreached.color.a, Is.EqualTo(1), "The 未到達 mark itself is not dimmed.");
            Assert.That(screen.view.bestScore.gameObject.activeSelf, Is.False);
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Waiting for " + scene); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
