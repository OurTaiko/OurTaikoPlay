using System;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class TjaShinuchiTests
    {
        static TaikoChart Parse(string body, string course = "Oni", string metadata = "")
            => ScoredTja.Parse("BPM:120\nCOURSE:" + course + "\n" + metadata + "\n#START\n" + body + "\n#END", course);

        static ShinuchiScore Score(TaikoChart chart) => new ShinuchiScore(new ChartStatistics(chart));

        [TestCase("Easy", 998900)] // floor(6 * 1.5) = 9; ceil(9*9/8) = 11
        [TestCase("Normal", 998700)]
        [TestCase("Hard", 998300)]
        [TestCase("Oni", 997500)]
        [TestCase("Edit", 997500)]
        [TestCase("Ura", 997500)]
        [TestCase("4", 997500)]
        public void RollBudgetUsesCourseAndBothIntegerSteps(string course, int initial)
        {
            var score = Score(Parse("1000,\n5008,", course));
            Assert.That(score.BaseScore, Is.EqualTo(initial));
            Assert.That(score.CeilingScore, Is.EqualTo(1000000));
        }

        [Test]
        public void QuantizeEachBeatBeforeSummingAndDoNotAddAnEpsilon()
        {
            // Six BPM-180 beats are 1.999998 seconds, so k=29, H=33 (not k=30, H=34).
            var score = Score(Parse("1000,\n5000,\n0080,", metadata: "BPM:180"));
            Assert.That(score.BaseScore, Is.EqualTo(996700));
            Assert.That(score.CeilingScore, Is.EqualTo(1000000));
        }

        [TestCase(3, 2, 997700)]
        [TestCase(96, 1, 1000000)]
        public void OffGridTailUsesAtMostOneSlot(int slots, int tail, int initial)
        {
            string roll = "5" + new string('0', tail - 1) + "8" + new string('0', slots - tail - 1) + ",";
            var score = Score(Parse("1000,\n" + roll));
            // The three-slot roll ends at 8/3 + 1/12 = 11/4 beats: H=23.
            // The 96-slot roll lasts 1/24 + 1/24 = 1/12 beat: k=0, H=0.
            Assert.That(score.BaseScore, Is.EqualTo(initial));
        }

        [Test]
        public void AggregateRollsBeforeFlooring()
        {
            // Each roll lasts 1/24 second; individually k=0, together k=1 and H=2.
            string roll = "58" + new string('0', 94) + ",";
            Assert.That(Score(Parse("1000,\n" + roll + "\n" + roll)).BaseScore, Is.EqualTo(999800));
        }

        [Test]
        public void IntegrateTempoChangesAndOnlyDelaysInsideRolls()
        {
            // 2 beats at 120 + 1 beat at 240 + .25 delay = 1.5 seconds, H=25.
            var score = Score(Parse("#DELAY 10\n1000,\n50\n#BPMCHANGE 240\n#DELAY .25\n08,\n#DELAY 10"));
            Assert.That(score.BaseScore, Is.EqualTo(997500));
        }

        [Test]
        public void BalloonBudgetIsNotCappedAtOneHundred()
        {
            // 150 hits in 3 seconds is within the validated 60 hits/s limit.
            var score = Score(Parse("1110,\n7000,\n0080,", metadata: "BALLOON:150"));
            Assert.That(score.BaseScore, Is.EqualTo(328340));
            Assert.That(score.CeilingScore, Is.EqualTo(1000020));
        }

        [TestCase("1110,\n5000,\n0080,", "", 331640, 1000020)]
        [TestCase("1110,\n5000,\n,\n0080,", "", 330500, 1000000)]
        [TestCase("1110,\n#MEASURE 1/3\n5008,", "", 333070, 1000010)]
        [TestCase("1110,\n5000000000008000,", "", 332500, 1000000)]
        [TestCase("1110,\n5008", "", 332500, 1000000)]
        [TestCase("1110,\n50/* 999, #DELAY -1 */08 // comment", "", 332500, 1000000)]
        [TestCase("1110,\n#GOGOSTART\n60\n08,", "OFFSET:1.2345", 332500, 1000000)]
        public void SourceGridTimingAndRoundUpToTenPoints(string body, string metadata, int initial, int ceiling)
        {
            var score = Score(Parse(body, metadata: metadata));
            Assert.That(score.BaseScore, Is.EqualTo(initial));
            Assert.That(score.CeilingScore, Is.EqualTo(ceiling));
        }

        [Test]
        public void SourceAnalysisDoesNotChangePlaybackTiming()
        {
            const string text = "BPM:180\nOFFSET:.314\nCOURSE:Oni\n#START\n1110,\n#MEASURE 1/3\n50\n#BPMCHANGE 137\n#DELAY .3\n08,\n#END";
            var legacy = TjaParser.Parse(text);
            var chart = ScoredTja.Parse(text);
            Assert.That(chart.Notes.Count, Is.EqualTo(legacy.Notes.Count));
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                Assert.That(chart.Notes[i].Time, Is.EqualTo(legacy.Notes[i].Time));
                Assert.That(chart.Notes[i].EndTime, Is.EqualTo(legacy.Notes[i].EndTime));
                Assert.That(chart.Notes[i].Beat, Is.EqualTo(legacy.Notes[i].Beat));
            }
        }

        [Test]
        public void OnlyTheRequestedCourseContributes()
        {
            const string text = "BPM:120\nCOURSE:Easy\n#START\n1110,\n#END\nCOURSE:Oni\n#START\n1110,\n5008,\n#END";
            Assert.That(Score(ScoredTja.Parse(text, "Easy")).BaseScore, Is.EqualTo(333340));
            Assert.That(Score(ScoredTja.Parse(text, "Oni")).BaseScore, Is.EqualTo(332500));
        }

        [TestCase("1110,\n5008,", "#START P1", "p1")]
        [TestCase("1110,\n9008,", "#START", "single")]
        [TestCase("1110,\n7008,", "#START", "single")]
        [TestCase("1110,\n#DELAY -1\n5008,", "#START", "single")]
        [TestCase("1110,\n5000,", "#START", "single")]
        [TestCase("1110,\n5\n#MEASURE 3/4\n008,", "#START", "single")]
        [TestCase("111G,\n5008,", "#START", "single")]
        [TestCase("0000,\n5008,", "#START", "single")]
        public void OutsideValidatedScopeKeepsLegacyScoring(string body, string start, string side)
        {
            string text = "BPM:120\nCOURSE:Oni\nBALLOON:150\n" + start + "\n" + body + "\n#END";
            var expected = Score(TjaParser.Parse(text, "Oni", side));
            var actual = Score(ScoredTja.Parse(text, "Oni", side));
            Assert.That(actual.BaseScore, Is.EqualTo(expected.BaseScore));
            Assert.That(actual.CeilingScore, Is.EqualTo(expected.CeilingScore));
        }

        [Test]
        public void ParsingStillRejectsInvalidCharts()
            => Assert.Throws<FormatException>(() => Parse("#BPMCHANGE 0\n1000,"));
    }
}
