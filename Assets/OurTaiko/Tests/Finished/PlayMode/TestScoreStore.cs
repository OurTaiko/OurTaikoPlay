using NUnit.Framework;

namespace OurTaiko.Tests
{
    // Every PlayMode test in this assembly uses temporary scores, options and player info.
    [SetUpFixture]
    public sealed class TestScoreStore
    {
        [OneTimeSetUp] public void UseTemporaryData() => TestData.Use();
        [OneTimeTearDown] public void RemoveTemporaryData() => TestData.Restore();
    }
}
