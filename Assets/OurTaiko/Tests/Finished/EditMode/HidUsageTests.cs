using NUnit.Framework;
using UnityEngine.InputSystem;

namespace OurTaiko.Tests
{
    // GCKeyCode values are USB HID keyboard usages; GameControllerKeyboard names them as Input System keys.
    public sealed class HidUsageTests
    {
        [TestCase(0x04, Key.A)]
        [TestCase(0x07, Key.D)]
        [TestCase(0x09, Key.F)]
        [TestCase(0x0D, Key.J)]
        [TestCase(0x0E, Key.K)]
        [TestCase(0x1D, Key.Z)]
        [TestCase(0x1E, Key.Digit1)]
        [TestCase(0x27, Key.Digit0)]
        [TestCase(0x28, Key.Enter)]
        [TestCase(0x29, Key.Escape)]
        [TestCase(0x2C, Key.Space)]
        [TestCase(0x3A, Key.F1)]
        [TestCase(0x45, Key.F12)]
        [TestCase(0x4F, Key.RightArrow)]
        [TestCase(0x52, Key.UpArrow)]
        [TestCase(0x58, Key.NumpadEnter)]
        [TestCase(0x59, Key.Numpad1)]
        [TestCase(0x61, Key.Numpad9)]
        [TestCase(0x62, Key.Numpad0)]
        [TestCase(0x68, Key.F13)]
        [TestCase(0x73, Key.F24)]
        [TestCase(0xE1, Key.LeftShift)]
        [TestCase(0xE7, Key.RightMeta)]
        public void UsagesNameTheirKeys(int usage, Key key)
        {
            Assert.That(HidUsage.TryGetKey(usage, out var found), Is.True);
            Assert.That(found, Is.EqualTo(key));
        }

        [Test]
        public void UnknownUsagesNameNoKey()
        {
            Assert.That(HidUsage.TryGetKey(0x00, out _), Is.False);
            Assert.That(HidUsage.TryGetKey(0x200, out _), Is.False);
        }
    }
}
