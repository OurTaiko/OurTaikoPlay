using System;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class BranchTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Branch Test\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";
        static TaikoChart Accuracy(string condition = "p,50,80", string lead = "1111,\n0000,\n0000,") =>
            Parse(lead + "\n#BRANCHSTART " + condition + Routes);

        [Test] public void RoutesShareStartAndRestoreTimingAndBalloonCursor()
        {
            var chart = Parse("7008,\n#BRANCHSTART p,50,80\n#N\n#BPMCHANGE 240\n#SCROLL 2\n#MEASURE 3/4\n#DELAY 0.25\n#GOGOSTART\n#BARLINEOFF\n7008,\n#E\n7008,\n#M\n7008,\n#BRANCHEND\n7008,", "BALLOON:2,3,4");
            var n = chart.Notes.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Normal);
            var e = chart.Notes.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Expert);
            var m = chart.Notes.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Master);
            Assert.That(n.Time, Is.EqualTo(2.25)); Assert.That(n.Bpm, Is.EqualTo(240));
            Assert.That(n.ScrollX, Is.EqualTo(2)); Assert.That(n.Gogo, Is.True);
            Assert.That(e.Time, Is.EqualTo(2)); Assert.That(e.Bpm, Is.EqualTo(120));
            Assert.That(e.ScrollX, Is.EqualTo(1)); Assert.That(e.Gogo, Is.False);
            Assert.That(m.Time, Is.EqualTo(2));
            Assert.That(new[] { n.BalloonHits, e.BalloonHits, m.BalloonHits }, Is.EqualTo(new[] { 3, 3, 3 }));
            Assert.That(chart.Notes.Last().Time, Is.EqualTo(4)); Assert.That(chart.Notes.Last().BalloonHits, Is.EqualTo(4));
            Assert.That(chart.Duration, Is.EqualTo(6));
            Assert.That(chart.Bars.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Normal).Display, Is.False);
            Assert.That(chart.Bars.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Expert).Display, Is.True);
        }

        [Test] public void DecisionUsesMasterFirstBarLoadIncludingHiddenAndEmptyMeasures()
        {
            var chart = Parse("0000,\n0000,\n0000,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n2000,\n#M\n#BARLINEOFF\n#SCROLL 2\n,\n#BRANCHEND");
            var branch = chart.Branches.Single();
            Assert.That(branch.Time, Is.EqualTo(6));
            Assert.That(branch.ArmTime, Is.EqualTo(2));
            Assert.That(branch.DecisionTime, Is.EqualTo(6 - 930.0 / 866).Within(1e-9));
            Assert.That(branch.FirstEntries[2].Display, Is.False);
        }

        [TestCase(0, 1, 6)]
        [TestCase(0, 0, 6)]
        [TestCase(-1, 0, 6)]
        public void LoadBoundaryHandlesVerticalStationaryAndReverseScroll(double x, double y, double time)
        {
            var note = new ChartNote { Time = time, Bpm = 120, ScrollX = x, ScrollY = y };
            Assert.That(NoteScroll.LoadTime(note), Is.EqualTo(x == 0 && y == 0 ? 6 : 6 - 930.0 / 433).Within(1e-9));
        }

        [TestCase(4, 0, BranchRoute.Master, 100)]
        [TestCase(3, 0, BranchRoute.Expert, 75)]
        [TestCase(1, 2, BranchRoute.Expert, 50)]
        [TestCase(1, 0, BranchRoute.Normal, 25)]
        [TestCase(0, 0, BranchRoute.Normal, 0)]
        public void AccuracyUsesGoodHalfOkAndBad(int good, int ok, BranchRoute expected, int percentage)
        {
            var session = new PlaySession(Accuracy());
            for (int i = 0; i < good + ok; i++) session.Hit(false, i * 0.5 + (i >= good ? 0.05 : 0));
            double at = session.Chart.Branches[0].DecisionTime;
            session.Advance(at - 0.001, false);
            Assert.That(session.SelectedRoute(0), Is.Null);
            session.Advance(at, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
            Assert.That(session.LastBranchValue, Is.EqualTo(percentage));
            Assert.That(session.Chart.Notes.Where(n => n.BranchId == 0 && session.IsActive(n)).All(n => n.Route == expected), Is.True);
        }

        [Test] public void PercentageIsTruncatedAndNegativeExpertThresholdIsSkipped()
        {
            var session = new PlaySession(Accuracy("p,66.5,90", "1110,\n0000,\n0000,"));
            session.Hit(false, 0); session.Hit(false, 0.5); session.Advance(4, false);
            Assert.That(session.LastBranchValue, Is.EqualTo(66)); Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
            var forced = new PlaySession(Accuracy("p,-1,0")); forced.Advance(4, false);
            Assert.That(forced.CurrentBranch, Is.EqualTo(BranchRoute.Master));
            var empty = new PlaySession(Accuracy("p,-1,100", "0000,\n0000,\n0000,")); empty.Advance(4, false);
            Assert.That(empty.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
        }

        [TestCase(0, BranchRoute.Normal)]
        [TestCase(3, BranchRoute.Expert)]
        [TestCase(5, BranchRoute.Master)]
        public void DrumrollCountsRepeatedSameFrameHits(int hits, BranchRoute expected)
        {
            var session = new PlaySession(Accuracy("r,3,5", "5008,\n0000,\n0000,"));
            for (int i = 0; i < hits; i++) session.Hit(i % 2 == 0, 0.1);
            session.Advance(4, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(expected)); Assert.That(session.LastBranchValue, Is.EqualTo(hits));
        }

        [Test] public void BalloonHitsDoNotCountAsDrumrollBranchHits()
        {
            var session = new PlaySession(Accuracy("r,1,3", "7008,\n0000,\n0000,"));
            for (int i = 0; i < 5; i++) session.Hit(false, 0.1);
            session.Advance(4, false);
            Assert.That(session.Rolls, Is.EqualTo(5)); Assert.That(session.LastBranchValue, Is.Zero);
        }

        [Test] public void SectionResetsAccuracyButActiveRollFallbackMatchesReference()
        {
            var session = new PlaySession(Accuracy("p,50,80", "1111,\n#SECTION\n1000,\n0000,"));
            for (int i = 0; i < 4; i++) session.Hit(false, i * 0.5);
            session.Advance(4, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal)); Assert.That(session.LastBranchValue, Is.Zero);
            var roll = new PlaySession(Accuracy("r,3,5", "5000,\n#SECTION\n0000,\n0008,"));
            for (int i = 0; i < 5; i++) roll.Hit(false, 0.1);
            roll.Advance(4, false);
            Assert.That(roll.CurrentBranch, Is.EqualTo(BranchRoute.Master)); Assert.That(roll.LastBranchValue, Is.EqualTo(5));
            var ended = new PlaySession(Accuracy("r,3,5", "5000,\n#SECTION\n0000,\n0008,\n0000,"));
            for (int i = 0; i < 5; i++) ended.Hit(false, 0.1);
            ended.Advance(6, false);
            Assert.That(ended.LastBranchValue, Is.Zero);
        }

        [Test] public void UnselectedNotesCannotConsumeInputMissOrInflateScore()
        {
            var chart = Accuracy(); var session = new PlaySession(chart);
            session.Advance(4, false); // All misses -> Normal.
            Assert.That(session.Hit(true, 6), Is.EqualTo(Judgment.None));
            Assert.That(session.Hit(false, 6), Is.EqualTo(Judgment.Good));
            int score = session.Score; session.Advance(10, false);
            Assert.That(session.Good, Is.EqualTo(1)); Assert.That(session.Bad, Is.EqualTo(4));
            Assert.That(session.Score, Is.EqualTo(score)); Assert.That(score, Is.EqualTo(125000));
            Assert.That(chart.Notes.Select((n, i) => n.BranchId < 0 || n.Route == BranchRoute.Normal || !session.Resolved[i]).All(x => x), Is.True);
        }

        [Test] public void ConsecutiveBranchesAndCoarseAutoplayMatchSmallSteps()
        {
            string block = "\n#N\n1000,\n1000,\n#E\n2000,\n2000,\n#M\n1100,\n0000,";
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80" + block + "\n#BRANCHSTART p,50,80" + block);
            var coarse = new PlaySession(chart); coarse.Advance(chart.Duration + 1, true);
            var fine = new PlaySession(chart);
            for (double t = -2; t < chart.Duration + 1; t += 1.0 / 120) fine.Advance(t, true);
            Assert.That(coarse.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
            Assert.That(coarse.BranchHistory, Is.EqualTo(fine.BranchHistory));
            Assert.That(coarse.Score, Is.EqualTo(fine.Score)); Assert.That(coarse.Bad, Is.Zero);
            Assert.That(coarse.Good, Is.EqualTo(chart.Notes.Count(n => !n.IsLong && coarse.IsActive(n))));
        }

        [Test] public void DecisionsResetCountersAndUnchosenSectionDoesNotResetThem()
        {
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80\n#N\n#SECTION\n0000,\n#E\n0000,\n#M\n1000,\n#BRANCHEND\n0000,\n0000,\n#BRANCHSTART p,50,80" + Routes);
            var session = new PlaySession(chart); session.Advance(chart.Duration + 1, true);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
            var reset = new PlaySession(Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80\n#N\n0000,\n#E\n0000,\n#M\n0000,\n#BRANCHEND\n0000,\n0000,\n#BRANCHSTART p,50,80" + Routes));
            reset.Advance(reset.Chart.Duration + 1, true);
            Assert.That(reset.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Normal }));
        }

        [TestCase("#N\n1000,")]
        [TestCase("#BRANCHSTART p,50,80\n#N\n1000,\n#E\n1000,\n#BRANCHEND")]
        [TestCase("#BRANCHSTART p,50,80\n#N\n1000,\n#N\n1000,")]
        [TestCase("#BRANCHSTART p,50,80\n#N\n5000,\n#E\n0008,")]
        [TestCase("10\n#BRANCHSTART p,50,80")]
        [TestCase("#BRANCHSTART p,50")]
        public void MalformedBranchIsRejected(string body) => Assert.Throws<FormatException>(() => Parse(body));

        [TestCase("#BRANCHSTART s,100,200")]
        [TestCase("#LEVELHOLD")]
        public void UnsupportedBranchExtensionsAreExplicit(string command) => Assert.Throws<NotSupportedException>(() => Parse(command));
    }
}
