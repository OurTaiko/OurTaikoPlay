using System;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class TjaShinuchiTests
    {
        static TaikoChart Parse(string body, string course = "Oni", string metadata = "")
            => TjaParser.Parse("BPM:120\nCOURSE:" + course + "\n" + metadata + "\n#START\n" + body + "\n#END", course);
        static ShinuchiScore Score(TaikoChart chart) => new ShinuchiScore(new ChartStatistics(chart));

        [TestCase("Easy", 998900)]
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
        public void AllRollsAreSummedBeforeTheSingleFloorAndCeiling()
        {
            string roll = "58" + new string('0', 94) + ",\n";
            Assert.That(Score(Parse("1000,\n" + roll + roll)).BaseScore, Is.EqualTo(999800));
            Assert.That(Score(Parse("1000,\n5000,\n0080,", metadata: "BPM:180")).BaseScore, Is.EqualTo(996700));
        }

        [TestCase("1110,\n7008,", 328340, 1000020)]
        [TestCase("1110,\n9008,", 328340, 1000020)]
        [TestCase("111G,\n5008,", 249380, 1000020)]
        [TestCase("111F,\n5008,", 332500, 1000000)]
        [TestCase("111C,\n5008,", 332500, 1000000)]
        [TestCase("1110,\n5000,", 332200, 1000000)]
        [TestCase("1110,\n5\n#MEASURE 3/4\n008,", 332640, 1000020)]
        public void SpecialNotesAndToleratedSyntaxUseOneFormula(string body, int initial, int ceiling)
        {
            var score = Score(Parse(body, metadata: "BALLOON:150"));
            Assert.That(score.BaseScore, Is.EqualTo(initial));
            Assert.That(score.CeilingScore, Is.EqualTo(ceiling));
        }

        [TestCase("p1")]
        [TestCase("p2")]
        public void PlayerBlocksUseTheSameFormula(string side)
        {
            const string text = "BPM:120\nCOURSE:Oni\n#START P1\n1110,\n5008,\n#END\n#START P2\n1110,\n5008,\n#END";
            var score = Score(TjaParser.Parse(text, "Oni", side));
            Assert.That(score.BaseScore, Is.EqualTo(332500));
            Assert.That(score.CeilingScore, Is.EqualTo(1000000));
        }

        [Test]
        public void CommonAndMasterRoutesSupplyTheBudget()
        {
            var chart = Parse("1000,\n5008,\n#BRANCHSTART p,50,80\n#N\n1111,\n#E\n7008,\n#M\n1100,\n5008,\n9008,\n#BRANCHEND", metadata: "BALLOONMAS:150");
            var stats = new ChartStatistics(chart);
            Assert.That(stats.JudgeableNotes, Is.EqualTo(3));
            Assert.That(stats.BalloonHitBudget, Is.EqualTo(150));
            Assert.That(stats.DrumrollScoreMicroseconds, Is.EqualTo((TjaFraction)3000000));
            Assert.That(Score(chart).BaseScore, Is.EqualTo(326640));
            Assert.That(Score(chart).CeilingScore, Is.EqualTo(1000020));
        }

        [Test]
        public void MissingMasterUsesTheExistingRouteResolution()
        {
            var chart = Parse("1000,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n1100,\n5008,\n#BRANCHEND");
            Assert.That(new ChartStatistics(chart).JudgeableNotes, Is.EqualTo(3));
            Assert.That(Score(chart).BaseScore, Is.EqualTo(332500));
        }

        [TestCase(9999, 1000000)]
        [TestCase(10000, 1000020)]
        [TestCase(10001, 1000120)]
        public void ExhaustedBudgetStillAwardsAtLeastTenPerGood(int hits, int ceiling)
        {
            var score = Score(Parse("1100,\n7008,", metadata: "BALLOON:" + hits));
            Assert.That(score.BaseScore, Is.EqualTo(hits == 9999 ? 50 : 10));
            Assert.That(score.CeilingScore, Is.EqualTo(ceiling));
            Assert.That(score.ApplyJudgment(Judgment.Good), Is.EqualTo(score.BaseScore));
        }

        [TestCase("0000,", 1000000)]
        [TestCase("7008,", 1000100)]
        public void NoJudgedNotesKeepsMillionPointBase(string body, int ceiling)
        {
            var score = Score(Parse(body, metadata: "BALLOON:10001"));
            Assert.That(score.BaseScore, Is.EqualTo(1000000));
            Assert.That(score.CeilingScore, Is.EqualTo(ceiling));
        }

        [Test]
        public void HugeBalloonBudgetsDoNotOverflowBeforeThresholdSaturation()
        {
            var chart = Parse("1000,\n7008,\n9008,", metadata: "BALLOON:2147483647,2147483647");
            Assert.That(new ChartStatistics(chart).BalloonHitBudget, Is.EqualTo(4294967294L));
            Assert.That(Score(chart).BaseScore, Is.EqualTo(10));
            Assert.That(Score(chart).CeilingScore, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void NegativeRequirementsAndReversedRollsCannotSubtractFromBudget()
        {
            var chart = Parse("1000,\n5\n#DELAY -3\n008,\n7008,", metadata: "BALLOON:-5");
            Assert.That(new ChartStatistics(chart).BalloonHitBudget, Is.Zero);
            Assert.That(new ChartStatistics(chart).DrumrollScoreMicroseconds, Is.EqualTo(default(TjaFraction)));
            Assert.That(Score(chart).BaseScore, Is.EqualTo(1000000));
        }

        [Test]
        public void ConstructedChartsUseTheSameExplicitDurationContract()
        {
            var chart = new TaikoChart();
            chart.Notes.Add(new ChartNote { Kind = NoteKind.Don });
            chart.Notes.Add(new ChartNote { Kind = NoteKind.Roll, ScoreDuration = 1500000 });
            Assert.That(Score(chart).BaseScore, Is.EqualTo(997500));
        }

        [Test]
        public void OnlyTheRequestedCourseContributes()
        {
            const string text = "BPM:120\nCOURSE:Easy\n#START\n1110,\n#END\nCOURSE:Oni\n#START\n1110,\n5008,\n#END";
            Assert.That(Score(TjaParser.Parse(text, "Easy")).BaseScore, Is.EqualTo(333340));
            Assert.That(Score(TjaParser.Parse(text, "Oni")).BaseScore, Is.EqualTo(332500));
        }
    }
}
