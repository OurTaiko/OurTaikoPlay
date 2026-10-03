using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SongClockTests
    {
        [Test]
        public void CountdownAndPauseKeepOnePublishedSongTime()
        {
            var clock = new SongClock();
            Assert.That(clock.Started, Is.False);
            Assert.That(clock.Time, Is.EqualTo(-2));
            clock.Start(100, 3);
            clock.Update(99.999); // The frame began before Start.
            Assert.That(clock.Time, Is.EqualTo(-3));
            Assert.That(clock.Schedule(100, 20).Value, Is.EqualTo((103d, 0d)));
            clock.Update(104);
            Assert.That(clock.Time, Is.EqualTo(1));
            clock.Pause();
            clock.Update(110, 8);
            Assert.That(clock.Time, Is.EqualTo(1));
            Assert.That(clock.Schedule(110, 20), Is.Null);
            clock.Resume(120);
            clock.Update(119.999);
            Assert.That(clock.Time, Is.EqualTo(1));
            var schedule = clock.Schedule(120, 20).Value;
            Assert.That(schedule.At, Is.EqualTo(120.05).Within(1e-9));
            Assert.That(schedule.Position, Is.EqualTo(1.05).Within(1e-9));
            clock.Update(121);
            Assert.That(clock.Time, Is.EqualTo(2));
        }

        [Test]
        public void ResumeDuringCountdownPreservesRemainingLead()
        {
            var clock = new SongClock();
            clock.Start(10, 2);
            clock.Update(11);
            clock.Pause();
            clock.Resume(20);
            Assert.That(clock.Time, Is.EqualTo(-1));
            Assert.That(clock.Schedule(20, 30).Value, Is.EqualTo((21d, 0d)));
        }

        [Test]
        public void PlaybackCorrectionOnlyAppliesInsideStartupWindow()
        {
            var clock = new SongClock();
            clock.Start(10, 2);
            clock.Update(11, 0.1);
            Assert.That(clock.Time, Is.EqualTo(-1));
            clock.Update(13, 0.9);
            Assert.That(clock.Time, Is.EqualTo(0.92).Within(1e-9));
            clock.Update(15, 2);
            Assert.That(clock.Time, Is.EqualTo(2.92).Within(1e-9));
            clock.Pause();
            clock.Resume(20);
            clock.Update(20.1, 3);
            Assert.That(clock.Time, Is.EqualTo(3.004).Within(1e-9));
        }

        [Test]
        public void EmptyAndFinishedAudioAreNotScheduled()
        {
            var clock = new SongClock();
            Assert.That(clock.Schedule(0, 10), Is.Null);
            clock.Start(10, 2);
            Assert.That(clock.Schedule(10, 0), Is.Null);
            Assert.That(clock.Schedule(21.99, 10), Is.Null);
            Assert.That(clock.Schedule(23, 10), Is.Null);
        }
    }
}
