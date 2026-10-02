using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class NoteScrollTests
    {
        const double Distance = 1280 - 414;

        // Independent reference: original Player::get_position_x in ms, normal scroll mode.
        [TestCase(120, 1, 1)]
        [TestCase(160, 1, 1)]
        [TestCase(160, 2, 1)]
        [TestCase(200, -1, 0.5)]
        [TestCase(160, 0, 1)]
        [TestCase(160, 0.5, -0.5)]
        public void MatchesOriginalMillisecondsFormula(double bpm, double scroll, double remaining)
        {
            double original = remaining * 1000 * (bpm / 240000 * scroll * (1280 - 414));
            Assert.That(NoteScroll.DistanceFromJudge(10, 10 - remaining, bpm, scroll, Distance),
                Is.EqualTo(original).Within(0.000001));
        }

        [TestCase(120)]
        [TestCase(160)]
        [TestCase(240)]
        public void OneFourBeatMeasureTravelsFromRightEdgeToJudge(double bpm)
        {
            Assert.That(NoteScroll.DistanceFromJudge(240 / bpm, 0, bpm, 1, Distance), Is.EqualTo(866).Within(0.000001));
            Assert.That(NoteScroll.DistanceFromJudge(60 / bpm, 0, bpm, 1, Distance), Is.EqualTo(216.5).Within(0.000001));
            Assert.That(NoteScroll.DistanceFromJudge(10, 10, bpm, 1, Distance), Is.Zero);
        }

        [Test] public void ConstantSpeedRollHasLengthOfItsTimeInterval()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n5008,\n#END");
            Assert.That(NoteScroll.RollLength(chart.Notes[0], Distance), Is.EqualTo(649.5).Within(0.000001));
        }

        [Test] public void BpmChangeInsideRollKeepsHeadSpeedButAffectsFollowingNotes()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n50\n#BPMCHANGE 240\n08,\n1000,\n#END");
            var note = chart.Notes[0];
            Assert.That(note.EndTime, Is.EqualTo(1.25));
            Assert.That(note.Bpm, Is.EqualTo(120));
            Assert.That(chart.Notes[1].Bpm, Is.EqualTo(240));
            Assert.That(NoteScroll.RollLength(note, Distance), Is.EqualTo(541.25).Within(0.000001));
        }

        [Test] public void ScrollChangeInsideRollKeepsHeadSpeedButAffectsFollowingNotes()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n#SCROLL 2\n50\n#SCROLL 1\n08,\n1000,\n#END");
            Assert.That(chart.Notes[0].ScrollX, Is.EqualTo(2));
            Assert.That(chart.Notes[1].ScrollX, Is.EqualTo(1));
            Assert.That(NoteScroll.RollLength(chart.Notes[0], Distance), Is.EqualTo(1299).Within(0.000001));
        }

        [Test] public void ResolutionScalePreservesTravelTime()
        {
            double normal = NoteScroll.DistanceFromJudge(2, 0.5, 160, 1, Distance);
            double scaled = NoteScroll.DistanceFromJudge(2, 0.5, 160, 1, Distance * 1.5);
            Assert.That(scaled, Is.EqualTo(normal * 1.5).Within(0.000001));
        }
    }
}
