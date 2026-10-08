using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class NoteExpressionTests
    {
        static TaikoChart Parse(string body, double offset = 0) => TjaParser.Parse(
            "BPM:120\nOFFSET:" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\nCOURSE:Oni\nLEVEL:1\n#START\n" + body + "\n#END");

        [TestCase(49, 0.25, 120, 0)]
        [TestCase(50, 0.249, 120, 0)]
        [TestCase(50, 0.25, 120, 1)]
        [TestCase(50, 0.5, 120, 0)]
        [TestCase(100, 0.125, 240, 1)]
        [TestCase(0, 0.25, 120, 0)]
        [TestCase(50, 1, 0, 0)]
        public void FiftyComboEnablesEighthNoteFrames(int combo, double time, double bpm, int expected)
            => Assert.That(NoteExpression.Frame(time, bpm, combo), Is.EqualTo(expected));

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
        public void MidMeasureAndSameTimeChangesKeepSourceOrderAndAbsolutePhase()
        {
            var session = new PlaySession(Parse("10\n#BPMCHANGE 180\n#BPMCHANGE 240\n01,"));
            Assert.That(session.Chart.Tempos[0].Time, Is.EqualTo(1));
            Assert.That(session.BpmAt(0.999), Is.EqualTo(120));
            Assert.That(session.BpmAt(1), Is.EqualTo(240));
            // At 1.13 s the source computes trunc(1.13 / .125) % 2 = 1.
            Assert.That(NoteExpression.Frame(1.13, session.BpmAt(1.13), 50), Is.EqualTo(1));
        }

        [TestCase(BranchRoute.Normal, 120)]
        [TestCase(BranchRoute.Expert, 180)]
        [TestCase(BranchRoute.Master, 240)]
        public void TempoFollowsOnlyTheSelectedRouteIncludingPracticePreview(BranchRoute route, double bpm)
        {
            var chart = Parse("1,\n#BRANCHSTART p,50,80\n#N\n0,\n#E\n#BPMCHANGE 180\n0,\n#M\n#BPMCHANGE 240\n0,\n#BRANCHEND\n#BPMCHANGE 150\n0,");
            var session = new PlaySession(chart, 0, route);
            Assert.That(session.BpmAt(2.1, preview: true), Is.EqualTo(bpm));
            session.Advance(2.1, true);
            Assert.That(session.BpmAt(2.1), Is.EqualTo(bpm));
            Assert.That(session.BpmAt(1.9), Is.EqualTo(120));
            Assert.That(session.BpmAt(3), Is.EqualTo(150));
        }
    }
}
