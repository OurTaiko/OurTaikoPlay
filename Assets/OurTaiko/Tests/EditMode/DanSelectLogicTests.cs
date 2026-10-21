using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class DanSelectLogicTests
    {
        [Test]
        public void KaWrapsTheRibbonAndDonOpensOnlyPlayableCourses()
        {
            var flow = new DanSelectFlow(3, i => i != 1);
            Assert.That(flow.Move(-1, 0), Is.True);
            Assert.That(flow.Selected, Is.EqualTo(2), "Left of the first is the last.");
            Assert.That(flow.Move(1, 10), Is.True);
            Assert.That(flow.Move(1, 20), Is.True);
            Assert.That(flow.Selected, Is.EqualTo(1));
            Assert.That(flow.Open(30), Is.False, "A greyed course does not open.");
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Browsing));
            flow.Move(1, 40);
            Assert.That(flow.Open(50), Is.True);
            Assert.That(flow.Cursor, Is.EqualTo(DanSelectFlow.Choice.No), "いいえ is selected first.");
            Assert.That(flow.Move(1, 60), Is.False, "The ribbon is still while the confirmation is up.");
        }

        [Test]
        public void TheConfirmationWaitsHalfASecondThenKaClampsAndDonDecides()
        {
            var flow = new DanSelectFlow(2, null);
            flow.Open(1000);
            Assert.That(flow.MoveChoice(-1, 1400), Is.False, "Input lock.");
            Assert.That(flow.Decide(1400), Is.Null);
            Assert.That(flow.MoveChoice(-1, 1500), Is.True);
            Assert.That(flow.Cursor, Is.EqualTo(DanSelectFlow.Choice.Yes));
            flow.MoveChoice(-1, 1510);
            Assert.That(flow.MoveChoice(-1, 1520), Is.False, "Clamped at オプション.");
            Assert.That(flow.Decide(1530), Is.EqualTo(DanSelectFlow.Choice.Option));
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Options));
            Assert.That(flow.Back(), Is.False, "The option panel closes itself.");
            flow.OptionsClosed();
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Confirm));
            flow.MoveChoice(1, 1600);
            Assert.That(flow.Decide(1610), Is.EqualTo(DanSelectFlow.Choice.Yes));
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Started));
        }

        [Test]
        public void NoAndBackCloseTheConfirmationAndBackLeavesTheRibbon()
        {
            var flow = new DanSelectFlow(1, null);
            flow.Open(0);
            Assert.That(flow.Decide(600), Is.EqualTo(DanSelectFlow.Choice.No));
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Browsing));
            flow.Open(700);
            Assert.That(flow.Point(DanSelectFlow.Choice.Yes, 800), Is.False, "Taps wait for the lock too.");
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Browsing));
            Assert.That(flow.Back(), Is.True);
            Assert.That(flow.State, Is.EqualTo(DanSelectFlow.Phase.Left));
            Assert.That(new DanSelectFlow(0, null).Open(0), Is.False, "Nothing to open without courses.");
            Assert.That(new DanSelectFlow(3, null, 7).Selected, Is.EqualTo(1));
        }

        [Test]
        public void TheCursorBreathesOnTheArcadeTable()
        {
            Assert.That(DanSelectScene.CursorAlpha(0), Is.EqualTo(1).Within(1e-6));
            Assert.That(DanSelectScene.CursorAlpha(6 * 5 / 0.06), Is.EqualTo(0.301f).Within(1e-4), "Frame 30 is the dimmest.");
            Assert.That(DanSelectScene.CursorAlpha(1000), Is.EqualTo(1).Within(1e-4), "One loop per second.");
        }
    }
}
