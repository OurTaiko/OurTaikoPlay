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

        [Test]
        public void TimerTicksOnlyWhenUpdatedAndConfirmsAtZero()
        {
            var timer = new ArcadeTimer(60, 0);
            // Frozen on the credit screen: the first update after a long wait ticks once.
            Assert.That(timer.Update(5000), Is.EqualTo(ArcadeTimer.Cue.None));
            Assert.That(timer.Seconds, Is.EqualTo(59));
            Assert.That(timer.Update(5999), Is.EqualTo(ArcadeTimer.Cue.None));
            double now = 5000;
            ArcadeTimer.Cue cues = ArcadeTimer.Cue.None;
            while (timer.Seconds > 30) { now += 1000; cues = timer.Update(now); }
            Assert.That(cues, Is.EqualTo(ArcadeTimer.Cue.Voice30));
            while (timer.Seconds > 10) { now += 1000; cues = timer.Update(now); }
            Assert.That(cues, Is.EqualTo(ArcadeTimer.Cue.Voice10), "10 is not yet in the red zone.");
            now += 1000;
            Assert.That(timer.Update(now), Is.EqualTo(ArcadeTimer.Cue.Blip | ArcadeTimer.Cue.Pop));
            Assert.That(timer.IsRed && timer.PoppedAtMs == now, Is.True);
            while (timer.Seconds > 5) { now += 1000; cues = timer.Update(now); }
            Assert.That(cues, Is.EqualTo(ArcadeTimer.Cue.Blip | ArcadeTimer.Cue.Pop | ArcadeTimer.Cue.Voice5));
            while (timer.Seconds > 0) { now += 1000; timer.Update(now); }
            Assert.That(timer.IsFinished, Is.False, "Reaching 0 confirms on the next update.");
            Assert.That(timer.Update(now + 1), Is.EqualTo(ArcadeTimer.Cue.Finished));
            Assert.That(timer.Update(now + 5000), Is.EqualTo(ArcadeTimer.Cue.None));
        }

        [Test]
        public void TimerPopCurvesFollowGlobalAnimations()
        {
            Assert.That(ArcadeTimer.DigitScale(double.NegativeInfinity), Is.EqualTo(1));
            Assert.That(ArcadeTimer.DigitScale(0), Is.EqualTo(1));
            Assert.That(ArcadeTimer.DigitScale(166.5), Is.EqualTo(1.375f).Within(1e-4), "Quadratic ease-out to 1.5.");
            Assert.That(ArcadeTimer.DigitScale(333), Is.EqualTo(1.5f).Within(1e-4));
            Assert.That(ArcadeTimer.DigitScale(499.5), Is.EqualTo(1.125f).Within(1e-4), "And eased back.");
            Assert.That(ArcadeTimer.DigitScale(666), Is.EqualTo(1));
            Assert.That(ArcadeTimer.HighlightScale(100), Is.EqualTo(1.25f).Within(1e-6));
            Assert.That(ArcadeTimer.HighlightAlpha(100), Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(ArcadeTimer.HighlightAlpha(250), Is.Zero);
        }
    }
}
