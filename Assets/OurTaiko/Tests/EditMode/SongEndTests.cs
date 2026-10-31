using NUnit.Framework;

namespace OurTaiko.Tests
{
    // OpenTaiko's end of a play: the chart end two seconds after the last note, the fade-out a
    // second after #END, both no later than the end of the music. 120 BPM: a measure is 2 s.
    public sealed class SongEndTests
    {
        static TaikoChart Chart(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");

        [Test] public void TheChartEndsAfterItsLastNoteAndFadesAfterEnd()
        {
            var chart = Chart("1000,\n1000,\n0000,\n0000,");
            Assert.That(chart.Duration, Is.EqualTo(8).Within(1e-9));
            var end = SongEnd.Of(chart, double.NaN);
            Assert.That(end.ChartEnd, Is.EqualTo(4).Within(1e-9));
            Assert.That(end.FadeOut, Is.EqualTo(9).Within(1e-9));
        }

        [Test] public void TheChartEndIsNeverLaterThanTheFadeOut()
        {
            var end = SongEnd.Of(Chart("0001,"), double.NaN);
            Assert.That(end.ChartEnd, Is.EqualTo(3).Within(1e-9), "1.5 + 2 is past #END + 1");
            Assert.That(end.FadeOut, Is.EqualTo(3).Within(1e-9));
        }

        [Test] public void ARollCountsUntilItsEnd()
        {
            var end = SongEnd.Of(Chart("5000,\n0008,\n0000,\n0000,"), double.NaN);
            Assert.That(end.ChartEnd, Is.EqualTo(5.5).Within(1e-9));
            Assert.That(end.FadeOut, Is.EqualTo(9).Within(1e-9));
        }

        [Test] public void MusicEndingFirstEndsBoth()
        {
            var chart = Chart("1000,\n1000,\n#BPMCHANGE 10\n0,");
            Assert.That(chart.Duration, Is.GreaterThan(20));
            var end = SongEnd.Of(chart, 10);
            Assert.That(end.ChartEnd, Is.EqualTo(4).Within(1e-9));
            Assert.That(end.FadeOut, Is.EqualTo(10).Within(1e-9));

            // Notes past the music's end: the last one before it decides.
            var cut = SongEnd.Of(Chart("1000,\n1000,\n1000,"), 3);
            Assert.That(cut.ChartEnd, Is.EqualTo(3).Within(1e-9));
            Assert.That(cut.FadeOut, Is.EqualTo(3).Within(1e-9));

            // Music longer than the chart leaves the chart's own points.
            var longer = SongEnd.Of(Chart("1000,\n1000,\n0000,\n0000,"), 60);
            Assert.That(longer.ChartEnd, Is.EqualTo(4).Within(1e-9));
            Assert.That(longer.FadeOut, Is.EqualTo(9).Within(1e-9));
        }

        [Test] public void AChartWithoutNotesEndsAtItsFadeOut()
        {
            var end = SongEnd.Of(Chart("0000,"), double.NaN);
            Assert.That(end.ChartEnd, Is.EqualTo(3).Within(1e-9));
            Assert.That(end.FadeOut, Is.EqualTo(3).Within(1e-9));
        }

        [Test] public void ALateJudgeOffsetMovesOnlyTheChartsPoints()
        {
            var chart = Chart("1000,\n1000,\n0000,\n0000,");
            var end = SongEnd.Of(chart, double.NaN, 0.1);
            Assert.That(end.ChartEnd, Is.EqualTo(4.1).Within(1e-9));
            Assert.That(end.FadeOut, Is.EqualTo(9.1).Within(1e-9));
            Assert.That(SongEnd.Of(chart, double.NaN, -0.1).FadeOut, Is.EqualTo(9).Within(1e-9));
            Assert.That(SongEnd.Of(chart, 5, 0.1).FadeOut, Is.EqualTo(5).Within(1e-9));
        }

        [Test] public void EverySeatMustReachEachPoint()
        {
            var both = SongEnd.Latest(new SongEnd(4, 9), new SongEnd(6, 7));
            Assert.That(both.ChartEnd, Is.EqualTo(6));
            Assert.That(both.FadeOut, Is.EqualTo(9));
        }
    }
}
