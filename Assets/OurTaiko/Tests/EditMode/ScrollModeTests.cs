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
