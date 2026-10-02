using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // tja.cpp modifier_moji: text frames under the notes. BPM 120: a measure is 2 s, an eighth 0.25 s.
    public sealed class NoteMojiTests
    {
        static TaikoChart Parse(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");
        static int[] Moji(TaikoChart chart) => chart.Notes.Select(n => n.Moji).ToArray();

        [TestCase("1111,", new[] { 0, 0, 0, 0 })]                          // quarters are no stream
        [TestCase("11111111,", new[] { 1, 1, 1, 1, 1, 1, 1, 0 })]           // ド…ドン
        [TestCase("11101110,", new[] { 1, 2, 0, 1, 2, 0 })]                 // ドコドン twice
        [TestCase("12120000,", new[] { 1, 4, 1, 3 })]                       // カ only before the last
        [TestCase("33440000,", new[] { 5, 5, 6, 6 })]                       // big notes keep their text
        [TestCase("1111111100000000,", new[] { 1, 1, 1, 1, 1, 1, 1, 0 })]   // sixteenths
        [TestCase("11500008,", new[] { 1, 0, 7 })]                          // a roll head ends the stream
        [TestCase("60000008,\n70000008,\n90000008,", new[] { 8, 9, 11 })]
        public void AssignsOriginalFrames(string body, int[] expected)
        {
            Assert.That(Moji(Parse(body)), Is.EqualTo(expected));
        }

        [Test] public void BarLinesTakePartInStreams()
        {
            // find_streams walks the bar line too: the stream ends on it, so the note
            // before it is ド, and a new stream starts after it (bar → note is 0 ms).
            Assert.That(Moji(Parse("00000011,\n11000000,")), Is.EqualTo(new[] { 1, 1, 1, 0 }));
        }

        [Test] public void BranchRoutesAreSeparateLists()
        {
            var chart = Parse("11111111,\n#BRANCHSTART p,50,80\n#N\n11000000,\n#E\n11000000,\n#M\n11000000,\n#BRANCHEND\n11000000,");
            Assert.That(chart.NoteLists.Count, Is.EqualTo(4));
            // The common list continues across the branch; each route stream is its own.
            foreach (BranchRoute route in new[] { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master })
                Assert.That(chart.Notes.Where(n => n.BranchId == 0 && n.Route == route).Select(n => n.Moji), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(chart.Notes.Where(n => n.BranchId < 0).Select(n => n.Moji),
                Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1, 1, 0, 1, 0 }));
        }

        [Test] public void ModifiersReassignAfterSwappingColours()
        {
            var chart = Parse("11120000,");
            ChartModifiers.Apply(chart, new PlayOptions { inverse = true }, new System.Random(1));
            Assert.That(Moji(chart), Is.EqualTo(new[] { 4, 4, 4, 0 }));
        }
    }
}
