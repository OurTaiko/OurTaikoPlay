using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class DanResultLogicTests
    {
        [Test]
        public void PlatesSlideInWithTheirOvershoot()
        {
            Assert.That(DanResultFlow.LandMs(0), Is.EqualTo(40 * DanResultFlow.FrameMs).Within(1e-9));
            Assert.That(DanResultFlow.LandMs(2), Is.EqualTo(90 * DanResultFlow.FrameMs).Within(1e-9));
            Assert.That(DanResultFlow.SlideOffset(-1), Is.EqualTo(1600));
            Assert.That(DanResultFlow.SlideOffset(DanResultFlow.SlideMs * 5 / 22), Is.EqualTo(35).Within(1e-6), "The break: 35 px short.");
            Assert.That(DanResultFlow.SlideOffset(DanResultFlow.SlideMs), Is.Zero);
        }

        [Test]
        public void HitsShowThePlatesTurnThePageStampAndLeave()
        {
            var flow = new DanResultFlow(3, 0);
            Assert.That(flow.Hit(100), Is.False, "Input lock.");
            Assert.That(flow.Hit(600), Is.True);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Songs), "The first hit lands every plate.");
            Assert.That(flow.SongsTime(600), Is.GreaterThanOrEqualTo(DanResultFlow.LandMs(2) + DanResultFlow.SlideMs));
            Assert.That(flow.Hit(700), Is.False, "Each change locks the drum for 500 ms.");
            flow.Hit(1100);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Totals));
            Assert.That(flow.StampShown(1200), Is.False);
            Assert.That(flow.Update(1100 + DanResultFlow.StampDelayMs), Is.True, "The stamp lands on its own.");
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Stamped));
            Assert.That(flow.Hit(1100 + DanResultFlow.StampDelayMs + 100), Is.False, "The stamp stays up 500 ms.");
            flow.Hit(3000);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Left));
            var quick = new DanResultFlow(1, 0);
            quick.Hit(5000); quick.Hit(5600);
            Assert.That(quick.StampShown(5600), Is.True, "A hit before the stamp brings it at once.");
            Assert.That(DanResultFlow.VerdictName(0), Is.EqualTo("不合格"));
            Assert.That(DanResultFlow.VerdictName(6), Is.EqualTo("金合格・全良"));
        }

        [Test]
        public void RecordsKeepEveryAttemptAndAChangedCourseIsAnother()
        {
            string path = Path.Combine(Path.GetTempPath(), "dan-records-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            var chart = new TextAsset("TITLE:x\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1,\n#END");
            try
            {
                song.name = "x"; song.localChartSha256 = "abc"; song.chart = chart;
                DanCourseEntry Entry(string exams) => new DanCourseResolver(new[] { song }, null).Resolve(DanCourse.Parse(
                    "{\"id\":\"local:r\",\"title\":\"x\",\"songs\":[{\"chart\":{\"sha256\":\"abc\"}}],\"exams\":" + exams + "}"));
                Assert.That(Entry("[]").IsPlayable, Is.True);
                var store = new DanRecordStore(path);
                string key = DanRecordStore.Key(Entry("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]}]"));
                Assert.That(store.Get(key), Is.Null);
                Assert.That(store.Save(key, "x", 1, 900000, 300, 10000, 3), Is.Null, "No best before the first attempt.");
                var before = store.Save(key, "x", 0, 950000, 200, 5000, 1);
                Assert.That(before?.Verdict, Is.EqualTo(1));
                var best = new DanRecordStore(path).Get(key).Value;
                Assert.That((best.Verdict, best.Score, best.Arrival, best.Attempts), Is.EqualTo((1, 950000, 3, 2)));
                Assert.That(DanRecordStore.Key(Entry("[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[90]}]")), Is.Not.EqualTo(key));
                song.localChartSha256 = "def";
                // The same song file edited: the course now finds it by its title (x) under the new hash.
                Assert.That(DanRecordStore.Key(new DanCourseResolver(new[] { song }, null).Resolve(DanCourse.Parse(
                    "{\"id\":\"local:r\",\"title\":\"x\",\"songs\":[{\"chart\":{\"sha256\":\"abc\"},\"title\":\"x\"}],\"exams\":[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]}]}"))),
                    Is.Not.EqualTo(key), "Another chart is another course.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(song);
                UnityEngine.Object.DestroyImmediate(chart);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
