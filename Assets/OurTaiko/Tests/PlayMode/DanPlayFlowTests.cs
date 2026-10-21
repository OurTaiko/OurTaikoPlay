using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class DanPlayFlowTests
    {
        static string Folder => Path.Combine(TestSongs.Root, "DanCharts");

        // At BPM 240 a measure is a second. A's branch is decided on its first measure (four 良
        // under auto: 達人); B's on no notes at all, so it must start from 普通 (accuracy 0) unless
        // A's counts leaked into it.
        const string ChartA = "TITLE:Dan Branch A\nBPM:240\nCOURSE:Oni\nLEVEL:8\n#START\n1111,\n#BRANCHSTART p,50,90\n#N\n1000,\n#E\n1100,\n#M\n1110,\n#BRANCHEND\n1111,\n#END\n";
        const string ChartB = "TITLE:Dan Branch B\nBPM:240\nCOURSE:Oni\nLEVEL:8\n#START\n#BRANCHSTART p,50,90\n#N\n1000,\n#E\n1100,\n#M\n1110,\n#BRANCHEND\n1111,\n#END\n";

        [SetUp]
        public void WriteCharts()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "a.tja"), ChartA);
            File.WriteAllText(Path.Combine(Folder, "b.tja"), ChartB);
            LocalSongLibrary.EnsureInstance().UseRoot(TestSongs.Root);
        }

        [TearDown]
        public void Cleanup()
        {
            PlayOptions.ForSeat(0).auto = false;
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            LocalSongLibrary.EnsureInstance().UseRoot(TestSongs.Root);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        static DanCourseEntry Course(string exams)
        {
            var library = LocalSongLibrary.EnsureInstance();
            var course = DanCourse.Parse("{\"id\":\"local:play-test\",\"title\":\"十段\",\"color\":3,\"songs\":["
                + "{\"chart\":{\"course\":\"Oni\"},\"title\":\"Dan Branch A\"},{\"chart\":{\"course\":\"Oni\"},\"title\":\"Dan Branch B\"}],"
                + "\"exams\":" + exams + "}");
            var entry = new DanCourseResolver(library.Songs.Concat(library.Folders.SelectMany(f => f.AllSongs())).ToList(), null).Resolve(course);
            Assert.That(entry.IsPlayable, Is.True, entry.Error);
            return entry;
        }

        [UnityTest]
        public IEnumerator ACoursePlaysItsSongsInTurnAndEachStartsOnTheNormalRoute()
        {
            PlayOptions.ForSeat(0).auto = true;
            var entry = Course("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[10]},{\"type\":\"bad\",\"range\":\"less\",\"value\":[5]},"
                + "{\"type\":\"good\",\"range\":\"more\",\"value\":[[3],[3]]}]");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            SceneSwitcher.EnsureInstance().PlayDan(entry);
            yield return WaitForScene(SceneSwitcher.DanPlayScene, 30);
            var scene = Object.FindFirstObjectByType<DanPlayScene>();
            Assert.That(scene.SongIndex, Is.Zero);
            Assert.That(scene.view.rows.Count(r => r.root.gameObject.activeSelf), Is.EqualTo(2), "The gauge exam has its plate, not a row.");
            Assert.That(scene.view.gaugePlate.gameObject.activeSelf, Is.True);
            Assert.That(scene.view.gaugeValue.text, Is.EqualTo("10 ％以上"));
            // The doors close before the first song too, with its title.
            yield return WaitUntil(() => scene.BetweenElapsedMs > 1500, 5);
            Assert.That(scene.view.between.gameObject.activeSelf, Is.True);
            Assert.That(scene.view.betweenTitle.text, Is.EqualTo("Dan Branch A"));

            // Song A: its first measure (auto, all 良) takes it to 達人.
            yield return WaitUntil(() => scene.ChartTime > 1.6, 15);
            Assert.That(scene.Session.CurrentBranch, Is.EqualTo(BranchRoute.Master));
            var first = scene.Session;
            TestCapture.Capture("DanPlay.png");

            // The doors close over the lane with song B's title.
            yield return WaitUntil(() => scene.SongIndex == 1, 15);
            // Nothing of song A stays on the lane; the course's gauge carries on.
            Assert.That(scene.noteLayer.Cast<Transform>().Count(t => t.gameObject.activeSelf), Is.Zero);
            Assert.That(scene.Gauge.Points, Is.GreaterThan(0));
            Assert.That(scene.soulGauge.FilledCells, Is.GreaterThan(0));
            Assert.That(scene.Lane.Carry.Good, Is.EqualTo(11), "Song A's 良 carry into the course's counts.");
            Assert.That(scene.Lane.DisplayCombo, Is.EqualTo(11), "The combo runs on into song B.");
            yield return new WaitForSecondsRealtime(3f);
            Assert.That(scene.view.between.gameObject.activeSelf, Is.True);
            Assert.That(scene.view.betweenTitle.text, Is.EqualTo("Dan Branch B"));
            TestCapture.Capture("DanPlayBetween.png");

            // Song B: a new session, decided on its own (no notes yet: 普通).
            yield return WaitUntil(() => scene.ChartTime > 0.3, 15);
            Assert.That(scene.Session, Is.Not.SameAs(first));
            Assert.That(scene.Session.CurrentBranch, Is.EqualTo(BranchRoute.Normal), "The branch counters start over with each song.");
            yield return WaitUntil(() => scene.view.between.gameObject.activeSelf == false, 5);

            yield return WaitForScene(SceneSwitcher.DanResultScene, 40);
            var result = SceneSwitcher.Instance.LastDan;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Songs.Length, Is.EqualTo(2));
            Assert.That(result.Songs.Select(s => s.Good), Is.EqualTo(new[] { 11, 5 }));
            Assert.That(result.Progress.Total.Good, Is.EqualTo(16));
            Assert.That(result.MaxCombo, Is.EqualTo(16));
            Assert.That(result.Verdict, Is.EqualTo(6), "Gold, no 可, no 不可.");

            // The result: under オート nothing is recorded, and the plate says so.
            var screen = Object.FindFirstObjectByType<DanResultScene>();
            Assert.That(screen.Recorded, Is.False);
            Assert.That(screen.view.noSave.gameObject.activeSelf, Is.True);
            Assert.That(DanRecordStore.Shared.Get(DanRecordStore.Key(entry)), Is.Null);
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.That(screen.view.plates[0].title.text, Is.EqualTo("Dan Branch A"));
            Assert.That(screen.view.plates[1].unreached.enabled, Is.False);
            TestCapture.Capture("DanResultSongs.png");
            screen.Don();
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(screen.Flow.State, Is.EqualTo(DanResultFlow.Phase.Totals));
            Assert.That(screen.view.exams[0].gauge.gameObject.activeSelf, Is.True, "The gauge exam is the soul gauge row.");
            Assert.That(screen.view.exams[2].songs[1].root.gameObject.activeSelf, Is.True, "A per-song exam shows both songs.");
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(screen.view.stamp.enabled, Is.True);
            Assert.That(screen.view.stamp.sprite.name, Is.EqualTo("gold_clear_03"));
            TestCapture.Capture("DanResultTotals.png");
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            yield return WaitForScene(SceneSwitcher.DanSelectScene, 10);
        }

        [UnityTest]
        public IEnumerator AFailedExamEndsTheCourseAfterItsSong()
        {
            var entry = Course("[{\"type\":\"bad\",\"range\":\"less\",\"value\":[1]}]");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            SceneSwitcher.EnsureInstance().PlayDan(entry);
            yield return WaitForScene(SceneSwitcher.DanPlayScene, 30);
            var scene = Object.FindFirstObjectByType<DanPlayScene>();
            // Nobody plays: the first note is a 不可 and the exam fails at once, but the song plays on.
            yield return WaitUntil(() => scene.Progress.AnyFailed, 15);
            Assert.That(scene.SongEnded, Is.False);
            Assert.That(scene.view.rows[0].failed.enabled, Is.True);
            TestCapture.Capture("DanPlayFailed.png");
            yield return WaitForScene(SceneSwitcher.DanResultScene, 30);
            var result = SceneSwitcher.Instance.LastDan;
            Assert.That(result.Songs.Length, Is.EqualTo(1), "The course stopped after its first song.");
            Assert.That(result.Verdict, Is.Zero);

            // Played for real: the attempt is recorded, and song A's score with it.
            var screen = Object.FindFirstObjectByType<DanResultScene>();
            Assert.That(screen.Recorded, Is.True);
            var best = DanRecordStore.Shared.Get(DanRecordStore.Key(entry));
            Assert.That(best.HasValue, Is.True);
            Assert.That((best.Value.Verdict, best.Value.Arrival), Is.EqualTo((0, 1)));
            var songA = entry.Songs[0].Song;
            Assert.That(ScoreStore.Shared.Get(songA.ScoreKey, Difficulty.Oni)?.bad, Is.EqualTo(9), "Unplayed on the normal route: 4 + 1 + 4 不可.");
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.That(screen.view.plates[1].unreached.enabled, Is.True, "Song B was never reached.");
            screen.Don();
            yield return new WaitForSecondsRealtime(0.6f);
            screen.Don();
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(screen.view.stamp.enabled, Is.True);
            Assert.That(screen.view.stamp.sprite.name, Is.EqualTo("fail"));
            TestCapture.Capture("DanResultFailed.png");
            screen.Don();
            yield return WaitForScene(SceneSwitcher.DanSelectScene, 10);
        }

        [UnityTest]
        public IEnumerator ThePauseHoldsTheDoors()
        {
            PlayOptions.ForSeat(0).auto = true;
            var entry = Course("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[10]}]");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.DanSelectScene);
            yield return null;
            SceneSwitcher.EnsureInstance().PlayDan(entry);
            yield return WaitForScene(SceneSwitcher.DanPlayScene, 30);
            var scene = Object.FindFirstObjectByType<DanPlayScene>();
            yield return WaitUntil(() => scene.BetweenElapsedMs > 1000, 5);
            double clock = scene.SongTime;
            scene.TogglePause();
            Assert.That(scene.IsPaused, Is.True);
            double held = scene.BetweenElapsedMs;
            yield return new WaitForSecondsRealtime(3f);
            Assert.That(scene.BetweenElapsedMs, Is.EqualTo(held).Within(1), "The doors wait with the song clock.");
            Assert.That(scene.view.between.gameObject.activeSelf, Is.True);
            scene.Resume();
            yield return WaitUntil(() => !scene.IsPaused, 3);
            Assert.That(scene.BetweenElapsedMs, Is.LessThan(held + 1000), "Only the menu's fade passed.");
            Assert.That(scene.SongTime - clock, Is.LessThan(1.2));
            Assert.That(scene.view.between.gameObject.activeSelf, Is.True);
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

        static IEnumerator WaitForScene(string scene, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Waiting for " + scene); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
