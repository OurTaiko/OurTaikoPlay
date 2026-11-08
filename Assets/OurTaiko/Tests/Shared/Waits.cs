using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The flow tests' wait for a scene transition or a state: frames pass until the condition holds,
    // and the test fails with the message once the real-time deadline is over.
    public static class Waits
    {
        public const float DefaultSeconds = 25;

        public static IEnumerator Until(Func<bool> condition, string message = "Timed out waiting.", float seconds = DefaultSeconds)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), message);
                yield return null;
            }
        }
    }
}
