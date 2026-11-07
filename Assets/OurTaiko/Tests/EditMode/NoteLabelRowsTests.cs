using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // Where the practice timing review puts each label (NoteLabelRows): above, unless labelled notes
    // crowd each other on screen, closer than a 32nd at SCROLL 1 (45 of the 1440-pixel measure).
    public sealed class NoteLabelRowsTests
    {
        const sbyte A = NoteLabelRows.Above, B = NoteLabelRows.Below;

        static sbyte[] Rows(string body, BranchRoute route = BranchRoute.Normal)
        {
            var chart = TjaParser.Parse("TITLE:Rows\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n" + body + "\n#END");
            return NoteLabelRows.Compute(new PlaySession(chart, 0, route), new ScrollClockTimeline(chart));
        }

        [Test] public void SixteenthsAtScrollOneStayAbove()
            => Assert.That(Rows("1111111111111111,"), Is.All.EqualTo(A));

        [Test] public void AnExactThirtySecondAtScrollOneIsNotCrowded()
            => Assert.That(Rows(string.Concat(Enumerable.Repeat("1", 32)) + ","), Is.All.EqualTo(A));

        [Test] public void CrowdedNotesAlternateAndALoneNoteStaysAbove()
        {
            // 16ths at SCROLL 0.3 are 27 px apart; the fifth comes five sixteenths (135 px) after the fourth.
            Assert.That(Rows("#SCROLL 0.3\n1111000010000000,"), Is.EqualTo(new[] { A, B, A, B, A }));
        }

        [Test] public void AVerticalScrollKeepsNotesApart()
        {
            // 27 px across but 90 px up the lane: they do not crowd.
            Assert.That(Rows("#SCROLL 0.3+1i\n1111111111111111,"), Is.All.EqualTo(A));
        }

        [Test] public void OnlyThePracticeRouteIsLabelled()
        {
            var chart = TjaParser.Parse("TITLE:Rows\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n#BRANCHSTART p,0,0\n#N\n#SCROLL 0.3\n1111,\n#E\n1,\n#M\n1,\n#BRANCHEND\n#END");
            var session = new PlaySession(chart, 0, BranchRoute.Expert);
            var labelled = chart.Notes.Where(note => NoteLabelRows.Labelled(session, note)).ToArray();
            Assert.That(labelled.Select(note => note.Route), Is.EqualTo(new[] { BranchRoute.Expert }));
            Assert.That(NoteLabelRows.Compute(session, new ScrollClockTimeline(chart)), Is.All.EqualTo(A),
                "The crowded Normal route has no labels to alternate.");
        }
    }
}
