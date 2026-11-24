using NUnit.Framework;

namespace OurTaiko.Tests
{
    // The judge counter's 良率: (良 + 可 / 2) / (良 + 可 + 不可) in tenths of a percent, cut, not rounded.
    public sealed class JudgeCounterRateTests
    {
        [TestCase(0, 0, 0, 1000, "100.0", TestName = "Nothing judged yet reads 100.0")]
        [TestCase(10, 1, 1, 875, "87.5", TestName = "10 良 1 可 1 不可 reads 87.5")]
        [TestCase(0, 3, 0, 500, "50.0", TestName = "All 可 reads 50.0")]
        [TestCase(0, 1, 0, 500, "50.0", TestName = "A single 可 keeps its half credit")]
        [TestCase(0, 1, 2, 166, "16.6", TestName = "Half of three cuts 16.66 to 16.6")]
        [TestCase(1, 1, 1, 500, "50.0", TestName = "One of each reads 50.0")]
        [TestCase(3, 0, 0, 1000, "100.0", TestName = "All 良 reads 100.0")]
        [TestCase(1, 0, 2, 333, "33.3", TestName = "One of three")]
        [TestCase(1999, 0, 1, 999, "99.9", TestName = "One 不可 in 2000 is not 100.0")]
        [TestCase(1999, 1, 0, 999, "99.9", TestName = "One 可 in 2000 is not 100.0")]
        [TestCase(0, 0, 5, 0, "0.0", TestName = "All 不可 reads 0.0")]
        [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, 500, "50.0", TestName = "Large counts do not overflow")]
        [TestCase(-1, 1, -1, 500, "50.0", TestName = "Negative counts are clamped before weighting")]
        public void RateWeightsOkAsHalfOfGood(int good, int ok, int bad, int tenths, string text)
        {
            Assert.That(JudgeCounterView.RateTenths(good, ok, bad), Is.EqualTo(tenths));
            Assert.That(JudgeCounterView.RateString(tenths), Is.EqualTo(text));
        }
    }
}
