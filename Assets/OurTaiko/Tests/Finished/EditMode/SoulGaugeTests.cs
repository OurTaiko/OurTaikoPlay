using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SoulGaugeTests
    {
        // Reference gauge.h rows, exercised through successive judgments rather than table access.
        [TestCase("Easy", 1, 166.6666666666667, 541.6666666666667)]
        [TestCase("Easy", 3, 157.89556787141, 513.160595582083)]
        [TestCase("Easy", 5, 136.364256201165, 443.183832653786)]
        [TestCase("Normal", 1, 152.439024390244, 495.426829268293)]
        [TestCase("Normal", 3, 143.884892086331, 467.625899280576)]
        [TestCase("Normal", 4, 142.247510668563, 426.74253200569)]
        [TestCase("Normal", 7, 133.333333333333, 366.666666666667)]
        [TestCase("Hard", 1, 128.865979381443, 386.59793814433)]
        [TestCase("Hard", 3, 137.931034482759, 379.310344827586)]
        [TestCase("Hard", 4, 144.613159797542, 373.101952277657)]
        [TestCase("Hard", 5, 148.148148148148, 370.37037037037)]
        [TestCase("Hard", 8, 145.475705557172, 363.68926389293)]
        [TestCase("Oni", 7, 141.342756183746, 268.551236749117)]
        [TestCase("Oni", 8, 142.857142857143, 214.285714285714)]
        [TestCase("Oni", 9, 130.293159609121, 195.439739413681)]
        [TestCase("Edit", 10, 130.293159609121, 195.439739413681)]
        public void DifficultyAndStarsControlEachJudgment(string course, int level, double firstGood, double mixedTotal)
        {
            var gauge = new SoulGauge(100, course, level);
            gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(firstGood).Within(1e-8));
            gauge.ApplyJudgment(Judgment.Good); gauge.ApplyJudgment(Judgment.Good);
            gauge.ApplyJudgment(Judgment.Ok); gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.EqualTo(mixedTotal).Within(1e-8));
            double previous = gauge.Points;
            gauge.ApplyJudgment(Judgment.None); gauge.ApplyJudgment(Judgment.Roll);
            Assert.That(gauge.Points, Is.EqualTo(previous));
        }

        [TestCase("Easy")]
        [TestCase("Normal")]
        [TestCase("Hard")]
        [TestCase("Oni")]
        [TestCase("Edit")]
        public void AbsentLevelUsesOniTenGaugeIncludingClearTier(string course)
        {
            var session = new PlaySession(new TaikoChart { Course = course });
            Assert.That(session.ClearThreshold, Is.EqualTo(0.8));
            var gauge = new SoulGauge(100, course, 0);
            gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(130.293159609121).Within(1e-8));
        }

        [TestCase("Easy", 6)]
        [TestCase("Normal", 8)]
        [TestCase("Hard", 9)]
        public void UnusedStarRowsRetainTheReferenceZeroGauge(string course, int level)
        {
            var gauge = new SoulGauge(1, course, level);
            gauge.ApplyJudgment(Judgment.Good); gauge.ApplyJudgment(Judgment.Ok); gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.Zero);
            Assert.That(gauge.IsClear, Is.False);
        }

        [Test]
        public void StarLimitsAndZeroNoteDenominatorFollowReference()
        {
            var low = new SoulGauge(100, "Easy", -1);
            low.ApplyJudgment(Judgment.Good);
            Assert.That(low.Points, Is.EqualTo(166.6666666666667).Within(1e-8));
            var high = new SoulGauge(100, "Oni", 20);
            high.ApplyJudgment(Judgment.Good);
            Assert.That(high.Points, Is.EqualTo(130.293159609121).Within(1e-8));
            var empty = new SoulGauge(0, "Oni", 10);
            empty.ApplyJudgment(Judgment.Good);
            Assert.That(empty.Points, Is.EqualTo(10000));
        }

        [Test]
        public void ClearAndFullBoundariesSnapAndBadJudgmentsClampPerHit()
        {
            var gauge = new SoulGauge(100, "Easy", 1);
            gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.Zero);
            for (int i = 0; i < 35; i++) gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Percent, Is.EqualTo(58));
            Assert.That(gauge.IsClear, Is.False);
            gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(6000));
            Assert.That(gauge.Percent, Is.EqualTo(60));
            Assert.That(gauge.IsClear, Is.True);
            for (int i = 0; i < 24; i++) gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(10000));
            Assert.That(gauge.IsFull, Is.True);
            gauge.ApplyJudgment(Judgment.Good); gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.EqualTo(9916.666666666666).Within(1e-8), "An extra full-gauge hit cannot bank progress above 10000.");
            Assert.That(gauge.IsFull, Is.False);
            for (int i = 0; i < 48; i++) gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.IsClear, Is.False);
            Assert.That(gauge.Points, Is.EqualTo(5916.666666666666).Within(1e-8));
        }

        [Test]
        public void ManualBadAndMissApplyTheSamePenaltyAndRestartClearsTheState()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\nLEVEL:8\n#START\n1111,\n#END");
            var session = new PlaySession(chart);
            session.Hit(false, 0); session.Hit(false, 0.5); session.Hit(false, 1.06);
            Assert.That(session.GaugePoints, Is.EqualTo(8928.571428571428).Within(1e-8));
            Assert.That(session.Hit(false, 1.6), Is.EqualTo(Judgment.Bad));
            Assert.That(session.GaugePoints, Is.EqualTo(1785.714285714286).Within(1e-8));
            var missed = new PlaySession(chart);
            missed.Hit(false, 0); missed.Hit(false, 0.5); missed.Hit(false, 1.06);
            missed.Advance(1.7, false);
            Assert.That(missed.GaugePoints, Is.EqualTo(session.GaugePoints));
            Assert.That(missed.GaugePercent, Is.EqualTo(17));
            var restarted = new PlaySession(chart);
            Assert.That(restarted.Score, Is.Zero); Assert.That(restarted.GaugePoints, Is.Zero);
        }
    }
}
