using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class GaugeHitEffectTimingTests
    {
        [Test]
        public void FollowsNijiiroAnimationTable()
        {
            // 2: frames 0 / 1 / 2 at 33.33 and 66.66 ms.
            Assert.That(GaugeHitEffectTiming.Frame(0), Is.Zero);
            Assert.That(GaugeHitEffectTiming.Frame(0.0333), Is.Zero);
            Assert.That(GaugeHitEffectTiming.Frame(0.05), Is.EqualTo(1));
            Assert.That(GaugeHitEffectTiming.Frame(0.07), Is.EqualTo(2));
            Assert.That(GaugeHitEffectTiming.Frame(0.3), Is.EqualTo(2));
            // 32: 0.8 until 116.67 ms, then linear to 1.5 over 266 ms.
            Assert.That(GaugeHitEffectTiming.Scale(0.1), Is.EqualTo(0.8));
            Assert.That(GaugeHitEffectTiming.Scale(0.11667 + 0.133), Is.EqualTo(1.15).Within(1e-9));
            Assert.That(GaugeHitEffectTiming.Scale(0.5), Is.EqualTo(1.5));
            // 33: opaque until 300 ms, gone at 383 ms.
            Assert.That(GaugeHitEffectTiming.Opacity(0.3), Is.EqualTo(1));
            Assert.That(GaugeHitEffectTiming.Opacity(0.3415), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(GaugeHitEffectTiming.IsFinished(0.3829), Is.False);
            Assert.That(GaugeHitEffectTiming.IsFinished(0.383), Is.True);
        }

        [Test]
        public void TintFollowsBurstSize()
        {
            Assert.That(GaugeHitEffectTiming.Tint(0.8), Is.EqualTo(((byte)253, (byte)249, (byte)0)));
            Assert.That(GaugeHitEffectTiming.Tint(0.85), Is.EqualTo(((byte)255, (byte)161, (byte)0)));
            Assert.That(GaugeHitEffectTiming.Tint(0.9), Is.EqualTo(((byte)255, (byte)161, (byte)0)));
            Assert.That(GaugeHitEffectTiming.Tint(0.91), Is.EqualTo(((byte)230, (byte)41, (byte)55)));
            // Orange lasts from the start of the resize until it reaches 0.9, about 38 ms.
            Assert.That(GaugeHitEffectTiming.Tint(GaugeHitEffectTiming.Scale(0.12)).Item2, Is.EqualTo(161));
            Assert.That(GaugeHitEffectTiming.Tint(GaugeHitEffectTiming.Scale(0.16)).Item2, Is.EqualTo(41));
        }
    }
}
