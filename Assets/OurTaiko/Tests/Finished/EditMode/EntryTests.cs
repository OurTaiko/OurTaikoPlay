using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class EntryTests
    {
        [Test]
        public void FlowJoinsThenWaitsForTheCloudBeforeTheBoard()
        {
            var flow = new EntryFlow(1000);
            Assert.That(flow.Join(1100), Is.False, "Side select locks input for 120 ms.");
            Assert.That(flow.State, Is.EqualTo(EntryFlow.Phase.SelectSide));
            Assert.That(flow.Join(1200), Is.True);
            Assert.That(flow.Join(1300), Is.False, "Only one player joins.");
            Assert.That(flow.NameplateAlpha(1300), Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(flow.IsModeReady(1200 + 1233), Is.False);
            Assert.That(flow.Select(1200 + 1233), Is.False, "No decide before the board is up.");
            Assert.That(flow.IsModeReady(1200 + 1234), Is.True);
            Assert.That(flow.Select(2500), Is.True);
            Assert.That(flow.Select(2510), Is.False);
            Assert.That(flow.BoardFade(2580), Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(flow.IsFinished(2659), Is.False);
            Assert.That(flow.IsFinished(2660), Is.True);
        }
    }
}
