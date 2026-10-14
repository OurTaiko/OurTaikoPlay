namespace OurTaiko
{
    public static class NoteScroll
    {
        // Nijiiro's native 1920-wide field: judge X=618 and 192-pixel notes.
        public const double DefaultTravelDistance = 1302, NoteHalfWidth = 96;

        // Same entry boundary as Player::get_load_time, including the sprite radius.
        public static double LoadTime(ChartNote note)
        {
            double scroll = System.Math.Abs(note.ScrollX);
            if (scroll == 0) scroll = System.Math.Abs(note.ScrollY);
            double speed = note.Bpm / 240.0 * scroll * DefaultTravelDistance;
            return speed == 0 ? note.Time : note.Time - (DefaultTravelDistance + NoteHalfWidth) / speed;
        }

        // OurTaikoPlayer Player::get_position_x/y uses BPM / 240000 in milliseconds.
        // Our clock is seconds: at SCROLL 1, four beats span the right edge-to-judge distance.
        public static double DistanceFromJudge(double hitTime, double currentTime, double bpm,
            double scroll, double travelDistance)
        {
            return (hitTime - currentTime) * bpm / 240.0 * scroll * travelDistance;
        }

        // A note's distance from the judge on both axes, as OpenTaiko places it for its scroll mode:
        // NM by its own time and BPM; BM and HB by the beat clock (measures to go × travel), BM with
        // SCROLL 1 and no vertical part, HB with its SCROLL.
        public static (double X, double Y) Distance(ChartNote note, double currentTime, ScrollClockTimeline clocks,
            double travelDistance)
        {
            if (note.ScrollMode == ScrollMode.Normal)
                return (DistanceFromJudge(note.Time, currentTime, note.Bpm, note.ScrollX, travelDistance),
                    DistanceFromJudge(note.Time, currentTime, note.Bpm, note.ScrollY, travelDistance));
            double measures = note.ScrollClock - clocks.At(ScrollClockTimeline.RouteOf(note), currentTime);
            return (measures * ScrollX(note) * travelDistance, measures * ScrollY(note) * travelDistance);
        }

        // The SCROLL a note moves by: BM-scroll ignores the chart's (OpenTaiko forces 1 and no Y).
        public static double ScrollX(ChartNote note) => note.ScrollMode == ScrollMode.BMScroll ? 1 : note.ScrollX;
        public static double ScrollY(ChartNote note) => note.ScrollMode == ScrollMode.BMScroll ? 0 : note.ScrollY;

        // A roll is a rigid body: its tail inherits the head's BPM and SCROLL (and scroll mode),
        // even when chart commands change before the tail. Its length stays constant.
        public static double RollLength(ChartNote note, double travelDistance)
        {
            if (note.ScrollMode != ScrollMode.Normal)
                return (note.EndScrollClock - note.ScrollClock) * ScrollX(note) * travelDistance;
            return DistanceFromJudge(note.EndTime, note.Time, note.Bpm, note.ScrollX, travelDistance);
        }
    }
}
