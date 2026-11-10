using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // A play's record (PlayRecord, version 2) holds what is needed to see the play again: the options
    // the chart was prepared with and every judged press. ReplayFeed judges it again.
    public sealed class ReplayTests
    {
        const string Tja = "TITLE:Replay\nBPM:150\nCOURSE:Oni\nLEVEL:8\nBALLOON:4,6\n#START\n"
            + "1212121211221122,\n3003400450000008,\n#SECTION\n#BRANCHSTART p,60,85\n#N\n1111,\n1010,\n#E\n12121212,\n7008,\n#M\n1212121212121212,\n1122,\n#BRANCHEND\n"
            + "#SECTION\n#BRANCHSTART p,60,85\n#N\n1000,\n#E\n1200,\n#M\n1221,\n#BRANCHEND\n1121211270080000,\n1000,\n#END";

        static TaikoChart Chart() => TjaParser.Parse(Tja);

        // A play as PlayerLane makes it: a frame advances the session, then judges the presses it took,
        // each recorded at the frame's time.
        static (PlaySession, PlayRecord) Play(TaikoChart chart, int seed, double frame, double accuracy)
        {
            var random = new Random(seed);
            var presses = new List<(double Time, bool Ka)>();
            foreach (var note in chart.Notes)
            {
                if (note.IsLong) { for (double t = note.Time; t < note.EndTime; t += 0.07) presses.Add((t, false)); continue; }
                if (random.NextDouble() < 0.1) continue; // left alone
                presses.Add((note.Time + (random.NextDouble() - 0.5) * accuracy, random.NextDouble() < 0.9 ? note.IsKa : !note.IsKa));
            }
            presses.Sort((a, b) => a.Time.CompareTo(b.Time));
            var session = new PlaySession(chart);
            var record = new PlayRecord();
            record.SetOptions(new PlayOptions(), chart);
            int next = 0;
            for (double time = -1; time < chart.Notes.Max(n => n.EndTime) + 2; time += frame)
            {
                session.Advance(time, false);
                while (next < presses.Count && presses[next].Time <= time)
                {
                    record.Inputs.Add((time * 1000, PlayRecord.TypeOf(presses[next].Ka, next % 2 == 0)));
                    session.Hit(presses[next++].Ka, time);
                }
            }
            return (session, record);
        }

        static string State(PlaySession s) => $"{s.Score} {s.Good}/{s.Ok}/{s.Bad} rolls {s.Rolls} combo {s.Combo}/{s.MaxCombo} gauge {s.GaugePoints} "
            + string.Join(",", s.BranchHistory) + " " + string.Join("", s.Resolved.Select(r => r ? '1' : '0'));

        [TestCase(1, 0.03)] [TestCase(2, 0.12)] [TestCase(3, 0.2)] [TestCase(4, 0.0)]
        public void AReplayEndsAsThePlayDidAtAnyFrameRate(int seed, double accuracy)
        {
            var (played, record) = Play(Chart(), seed, 1 / 60.0, accuracy);
            Assert.That(played.Good + played.Ok, Is.GreaterThan(10));
            record = PlayRecord.FromJson(JToken.Parse(record.ToJson().ToString()));
            foreach (double frame in new[] { 1 / 144.0, 1 / 24.0, 0.31 })
            {
                var feed = new ReplayFeed(record);
                var session = new PlaySession(Chart());
                for (double time = -1; time < 60; time += frame)
                {
                    while (feed.TryTake(time, out double at, out bool ka, out _)) session.Hit(ka, at);
                    session.Advance(time, false);
                }
                Assert.That(feed.Remaining, Is.Zero);
                Assert.That(State(session), Is.EqualTo(State(played)), "frame " + frame);
            }
        }

        [Test] public void AReplayOpenedPartWayHasThePlaysStateThereAndGoesOnToTheSameEnd()
        {
            var (played, record) = Play(Chart(), 7, 1 / 60.0, 0.1);
            var whole = new ReplayFeed(record);
            Assert.That(State(whole.SessionAt(Chart(), 1000)), Is.EqualTo(State(played)));
            var early = whole.SessionAt(Chart(), -5);
            Assert.That(early.Score, Is.Zero);
            Assert.That(whole.Remaining, Is.EqualTo(record.Inputs.Count));
            foreach (double start in new[] { 2.0, 5.5, 9.25 })
            {
                var feed = new ReplayFeed(record);
                var session = feed.SessionAt(Chart(), start);
                Assert.That(session.Good + session.Ok + session.Bad, Is.GreaterThan(0), "Judged up to " + start);
                Assert.That(feed.Remaining, Is.EqualTo(record.Inputs.Count(i => i.Ms / 1000 > start)));
                for (double time = start; time < 60; time += 1 / 50.0)
                {
                    while (feed.TryTake(time, out double at, out bool ka, out _)) session.Hit(ka, at);
                    session.Advance(time, false);
                }
                Assert.That(State(session), Is.EqualTo(State(played)), "from " + start);
            }
        }

        [Test] public void TheRecordKeepsTheOptionsAndRebuildsTheirChart()
        {
            var options = new PlayOptions { speed = 25, inverse = true, display = true, random = RandomMode.Detarame };
            var chart = Chart();
            ChartModifiers.Apply(chart, options, new Random(5));
            Assert.That(ChartModifiers.FlippedIn(chart), Is.Not.Empty);
            var record = new PlayRecord { AudioOffsetMs = 4, VisualOffsetMs = -2 };
            record.SetOptions(options, chart);
            record.Inputs.Add((120.5, 2));
            var json = (JObject)record.ToJson();
            Assert.That((int)json["version"], Is.EqualTo(2));
            Assert.That(json["options"].ToString(Newtonsoft.Json.Formatting.None), Does.StartWith("{\"speed\":25,\"inverse\":true,\"display\":true,\"random\":2,\"flipped\":["));
            var read = PlayRecord.FromJson(JToken.Parse(json.ToString()));
            Assert.That(read.Flipped, Is.EqualTo(record.Flipped));
            Assert.That((read.Speed, read.Inverse, read.Display, read.Random, read.AudioOffsetMs, read.VisualOffsetMs), Is.EqualTo((25, true, true, 2, 4, -2)));
            Assert.That(read.Inputs, Is.EqualTo(new[] { (120.5, 2) }));
            var again = Chart();
            ChartModifiers.Apply(again, read);
            Assert.That(again.Notes.Select(n => (n.Kind, n.ScrollX, n.Display)), Is.EqualTo(chart.Notes.Select(n => (n.Kind, n.ScrollX, n.Display))));
            Assert.That(again.Notes.Select(n => n.Kind), Is.Not.EqualTo(Chart().Notes.Select(n => n.Kind)));
            Assert.That(ChartModifiers.FlippedIn(again), Is.EqualTo(record.Flipped));
            Assert.That(ChartModifiers.FlippedIn(Chart()), Is.Empty);
        }

        [Test] public void AVersionOneRecordReadsAsAPlayWithoutOptionsAndBadRecordsAreRefused()
        {
            var old = PlayRecord.FromJson(JToken.Parse("{\"version\":1,\"audio_offset_ms\":3,\"visual_offset_ms\":0,\"inputs\":[[10,1],[20.5,3]]}"));
            Assert.That(old.Inputs, Is.EqualTo(new[] { (10.0, 1), (20.5, 3) }));
            Assert.That((old.Speed, old.Inverse, old.Display, old.Random, old.Flipped.Count), Is.EqualTo((10, false, false, 0, 0)));
            Assert.That((PlayRecord.IsKa(0), PlayRecord.IsKa(1), PlayRecord.IsKa(2), PlayRecord.IsKa(3)), Is.EqualTo((true, false, false, true)));
            Assert.That((PlayRecord.IsRight(0), PlayRecord.IsRight(1), PlayRecord.IsRight(2), PlayRecord.IsRight(3)), Is.EqualTo((false, false, true, true)));
            foreach (string bad in new[]
            {
                "null", "[]", "{\"version\":3,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[]}",
                "{\"version\":1,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[[1,4]]}",
                "{\"version\":1,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[[\"1\",1]]}",
                "{\"version\":1,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[[1e12,1]]}",
                "{\"version\":2,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[]}",
                "{\"version\":2,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[],\"options\":{\"speed\":0,\"inverse\":false,\"display\":false,\"random\":0,\"flipped\":[]}}",
                "{\"version\":2,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[],\"options\":{\"speed\":10,\"inverse\":false,\"display\":false,\"random\":3,\"flipped\":[]}}",
                "{\"version\":2,\"audio_offset_ms\":0,\"visual_offset_ms\":0,\"inputs\":[],\"options\":{\"speed\":10,\"inverse\":false,\"display\":false,\"random\":1,\"flipped\":[2,2]}}",
            })
                Assert.That(PlayRecord.FromJson(JToken.Parse(bad)), Is.Null, bad);
        }
    }
}
