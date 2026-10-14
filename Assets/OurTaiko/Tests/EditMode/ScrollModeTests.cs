using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // #NMSCROLL / #BMSCROLL / #HBSCROLL as OpenTaiko reads them (plan: OurTaikoLogs
    // OurTaikoPlay/Documentation/2026-10-14-bmscroll-plan.md). Stage 1: modes and the beat clock.
    public sealed class ScrollModeTests
    {
        static TaikoChart Parse(string body, string beforeStart = "", string header = "")
            => TjaParser.Parse("TITLE:Scroll\nBPM:120\n" + header + "\nCOURSE:Oni\n" + beforeStart + "\n#START\n" + body + "\n#END");

        static ScrollMode[] Modes(TaikoChart chart) => chart.Notes.Select(n => n.ScrollMode).ToArray();

        // T1
        [Test] public void ModesFollowTheCommandsBeforeAndInTheChart()
        {
            var chart = Parse("1,\n#HBSCROLL\n1,\n#NMSCROLL\n1,", "#BMSCROLL");
            Assert.That(Modes(chart), Is.EqualTo(new[] { ScrollMode.BMScroll, ScrollMode.HBScroll, ScrollMode.Normal }));
            Assert.That(chart.Bars.Select(b => b.ScrollMode), Is.EqualTo(Modes(chart)), "Bar lines take the mode too.");
            // Before COURSE: the command is part of the global headers, which OpenTaiko ignores.
            Assert.That(Modes(Parse("1,", header: "#BMSCROLL")), Is.EqualTo(new[] { ScrollMode.Normal }));
        }

        [Test] public void RoutesStartFromTheBranchStartMode()
        {
            var chart = Parse("#HBSCROLL\n1,\n#BRANCHSTART p,0,0\n#N\n#BMSCROLL\n1,\n#E\n1,\n#M\n#NMSCROLL\n1,\n#BRANCHEND\n1,");
            ScrollMode Route(BranchRoute route) => chart.Notes.Single(n => n.BranchId == 0 && n.Route == route).ScrollMode;
            Assert.That(new[] { Route(BranchRoute.Normal), Route(BranchRoute.Expert), Route(BranchRoute.Master) },
                Is.EqualTo(new[] { ScrollMode.BMScroll, ScrollMode.HBScroll, ScrollMode.Normal }));
            Assert.That(chart.Notes.Last().ScrollMode, Is.EqualTo(ScrollMode.Normal), "After the branch the last route's mode goes on.");
        }

        // T2
        [Test] public void TheClockCountsWrittenMeasuresAndDelays()
        {
            // The empty measure (2.75–3.75) and the delay at BPM 240 (+0.5) both advance the clock.
            var chart = Parse("1111,\n#MEASURE 3/4\n111,\n#MEASURE 4/4\n#BPMCHANGE 240\n1,\n,\n#DELAY 0.5\n1,\n5008,");
            Assert.That(chart.Notes.Select(n => n.ScrollClock), Is.EqualTo(new[] { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 4.25, 5.25 }));
            Assert.That(chart.Notes.Select(n => n.Time), Is.EqualTo(new[] { 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 6, 7 }));
            Assert.That(chart.Notes.Last().EndScrollClock, Is.EqualTo(6), "A long note's tail has its own clock.");
            Assert.That(chart.Bars.Select(b => b.ScrollClock), Is.EqualTo(new[] { 0, 1, 1.75, 2.75, 4.25, 5.25 }));
        }

        [Test] public void RoutesStartFromTheBranchStartClock()
        {
            var chart = Parse("1,\n#BRANCHSTART p,0,0\n#N\n1,\n#E\n11,\n#M\n#MEASURE 1/2\n1,\n1,\n#BRANCHEND\n1,");
            double[] Route(BranchRoute route) => chart.Notes.Where(n => n.BranchId == 0 && n.Route == route).Select(n => n.ScrollClock).ToArray();
            Assert.That(Route(BranchRoute.Normal), Is.EqualTo(new[] { 1.0 }));
            Assert.That(Route(BranchRoute.Expert), Is.EqualTo(new[] { 1, 1.5 }));
            Assert.That(Route(BranchRoute.Master), Is.EqualTo(new[] { 1, 1.5 }));
            Assert.That(chart.Notes.Last().ScrollClock, Is.EqualTo(2), "After the branch the last route's clock goes on.");
        }

        // T3
        [Test] public void BpmPointsMarkTheStartBpmChangesAndBranches()
        {
            var chart = Parse("1,\n#BPMCHANGE 240\n1,\n#BRANCHSTART p,0,0\n#N\n1,\n#E\n1,\n#M\n#BPMCHANGE 60\n1,\n#BRANCHEND\n1,", header: "OFFSET:1");
            var points = chart.ScrollClocks.Select(p => (p.Time, p.Clock, p.Bpm, p.BranchId, p.BranchId < 0 ? (BranchRoute?)null : p.Route)).ToArray();
            Assert.That(points, Is.EqualTo(new (double, double, double, int, BranchRoute?)[]
            {
                (-1, 0, 120, -1, null),                    // #START
                (1, 1, 240, -1, null),                     // common #BPMCHANGE
                (2, 2, 240, -1, null),                     // branch start
                (2, 2, 60, 0, BranchRoute.Master),         // #BPMCHANGE in 達人
                (6, 3, 60, -1, null),                      // branch end, from the last route (達人)
            }));
        }

        // Stage 2 ------------------------------------------------------------------------
        const double Travel = 1302;

        // T5
        [Test] public void TheClockRunsAtTheBpmAndThroughDelays()
        {
            // Measure 1 at 120 (0–2 s), measure 2 at 240 (2–3 s), a 1 s delay, the last note at 4 s.
            var chart = Parse("1,\n#BPMCHANGE 240\n1,\n#DELAY 1\n1,");
            var clocks = new ScrollClockTimeline(chart);
            double At(double t) => clocks.At(BranchRoute.Normal, t);
            Assert.That(At(-2), Is.EqualTo(-1), "Before the start it runs back at the first BPM.");
            Assert.That(At(1), Is.EqualTo(0.5));
            Assert.That(At(2.5), Is.EqualTo(1.5));
            Assert.That(At(3.5), Is.EqualTo(2.5), "It keeps running through the delay.");
            Assert.That(At(4), Is.EqualTo(chart.Notes[2].ScrollClock), "A note reaches the judge at its own time.");
        }

        [Test] public void EachRouteHasItsClockUntilTheBranchEnds()
        {
            // The branch is 2–4 s; 達人 slows to 60 BPM (and so is written last, its length decides).
            var chart = Parse("1,\n#BRANCHSTART p,0,0\n#N\n1,\n#E\n1,\n#M\n#BPMCHANGE 60\n1,\n#BRANCHEND\n1,");
            var clocks = new ScrollClockTimeline(chart);
            Assert.That(clocks.At(BranchRoute.Normal, 3), Is.EqualTo(1.5));
            Assert.That(clocks.At(BranchRoute.Master, 3), Is.EqualTo(1.25));
            // The branch ends at 6 s (達人's 4 s measure): every route goes on from 達人's clock 2.
            Assert.That(clocks.At(BranchRoute.Normal, 6), Is.EqualTo(2));
            Assert.That(clocks.At(BranchRoute.Expert, 7), Is.EqualTo(2.25));
            Assert.That(ScrollClockTimeline.RouteOf(chart.Notes[0]), Is.EqualTo(BranchRoute.Normal), "Common notes read the Normal clock.");
        }

        // T6
        [Test] public void PositionsFollowTheModes()
        {
            var normal = Parse("#SCROLL 2\n1,\n#BPMCHANGE 240\n1,");
            var bm = Parse("#BMSCROLL\n#SCROLL 2+1i\n1,\n#BPMCHANGE 240\n1,\n1,");
            var hb = Parse("#HBSCROLL\n#SCROLL 2+1i\n1,\n#BPMCHANGE 240\n1,\n1,");
            (double, double) At(TaikoChart chart, int note, double t) => NoteScroll.Distance(chart.Notes[note], t, new ScrollClockTimeline(chart), Travel);
            // NM is unchanged: the second note keeps its own 240 BPM and SCROLL 2 from afar.
            Assert.That(At(normal, 1, 1), Is.EqualTo((NoteScroll.DistanceFromJudge(2, 1, 240, 2, Travel), 0.0)));
            Assert.That(At(normal, 1, 1).Item1, Is.EqualTo(2604));
            // BM: half a measure to go at 1 s, SCROLL ignored, no vertical part.
            Assert.That(At(bm, 1, 1), Is.EqualTo((651.0, 0.0)));
            // HB: the same distance times SCROLL 2+1i.
            Assert.That(At(hb, 1, 1), Is.EqualTo((1302.0, 651.0)));
            // After the change to 240 every BM/HB note on the lane speeds up together: in the
            // 0.25 s from 2 s the third note (clock 2) comes half a measure closer.
            Assert.That(At(bm, 2, 2).Item1 - At(bm, 2, 2.25).Item1, Is.EqualTo(Travel / 4));
            Assert.That(At(bm, 2, 1.5).Item1 - At(bm, 2, 1.75).Item1, Is.EqualTo(Travel / 8), "Before the change it came at 120.");
        }

        // T7
        [Test] public void RollsAreRigidInEveryMode()
        {
            var hb = Parse("#HBSCROLL\n#SCROLL 2\n5000,\n#BPMCHANGE 60\n0008,");
            var roll = hb.Notes[0];
            Assert.That(NoteScroll.RollLength(roll, Travel), Is.EqualTo(1.75 * 2 * Travel));
            var bm = Parse("#BMSCROLL\n#SCROLL 2\n5008,");
            Assert.That(NoteScroll.RollLength(bm.Notes[0], Travel), Is.EqualTo(0.75 * Travel), "BM ignores SCROLL.");
            var normal = Parse("#SCROLL 2\n5008,");
            Assert.That(NoteScroll.RollLength(normal.Notes[0], Travel), Is.EqualTo(NoteScroll.DistanceFromJudge(1.5, 0, 120, 2, Travel)));
        }

        // T8 (the full cull comparison is ChartCursorTests' "Synthetic BM and HB scrolls")
        [Test] public void ClockIntervalsCoverTheLane()
        {
            var chart = Parse("#BMSCROLL\n1,\n#BPMCHANGE 240\n1,\n1,");
            var clocks = new ScrollClockTimeline(chart);
            // Lane 1422 wide, judge at 120: the third note (clock 2) enters at clock 1 (1302 px out)
            // and leaves at clock 2 + 120 / 1302.
            LaneCull.ClockInterval(chart.Notes[2], clocks, Travel, 120, 1422, 0, out double start, out double end);
            Assert.That(start, Is.EqualTo(2).Within(1e-9));
            Assert.That(end, Is.EqualTo(3 + 120.0 / 1302).Within(1e-9));
            var still = Parse("#HBSCROLL\n#SCROLL 0\n1,");
            LaneCull.ClockInterval(still.Notes[0], new ScrollClockTimeline(still), Travel, 120, 1422, 0, out start, out end);
            Assert.That((start, end), Is.EqualTo((double.NegativeInfinity, double.PositiveInfinity)));
        }

        // T4
        [TestCase("#BMSCROLL")]
        [TestCase("#HBSCROLL")]
        [TestCase("#NMSCROLL")]
        public void ScrollCommandsAreRead(string command)
        {
            var chart = Parse(command + "\n1,");
            Assert.That(chart.Warnings, Is.Empty);
            Assert.That(chart.Notes.Single().Time, Is.Zero);
        }
    }
}
