using NUnit.Framework;

namespace OurTaiko.Tests
{
    // An EditMode run is silent when OURTAIKO_TEST_MUTE is set, as a PlayMode run is (TestData),
    // and draws plates from the baked nameplate set, as a PlayMode run does.
    [SetUpFixture]
    public sealed class QuietTestRun
    {
        [OneTimeSetUp] public void Mute() => TestData.MuteIfAsked();
        [OneTimeSetUp] public void DrawPlates() => TestData.UseBakedNameplates();
    }
}
