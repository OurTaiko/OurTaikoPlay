using System;

namespace OurTaiko
{
    // Which side of its note a practice timing label goes. Labels sit above their notes; where two
    // labelled notes crowd each other on screen, the run of crowded notes alternates above and below.
    // Pure: no scene objects, so it is checked in EditMode.
    public static class NoteLabelRows
    {
        public const sbyte Above = 0, Below = 1;

        // One row per chart note of `session` (its practice route). Notes without a label (rolls,
        // balloons, mines, Ad-Libs, other routes) are Above and do not break a run.
        public static sbyte[] Compute(PlaySession session, ScrollClockTimeline clocks)
        {
            var notes = session.Chart.Notes;
            var rows = new sbyte[notes.Count];
            // Numbered in note order, misses included, so a note keeps its row whatever part of the
            // lane is browsed.
            int ordinal = 0, previous = -1;
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (!Labelled(session, note)) continue;
                if (previous >= 0 && Crowded(notes[previous], note, clocks))
                {
                    rows[previous] = (sbyte)((ordinal - 1) % 2);
                    rows[i] = (sbyte)(ordinal % 2);
                }
                ordinal++;
                previous = i;
            }
            return rows;
        }

        public static bool Labelled(PlaySession session, ChartNote note)
            => !note.IsLong && !note.IsMine && !note.IsAdLib && session.IsPracticePreviewActive(note);

        // Closer than a 32nd note at SCROLL 1 on both axes, measured where the lane draws them with the
        // earlier note at the judge: so a vertical or complex #SCROLL, a #BPMCHANGE between them and
        // HB/BM scroll all count, and playback speed does not.
        public static bool Crowded(ChartNote earlier, ChartNote later, ScrollClockTimeline clocks)
        {
            // Distance cuts to whole pixels; on a 2^20-times finer grid the cut cannot move a note across
            // the limit, and the two-unit margin keeps an exact 32nd (1/8 beat at SCROLL 1) apart.
            const double Fine = 1 << 20, Travel = NoteScroll.MeasureWidth * Fine;
            var a = NoteScroll.RawDistance(earlier, earlier.Time, clocks, Travel);
            var b = NoteScroll.RawDistance(later, earlier.Time, clocks, Travel);
            double limit = Travel / 32 - 2;
            return Math.Abs(b.X - a.X) < limit && Math.Abs(b.Y - a.Y) < limit;
        }
    }
}
