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
        public void HitsShowThePlatesTurnThePageLandItAndLeave()
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
            double stamp = 1100 + flow.StampAt;
            Assert.That(flow.StampShown(stamp), Is.True, "The stamp lands on its own.");
            Assert.That(flow.Hit(stamp + 100), Is.False, "The stamp stays up 500 ms.");
            flow.Hit(stamp + 600);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Left));

            var quick = new DanResultFlow(1, 0);
            quick.Hit(5000); quick.Hit(5600);
            Assert.That(quick.Skipped, Is.True, "A hit before the stamp lands everything.");
            Assert.That(quick.StampShown(5600 + DanResultFlow.WaitMs), Is.True, "The stamp follows 500 ms on.");
            Assert.That(quick.Hit(5600 + DanResultFlow.WaitMs + 100), Is.False);
            Assert.That(DanResultFlow.VerdictName(0), Is.EqualTo("不合格"));
            Assert.That(DanResultFlow.VerdictName(6), Is.EqualTo("金合格・全良"));
        }

        [Test]
        public void PageTwoPlaysOutItsTimeline()
        {
            // build_page2_timeline: a 7-digit score, a full gauge and two bars (one travels all the way).
            var page = new DanResultFlow.Page { Digits = 7, GaugeRow = 0, GaugeValue = 100, Travel = new[] { 0, 1.0, 0.25 } };
            var flow = new DanResultFlow(3, 0, page);
            double start = 94 * DanResultFlow.FrameMs;
            Assert.That(flow.TotalsStart, Is.EqualTo(start).Within(1e-6));
            Assert.That(flow.TotalsEnd, Is.EqualTo(start + 3500).Within(1e-6), "7 places at 500 ms beat the gauge's 2.5 s + 667 ms.");
            Assert.That(flow.Rows[0].Number, Is.EqualTo(start + 2500 + DanResultFlow.GaugeNumberMs - 500).Within(1e-6));
            double t = flow.TotalsEnd + 200;
            Assert.That(flow.Rows[1].Land, Is.EqualTo(t).Within(1e-6));
            Assert.That(flow.Rows[1].FillMs, Is.EqualTo(100 * DanResultFlow.RowUnitMs).Within(1e-6));
            Assert.That(flow.Rows[2].Land, Is.EqualTo(flow.Rows[1].Number + DanResultFlow.RowWaitMs).Within(1e-6));
            Assert.That(flow.Rows[2].FillMs, Is.EqualTo(25 * DanResultFlow.RowUnitMs).Within(1e-6));
            Assert.That(flow.StampAt, Is.EqualTo(flow.Rows[2].Number + DanResultFlow.RowWaitMs + 500).Within(1e-6));
            Assert.That(flow.VoiceAt - flow.StampAt, Is.EqualTo(89 * DanResultFlow.FrameMs).Within(1e-6));

            flow.Hit(600); flow.Hit(1100);
            Assert.That(flow.GaugeAt(start + 1000, out bool landed), Is.EqualTo(40));
            Assert.That(landed, Is.False);
            Assert.That(flow.GaugeAt(start + 2500, out landed), Is.EqualTo(100));
            Assert.That(landed, Is.True);
            Assert.That(flow.FillShare(1, flow.Rows[1].Fill + flow.Rows[1].FillMs / 2), Is.EqualTo(0.5).Within(1e-6));
            // The ones roll first; the tens show after 500 ms.
            Assert.That(DanResultFlow.RollDigit(7, 1, 100), Is.EqualTo(-1));
            Assert.That(DanResultFlow.RollDigit(7, 0, 900), Is.EqualTo(7));
            Assert.That(DanResultFlow.RollDigit(7, 1, 600), Is.InRange(0, 9));
            Assert.That(DanResultFlow.RollDigit(7, 1, -1), Is.EqualTo(7));

            flow.Hit(1100 + start + 100);
            Assert.That(flow.Skipped, Is.True);
            Assert.That(flow.FillShare(2, 1100 + start + 100), Is.EqualTo(1));
            Assert.That(flow.RollTime(1100 + start + 100), Is.EqualTo(-1));
        }

        [Test]
        public void BarsDrainALessExamAndGrowAMoreOne()
        {
            var less = new DanExam { Type = DanExamType.Bad, Range = DanExamRange.Less };
            var more = new DanExam { Type = DanExamType.Good, Range = DanExamRange.More };
            Assert.That(DanResultFlow.Travel(less, 0.8), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(DanResultFlow.Travel(more, 0.8), Is.EqualTo(0.8).Within(1e-9));
            Assert.That(DanResultFlow.Travel(more, 0, new[] { 0.3, 0.9 }), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(DanResultFlow.FillProgress(less, 0.8, 0), Is.EqualTo(1), "A less bar starts full.");
            Assert.That(DanResultFlow.FillProgress(more, 0.8, 0.5), Is.EqualTo(0.4).Within(1e-9));
            Assert.That(DanResultFlow.FillState(0), Is.EqualTo("empty"));
            Assert.That(DanResultFlow.FillState(0.3), Is.EqualTo("up_50"));
            Assert.That(DanResultFlow.FillState(0.7), Is.EqualTo("up_80"));
            Assert.That(DanResultFlow.FillState(1), Is.EqualTo("up_100"));
        }

        [Test]
        public void ACelebrationEndsAfterThreeAndAHalfSecondsAndThePopupLeavesByItself()
        {
            var flow = new DanResultFlow(1, 0, null, celebrate: true, congratulate: true);
            flow.Hit(600); flow.Hit(1100);
            double stamp = 1100 + flow.StampAt;
            flow.Hit(stamp + 600);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Celebrating));
            Assert.That(flow.Hit(stamp + 600 + 3000), Is.False, "3.5 s first.");
            flow.Hit(stamp + 600 + 3500);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Congrats));
            Assert.That(flow.Hit(stamp + 600 + 6000), Is.False, "The popup takes no hit.");
            Assert.That(flow.Update(stamp + 600 + 3500 + DanResultFlow.CongratsMs), Is.True);
            Assert.That(flow.State, Is.EqualTo(DanResultFlow.Phase.Left));
            Assert.That(new DanResultFlow(1, 0, null, celebrate: false, congratulate: true).Congratulates, Is.False, "No popup without the celebration.");
        }

        [Test]
        public void TheRankTextNamesTheCourse()
        {
            Assert.That(DanResultFlow.RankGlyphs(0), Is.EqualTo((10, 11)), "初級");
            Assert.That(DanResultFlow.RankGlyphs(1), Is.EqualTo((9, 11)), "十級");
            Assert.That(DanResultFlow.RankGlyphs(11), Is.EqualTo((10, 12)), "初段");
            Assert.That(DanResultFlow.RankGlyphs(20), Is.EqualTo((9, 12)), "十段");
            Assert.That(DanResultFlow.RankGlyphs(24), Is.EqualTo((16, 17)), "達人");
            Assert.That(DanResultFlow.RankGlyphs(-1), Is.Null, "No rank: 合格 alone.");
            Assert.That(DanCourse.Parse("{\"id\":\"x\",\"title\":\"x\",\"songs\":[{\"chart\":{\"id\":\"1\"}}],\"rewards\":{\"dan\":20}}", "https://s").DanIndex, Is.EqualTo(20));
            Assert.That(DanCourse.Parse("{\"id\":\"x\",\"title\":\"x\",\"songs\":[{\"chart\":{\"sha256\":\"a\"}}],\"rewards\":{\"dan\":20}}").DanIndex, Is.EqualTo(-1), "Local courses name no rank.");
            var (y, alpha) = DanResultFlow.BestScorePill(5);
            Assert.That((y, alpha), Is.EqualTo((24.0, 0.0)));
            Assert.That(DanResultFlow.BestScorePill(52), Is.EqualTo((0.0, 1.0)));
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
