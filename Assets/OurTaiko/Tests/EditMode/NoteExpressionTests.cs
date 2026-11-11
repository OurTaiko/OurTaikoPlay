using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class NoteExpressionTests
    {
        static TaikoChart Parse(string body, double offset = 0) => TjaParser.Parse(
            "BPM:120\nOFFSET:" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\nCOURSE:Oni\nLEVEL:1\n#START\n" + body + "\n#END");

        [TestCase(9, 4, 0)]
        [TestCase(10, 1.999, 0)]
        [TestCase(10, 2, 1)]
        [TestCase(10, 2.499, 1)]
        [TestCase(10, 2.5, 3)]
        [TestCase(49, 4.25, 1)] // Still eighths.
        [TestCase(50, 3.75, 3)] // Wait for beat 4 before sixteenths.
        [TestCase(50, 4, 1)]
        [TestCase(50, 4.249, 1)]
        [TestCase(50, 4.25, 3)]
        [TestCase(50, 4.5, 1)]
        [TestCase(99, 6, 1)]
        [TestCase(100, 5.75, 3)] // Wait for beat 6 before the angry face.
        [TestCase(100, 5.99999999, 2)] // Within the beat tolerance of 6.
        [TestCase(100, 6, 2)]
        [TestCase(100, 6.249, 2)]
        [TestCase(100, 6.25, 3)]
        [TestCase(101, 6.5, 2)]
        [TestCase(200, 6.75, 3)]
        // 0 rest, 1 open, 2 angry, 3 closed (NoteExpression).
        public void ThresholdsSwitchOnWholeBeats(int combo, double beat, int expected)
            => Assert.That(NoteExpression.Frame(beat, combo, 2, 4, 6), Is.EqualTo(expected));

        [TestCase(1.75, 2)] // A in the reference image.
        [TestCase(4, 4)]
        [TestCase(4.00000000001, 4)]
        public void ThresholdStartRoundsUpToTheAuthoredWholeBeat(double beat, double expected)
            => Assert.That(NoteExpression.StartBeat(beat), Is.EqualTo(expected));

        static PlaySession ReachCombo(string body, int combo, double hitOffset = 0)
        {
            var session = new PlaySession(Parse(body));
            for (int i = 0; i < combo; i++)
                Assert.That(session.Hit(false, session.Chart.Notes[i].Time + hitOffset), Is.EqualTo(Judgment.Good));
            return session;
        }

        [TestCase(-0.01)]
        [TestCase(0.01)]
        public void TenOnAnOffbeatWaitsForTheNextWholeBeat(double hitOffset)
        {
            var session = ReachCombo(new string('1', 9) + ",\n01000000,\n1,", 10, hitOffset);
            Assert.That(session.NoteExpressionFrame(2.25), Is.EqualTo(NoteExpression.Rest));
            Assert.That(session.NoteExpressionFrame(2.499), Is.EqualTo(NoteExpression.Rest));
            Assert.That(session.NoteExpressionFrame(2.5), Is.EqualTo(NoteExpression.Open));
            Assert.That(session.NoteExpressionFrame(2.75), Is.EqualTo(NoteExpression.Closed));
        }

        [TestCase(-0.01)]
        [TestCase(0.01)]
        public void TenOnAWholeBeatStartsOnThatBeat(double hitOffset)
        {
            var session = ReachCombo(new string('1', 9) + ",\n1,", 10, hitOffset);
            Assert.That(session.NoteExpressionFrame(2), Is.EqualTo(NoteExpression.Open));
            Assert.That(session.NoteExpressionFrame(2.25), Is.EqualTo(NoteExpression.Closed));
        }

        [Test]
        public void FiftyWaitsForWholeBeatBeforeSixteenths()
        {
            // 10 combo starts eighths on beat 1; the 50th note, on beat 4.5, starts sixteenths on beat 5.
            var session = ReachCombo(new string('1', 49) + ",\n01000000,\n1,", 50);
            Assert.That(session.NoteExpressionFrame(2.0), Is.EqualTo(NoteExpression.Open));
            Assert.That(session.NoteExpressionFrame(2.375), Is.EqualTo(NoteExpression.Closed));
            Assert.That(session.NoteExpressionFrame(2.5), Is.EqualTo(NoteExpression.Open));
            Assert.That(session.NoteExpressionFrame(2.625), Is.EqualTo(NoteExpression.Closed), "Sixteenths: closed a quarter beat in.");
            Assert.That(session.NoteExpressionFrame(2.75), Is.EqualTo(NoteExpression.Open));
        }

        [Test]
        public void AngryTierWaitsForWholeBeatAndMissResetsEveryTier()
        {
            var session = ReachCombo(new string('1', 99) + ",\n01000000,\n1,", 100);
            Assert.That(session.NoteExpressionFrame(2.0), Is.EqualTo(NoteExpression.Open), "Keep opening the mouth on sixteenths before beat 5.");
            Assert.That(session.NoteExpressionFrame(2.375), Is.EqualTo(NoteExpression.Closed));
            Assert.That(session.NoteExpressionFrame(2.5), Is.EqualTo(NoteExpression.Angry));
            Assert.That(session.NoteExpressionFrame(2.625), Is.EqualTo(NoteExpression.Closed));
            Assert.That(session.NoteExpressionFrame(2.75), Is.EqualTo(NoteExpression.Angry));
            session.Advance(4.2, false);
            Assert.That(session.Combo, Is.Zero);
            Assert.That(session.NoteExpressionFrame(4.5), Is.EqualTo(NoteExpression.Rest));
            Assert.That(PlaySession.PracticeAt(session.Chart, 2, session).NoteExpressionFrame(2.5), Is.EqualTo(NoteExpression.Rest));
        }

        [Test]
        public void BeatPhaseUsesOffsetTempoAndDelay()
        {
            // Tempo anchors sit on whole milliseconds, so each beat may be off by up to 1 ms of beats.
            const double Ms = 0.004;
            var session = new PlaySession(Parse("100\n#BPMCHANGE 180\n1,\n#DELAY 0.3\n1,", 0.37));
            Assert.That(session.BeatAt(-0.37), Is.EqualTo(0).Within(Ms));
            Assert.That(session.BeatAt(1.13), Is.EqualTo(3).Within(Ms));
            double end = 1.13 + 1.0 / 3;
            Assert.That(session.BeatAt(end + 0.1), Is.EqualTo(4).Within(Ms));
            Assert.That(session.BeatAt(end + 0.3), Is.EqualTo(4).Within(Ms));
            Assert.That(session.BeatAt(end + 0.3 + 1.0 / 6), Is.EqualTo(4.5).Within(Ms));
            Assert.That(session.BeatAt(-0.12), Is.EqualTo(0.5).Within(Ms));
        }

        [Test]
        public void SilentTempoCommandsUseTheirExactTimeAndSupportBackwardSeeking()
        {
            var session = new PlaySession(Parse("0,\n#BPMCHANGE 240\n#DELAY 1\n0001,", 0.5));
            Assert.That(session.BpmAt(1.499), Is.EqualTo(120));
            Assert.That(session.BpmAt(1.5), Is.EqualTo(240));
            Assert.That(session.BpmAt(2.4), Is.EqualTo(240), "No note or bar is needed to apply BPMCHANGE.");
            Assert.That(session.BpmAt(-1), Is.EqualTo(120), "A backward seek must restore the earlier tempo.");
        }

        [Test]
        public void MidMeasureAndSameTimeChangesKeepSourceOrderAndBeatPhase()
        {
            var session = new PlaySession(Parse("10\n#BPMCHANGE 180\n#BPMCHANGE 240\n01,"));
            Assert.That(session.Chart.Tempos[0].Time, Is.EqualTo(1));
            Assert.That(session.BpmAt(0.999), Is.EqualTo(120));
            Assert.That(session.BpmAt(1), Is.EqualTo(240));
            Assert.That(session.BeatAt(1.13), Is.EqualTo(2.52).Within(1e-8));
        }

        [TestCase(BranchRoute.Normal, 120)]
        [TestCase(BranchRoute.Expert, 180)]
        [TestCase(BranchRoute.Master, 240)]
        public void TempoFollowsOnlyTheSelectedRouteIncludingPracticePreview(BranchRoute route, double bpm)
        {
            var chart = Parse("1,\n#BRANCHSTART p,50,80\n#N\n0,\n#E\n#BPMCHANGE 180\n0,\n#M\n#BPMCHANGE 240\n0,\n#BRANCHEND\n#BPMCHANGE 150\n0,");
            var session = new PlaySession(chart, 0, route);
            Assert.That(session.BpmAt(2.1, preview: true), Is.EqualTo(bpm));
            Assert.That(session.BeatAt(2.1, preview: true), Is.EqualTo(4 + 0.1 * bpm / 60).Within(1e-8));
            session.Advance(2.1, true);
            Assert.That(session.BpmAt(2.1), Is.EqualTo(bpm));
            Assert.That(session.BpmAt(1.9), Is.EqualTo(120));
            Assert.That(session.BpmAt(3), Is.EqualTo(150));
        }
    }
}
