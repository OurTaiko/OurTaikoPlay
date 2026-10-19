using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class WheelMomentumTests
    {
        // A drag of `rows` over 80 ms in four moves, released at the last one.
        static double Flick(WheelMomentum wheel, double rows, double time)
        {
            wheel.Press();
            wheel.BeginDrag(time);
            for (int i = 1; i <= 4; i++) wheel.Drag(rows / 4, time + i * 0.02);
            wheel.EndDrag(time + 0.08);
            return time + 0.08;
        }

        static double Glide(WheelMomentum wheel, double from, double seconds)
        {
            double time = from;
            for (int i = 0; i < seconds * 60; i++) { time += 1 / 60.0; wheel.Tick(time); }
            return time;
        }

        [Test]
        public void AFlickGlidesAndComesToRest()
        {
            var wheel = new WheelMomentum();
            double t = Flick(wheel, 1, 0);
            Assert.That(wheel.Velocity, Is.EqualTo(12.5).Within(0.01), "1 row in 80 ms.");
            Assert.That(wheel.Offset, Is.EqualTo(1).Within(1e-9));
            t = Glide(wheel, t, 0.2);
            Assert.That(wheel.Gliding, Is.True);
            Assert.That(wheel.Offset, Is.GreaterThan(2));
            Glide(wheel, t, 5);
            Assert.That(wheel.Active, Is.False);
            Assert.That(wheel.Offset, Is.InRange(3, 8));
        }

        [Test]
        public void ALetGoWithoutAFlickDoesNotGlide()
        {
            var wheel = new WheelMomentum();
            wheel.Press();
            wheel.BeginDrag(0);
            wheel.Drag(1, 0.1);
            wheel.EndDrag(0.5);
            Assert.That(wheel.Active, Is.False);
            Assert.That(wheel.Offset, Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void AFlickDuringAGlideAddsItsSpeed()
        {
            var wheel = new WheelMomentum();
            double t = Glide(wheel, Flick(wheel, 1, 0), 0.1);
            double gliding = wheel.Velocity;
            Flick(wheel, 1, t);
            Assert.That(wheel.Velocity, Is.EqualTo(gliding + 12.5).Within(0.01));
        }

        [Test]
        public void AReverseFlickDuringAGlideBrakesOrTurnsIt()
        {
            var wheel = new WheelMomentum();
            double t = Glide(wheel, Flick(wheel, 2, 0), 0.05);
            double gliding = wheel.Velocity;
            t = Flick(wheel, -1, t);
            Assert.That(wheel.Velocity, Is.EqualTo(gliding - 12.5).Within(0.01));
            Assert.That(wheel.Velocity, Is.GreaterThan(0), "A weaker reverse flick only slows the glide.");
            t = Glide(wheel, t, 0.05);
            Flick(wheel, -3, t);
            Assert.That(wheel.Velocity, Is.LessThan(0), "A stronger one turns it round.");
        }

        [Test]
        public void ATapDuringAGlideStopsIt()
        {
            var wheel = new WheelMomentum();
            double t = Glide(wheel, Flick(wheel, 1, 0), 0.1);
            double offset = wheel.Offset;
            wheel.Press();
            Assert.That(wheel.Holding && !wheel.Gliding, Is.True);
            t = Glide(wheel, t, 0.5);
            Assert.That(wheel.Offset, Is.EqualTo(offset), "The finger holds the wheel.");
            wheel.Release();
            Glide(wheel, t, 0.5);
            Assert.That(wheel.Active, Is.False);
            Assert.That(wheel.Offset, Is.EqualTo(offset));
        }

        [Test]
        public void ShiftTakesWholeRowsOffTheOffset()
        {
            var wheel = new WheelMomentum();
            Flick(wheel, 2.25, 0);
            wheel.Shift(2);
            Assert.That(wheel.Offset, Is.EqualTo(0.25).Within(1e-9));
            wheel.Stop();
            Assert.That(wheel.Active || wheel.Offset != 0, Is.False);
        }
    }
}
