using NUnit.Framework;

namespace OurTaiko.Tests
{
    // An EditMode run is silent when OURTAIKO_TEST_MUTE is set, as a PlayMode run is (TestData).
    [SetUpFixture]
    public sealed class QuietTestRun
    {
        [OneTimeSetUp] public void Mute() => TestData.MuteIfAsked();
    }
}
