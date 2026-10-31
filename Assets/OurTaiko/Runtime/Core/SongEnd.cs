using System;

namespace OurTaiko
{
    // When a play ends, in chart seconds (OpenTaiko CTja.InsertEndOfChartChips). Two pairs of 0xFF
    // chips are placed, one a second after #END and one at the end of the music, and the first
    // of each kind decides: the chart end starts the ending, the fade-out lets the play leave
    // once that ending has played. The chart end is two seconds after the last note before its
    // fade-out (a roll counts until its end), never later than the fade-out itself.
    public readonly struct SongEnd
    {
        public const double FadeOutDelay = 1, LastNoteDelay = 2;

        public readonly double ChartEnd, FadeOut;

        public SongEnd(double chartEnd, double fadeOut) { ChartEnd = chartEnd; FadeOut = fadeOut; }

        // audioEnd: the music's end in chart time, or NaN without music. lateJudge (a positive
        // judge offset) moves the chart's own pair later, as the notes are judged that much later.
        public static SongEnd Of(TaikoChart chart, double audioEnd, double lateJudge = 0)
        {
            double late = Math.Max(0, lateJudge);
            double fadeOut = chart.Duration + FadeOutDelay + late;
            double chartEnd = ChartEndBefore(chart, fadeOut, late);
            if (!double.IsNaN(audioEnd))
            {
                chartEnd = Math.Min(chartEnd, ChartEndBefore(chart, audioEnd, late));
                fadeOut = Math.Min(fadeOut, audioEnd);
            }
            return new SongEnd(chartEnd, fadeOut);
        }

        // Every seat must reach each point (OpenTaiko IsChartEnded / IsFinishedPlaying: all players).
        public static SongEnd Latest(SongEnd a, SongEnd b) => new SongEnd(Math.Max(a.ChartEnd, b.ChartEnd), Math.Max(a.FadeOut, b.FadeOut));

        // FindLastHittableOrChip: the latest end among the notes starting by the fade-out chip.
        static double ChartEndBefore(TaikoChart chart, double fadeOut, double late)
        {
            double last = double.NegativeInfinity;
            foreach (var note in chart.Notes)
                if (note.Time + late <= fadeOut) last = Math.Max(last, (note.IsLong ? note.EndTime : note.Time) + late);
            // Without such a note the chip found is the fade-out itself.
            return double.IsNegativeInfinity(last) ? fadeOut : Math.Min(last + LastNoteDelay, fadeOut);
        }
    }
}
