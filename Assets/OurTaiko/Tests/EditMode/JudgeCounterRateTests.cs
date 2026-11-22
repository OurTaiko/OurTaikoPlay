using NUnit.Framework;

namespace OurTaiko.Tests
{
    // The judge counter's 良率: (良 + 可) / (良 + 可 + 不可) in tenths of a percent, cut, not rounded.
    public sealed class JudgeCounterRateTests
    {
        [TestCase(0, 0, 0, 1000, "100.0", TestName = "Nothing judged yet reads 100.0")]
        [TestCase(10, 1, 1, 916, "91.6", TestName = "11 of 12 cuts 91.66 to 91.6")]
        [TestCase(0, 3, 0, 1000, "100.0", TestName = "可 counts as a hit")]
        [TestCase(1, 0, 2, 333, "33.3", TestName = "One of three")]
        [TestCase(1999, 0, 1, 999, "99.9", TestName = "One 不可 in 2000 is not 100.0")]
        [TestCase(0, 0, 5, 0, "0.0", TestName = "All 不可 reads 0.0")]
        public void RateIsHitsOverJudgedNotes(int good, int ok, int bad, int tenths, string text)
        {
            Assert.That(JudgeCounterView.RateTenths(good, ok, bad), Is.EqualTo(tenths));
            Assert.That(JudgeCounterView.RateString(tenths), Is.EqualTo(text));
        }
    }
}
