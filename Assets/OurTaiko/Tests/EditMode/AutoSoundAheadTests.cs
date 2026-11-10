using NUnit.Framework;

namespace OurTaiko.Tests
{
    // PlayerLane starts autoplay's drum sounds ahead of the judgment; for a long note it asks the
    // session how many hits autoplay will have made by then.
    public sealed class AutoSoundAheadTests
    {
        static PlaySession Session(string body) => new PlaySession(TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\nBALLOON:5\n#START\n" + body + "\n#END"));

        [Test] public void LongHitsAheadAreTheHitsAutoplayMakes()
        {
            var session = Session("5000000000008000,\n7000000000008000,\n1000,");
            for (double t = -0.5; t < 6; t += 1 / 120.0)
            {
                int roll = session.AutoLongHitsBy(0, t), balloon = session.AutoLongHitsBy(1, t);
                session.Advance(t, true);
                Assert.That(session.LongHits[0], Is.EqualTo(roll), "roll at " + t);
                Assert.That(session.LongHits[1], Is.EqualTo(balloon), "balloon at " + t);
            }
            Assert.That(session.LongHits[0], Is.GreaterThan(15));
            Assert.That(session.LongHits[1], Is.EqualTo(5));
            Assert.That(session.AutoLongHitsBy(2, 6), Is.Zero);
        }
    }
}
