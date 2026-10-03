using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class GameTimelineTests
    {
        [UnityTest]
        public IEnumerator FrameSnapshotStaysFixedUntilTheNextFrame()
        {
            yield return null;
            double frame = GameTimeline.FrameTime, audio = GameTimeline.AudioFrameTime;
            double live = GameTimeline.Realtime;
            yield return new WaitForEndOfFrame();
            Assert.That(GameTimeline.FrameTime, Is.EqualTo(frame));
            Assert.That(GameTimeline.AudioFrameTime, Is.EqualTo(audio));
            Assert.That(GameTimeline.Realtime, Is.GreaterThan(live));
            yield return null;
            Assert.That(GameTimeline.FrameTime, Is.GreaterThan(frame));
            Assert.That(GameTimeline.AudioFrameTime, Is.GreaterThanOrEqualTo(audio));
        }
    }
}
