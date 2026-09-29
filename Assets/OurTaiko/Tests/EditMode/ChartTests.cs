using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class ChartTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse("TITLE:Test\nBPM:120\n" + metadata + "\nCOURSE:Oni\n#START\n" + body + "\n#END");

        [Test] public void OffsetAndEmptyMeasuresKeepAudioTime()
        {
            var chart = Parse("1000,\n,\n2000,", "OFFSET:0.5");
            Assert.That(chart.Notes.Select(n => n.Time), Is.EqualTo(new[] { -0.5, 3.5 }).Within(0.000001));
            Assert.That(chart.Duration, Is.EqualTo(5.5).Within(0.000001));
        }
        [Test] public void CommandsInsideMeasureKeepSubdivisionTiming()
        {
            var chart = Parse("10\n#BPMCHANGE 240\n#SCROLL 1-0.5i\n20,");
            Assert.That(chart.Notes[1].Time, Is.EqualTo(1).Within(0.000001));
            Assert.That(chart.Duration, Is.EqualTo(1.5).Within(0.000001));
            Assert.That(chart.Notes[1].ScrollY, Is.EqualTo(-0.5));
        }
        [Test] public void MeasureDelayAndGogoAreApplied()
        {
            var chart = Parse("#MEASURE 3/4\n#DELAY 0.25\n#GOGOSTART\n100,\n#GOGOEND\n200,");
            Assert.That(chart.Notes[0].Time, Is.EqualTo(0.25));
            Assert.That(chart.Notes[1].Time, Is.EqualTo(1.75));
            Assert.That(chart.Notes[0].Gogo, Is.True);
            Assert.That(chart.Notes[1].Gogo, Is.False);
        }
        [Test] public void LongNoteLinksAcrossMeasures()
        {
            var chart = Parse("5000,\n0080,");
            Assert.That(chart.Notes.Count, Is.EqualTo(1));
            Assert.That(chart.Notes[0].EndTime, Is.EqualTo(3));
        }
        [Test] public void CourseSelectionUsesRequestedDifficulty()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Easy\nLEVEL:1\n#START\n1000,\n#END\nCOURSE:3\nLEVEL:9\nBALLOON:3\n#START\n7008,\n#END", "Oni");
            Assert.That(chart.Level, Is.EqualTo(9)); Assert.That(chart.Notes[0].BalloonHits, Is.EqualTo(3));
        }
        [Test] public void UnsupportedBranchIsExplicit()
        {
            Assert.Throws<NotSupportedException>(() => Parse("#BRANCHSTART p,50,80\n1000,"));
            Assert.Throws<FormatException>(() => Parse("5000,"));
        }
        [Test] public void WrongColorDoesNotConsumeNoteAndMissResetsCombo()
        {
            var session = new PlaySession(Parse("1200,"));
            Assert.That(session.Hit(true, 0), Is.EqualTo(Judgment.None));
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            Assert.That(session.Combo, Is.EqualTo(1));
            session.Advance(0.7, false);
            Assert.That(session.Bad, Is.EqualTo(1)); Assert.That(session.Combo, Is.Zero);
        }
        [Test] public void TimingWindowsAndDuplicateInput()
        {
            Assert.That(new PlaySession(Parse("1000,")).Hit(false, 0.02), Is.EqualTo(Judgment.Good));
            Assert.That(new PlaySession(Parse("1000,")).Hit(false, 0.06), Is.EqualTo(Judgment.Ok));
            Assert.That(new PlaySession(Parse("1000,")).Hit(false, 0.1), Is.EqualTo(Judgment.Bad));
            var session = new PlaySession(Parse("1000,")); session.Hit(false, 0); session.Hit(false, 0);
            Assert.That(session.Good, Is.EqualTo(1));
        }
        [Test] public void RollInputsInSameFrameAreNotDropped()
        {
            var session = new PlaySession(Parse("5008,"));
            session.Hit(false, 0.5); session.Hit(true, 0.5);
            Assert.That(session.Rolls, Is.EqualTo(2));
            Assert.That(session.Combo, Is.Zero);
        }
        [Test] public void BalloonOnlyConsumesDonAndPopsOnce()
        {
            var session = new PlaySession(TjaParser.Parse("BPM:120\nCOURSE:Oni\nBALLOON:2\n#START\n7008,\n#END"));
            Assert.That(session.Hit(true, 0.2), Is.EqualTo(Judgment.None));
            session.Hit(false, 0.2); session.Hit(false, 0.2); session.Hit(false, 0.2);
            Assert.That(session.Rolls, Is.EqualTo(2)); Assert.That(session.Score, Is.EqualTo(5600)); Assert.That(session.Resolved[0], Is.True);
        }
        [TestCase("TripleHelix", "Oni")]
        [TestCase("Calibration", "Hard")]
        public void ImportedChartParsesAndAutoPlayCompletesWithoutMisses(string name, string course)
        {
            var chart = TjaParser.Parse(File.ReadAllText("Assets/OurTaiko/Songs/" + name + ".txt"), course);
            Assert.That(chart.Notes.Count, Is.GreaterThan(50));
            var session = new PlaySession(chart);
            for (double t = -2; t < chart.Duration + 2; t += 1.0 / 60) session.Advance(t, true);
            Assert.That(session.Good, Is.EqualTo(chart.Notes.Count(n => !n.IsLong)));
            Assert.That(session.Bad, Is.Zero); Assert.That(session.Resolved.All(x => x), Is.True);
        }
    }
}
