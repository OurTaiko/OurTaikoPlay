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

        static DanCourseEntry Course(string exams)
        {
            var library = LocalSongLibrary.EnsureInstance();
            var course = DanCourse.Parse("{\"id\":\"local:result-test\",\"title\":\"名人\",\"color\":5,\"songs\":["
                + "{\"chart\":{\"course\":\"Oni\"},\"title\":\"Result A\"},{\"chart\":{\"course\":\"Oni\"},\"title\":\"Result B\",\"hidden\":true}],"
                + "\"exams\":" + exams + "}");
            var entry = new DanCourseResolver(library.Songs.Concat(library.Folders.SelectMany(f => f.AllSongs())).ToList(), null).Resolve(course);
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

            // The drum waits 500 ms after each change.
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Songs), "The first hit lands the plates.");
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Songs), "Locked right after.");
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Totals));
            yield return new WaitForSecondsRealtime(1.5f);

            var view = screen.view;
            Assert.That(view.bestScore.gameObject.activeSelf, Is.True);
            var digits = view.bestScoreDigits.Where(d => d.enabled).ToArray();
            Assert.That(digits.Length, Is.EqualTo(6), "1,700,000 - 1,000,000 = 700000.");
            Assert.That(digits.Last().rectTransform.anchoredPosition.x, Is.EqualTo(844 - 459).Within(0.5f), "The last digit sits on the pill's own x.");
            var scoreRow = view.exams[1];
            Assert.That(scoreRow.all.gameObject.activeSelf, Is.True);
            Assert.That(string.Concat(scoreRow.allDigits.Where(d => d.enabled).Select(d => d.sprite.name)), Is.EqualTo("1700000"));
            Assert.That(view.plates[1].title.text, Is.EqualTo("Result B"), "Reached: its title shows.");
            TestCapture.Capture("DanResultBestScore.png");
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

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Waiting for " + scene); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
