using System;
using System.Linq;

namespace OurTaiko
{
    // The beat clock BM/HB-scroll notes are placed by (OpenTaiko GetNowPBPMPoint / GetNowPBMTime),
    // one per route: from the latest BPM point at or before a time it runs at that point's BPM, in
    // measures of four beats; before the first point it runs back from it. A branch's end resets
    // every route to the clock of the route written last, so a route's clock may jump there.
    public sealed class ScrollClockTimeline
    {
        readonly ChartClockPoint[][] routes = new ChartClockPoint[3][];

        public ScrollClockTimeline(TaikoChart chart)
        {
            // A chart built without TjaParser has no points: its clock starts at 0 s.
            var points = chart.ScrollClocks.Count > 0 ? chart.ScrollClocks.ToArray()
                : new[] { new ChartClockPoint { Bpm = chart.Bpm } };
            for (int r = 0; r < 3; r++)
                routes[r] = points.Where(p => p.BranchId < 0 || (int)p.Route == r).ToArray();
        }

        // OpenTaiko plays only the Normal copy of a common note, so it reads the Normal clock.
        public static BranchRoute RouteOf(ChartNote note) => note.BranchId < 0 ? BranchRoute.Normal : note.Route;

        public double At(BranchRoute route, double time)
        {
            var points = routes[(int)route];
            int low = 0, high = points.Length;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (points[mid].Time <= time) low = mid + 1;
                else high = mid;
            }
            var point = points[Math.Max(0, low - 1)];
            return point.Clock + (time - point.Time) * point.Bpm / 240;
        }

        // The span of times during which the route's clock is within [from, to]. Where the clock
        // jumps back the true set has gaps; the span covers them, which is enough for culling.
        public void Span(BranchRoute route, double from, double to, out double start, out double end)
        {
            var points = routes[(int)route];
            start = double.PositiveInfinity; end = double.NegativeInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                var point = points[i];
                double segmentStart = i == 0 ? double.NegativeInfinity : point.Time;
                double segmentEnd = i + 1 < points.Length ? points[i + 1].Time : double.PositiveInfinity;
                double enter = point.Time + (from - point.Clock) * 240 / point.Bpm;
                double leave = point.Time + (to - point.Clock) * 240 / point.Bpm;
                enter = Math.Max(enter, segmentStart); leave = Math.Min(leave, segmentEnd);
                if (!(enter <= leave)) continue;
                start = Math.Min(start, enter); end = Math.Max(end, leave);
            }
        }
    }
}
