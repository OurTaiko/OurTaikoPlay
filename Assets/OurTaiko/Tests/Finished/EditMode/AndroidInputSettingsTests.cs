using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace OurTaiko.Tests
{
    public sealed class AndroidInputSettingsTests
    {
        [TestCase(false, 0x00000101, 2, false, true)] // USB/Bluetooth keyboard
        [TestCase(false, 0x00000401, 0, false, true)] // Gamepad
        [TestCase(false, 0x01000010, 0, false, true)] // Joystick
        [TestCase(false, 0x00000503, 0, false, true)] // Composite controller
        [TestCase(false, 0x00000101, 1, true, true)] // Sparse keyboard-mode drum
        [TestCase(true, 0x00000101, 2, true, false)] // Virtual/IME keyboard
        [TestCase(true, 0x00000401, 0, false, false)] // Virtual gamepad
        [TestCase(false, 0x00001002, 0, false, false)] // Touchscreen
        [TestCase(false, 0x00002002, 0, false, false)] // Mouse
        [TestCase(false, 0x00000101, 1, false, false)] // Power/volume buttons
        [TestCase(false, 0x00000201, 0, false, false)] // Phone d-pad
        [TestCase(false, 0x00000001, 2, true, false)] // Shared source-class bit alone
        public void OnlyPhysicalKeyboardsAndControllersExposeBindings(bool isVirtual, int sources,
            int keyboardType, bool hasTypingKeys, bool expected)
        {
            Assert.That(PhysicalInputDevices.IsPhysicalControlDevice(isVirtual, sources, keyboardType, hasTypingKeys),
                Is.EqualTo(expected));
        }

        static SettingsNavigator Navigator() => new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Android), new GameSettings());

        static void Open(SettingsNavigator nav, string page, string row = null)
        {
            nav.TapPage(nav.Pages.ToList().FindIndex(p => p.Id == page));
            nav.Confirm();
            if (row != null) nav.OpenRow(nav.Page.Rows.ToList().FindIndex(r => r.Id == row));
        }

        [Test]
        public void ConnectingAndDisconnectingChangesOnlyTheControlsPage()
        {
            var nav = Navigator();
            var original = nav.Pages.ToArray();
            var settings = nav.Settings;
            Assert.That(nav.SetControlsAvailable(false), Is.True);
            Assert.That(nav.Pages.Select(p => p.Id), Is.EqualTo(new[] { "general", "play", "display", "sound", "accounts" }));
            Assert.That(nav.SetControlsAvailable(false), Is.False);
            Assert.That(nav.SetControlsAvailable(true), Is.True);
            Assert.That(nav.Pages, Is.EqualTo(original));
            Assert.That(nav.Settings, Is.SameAs(settings));
        }

        [Test]
        public void DisconnectingDuringCaptureReturnsToRailWithoutLosingCustomBindings()
        {
            var nav = Navigator();
            nav.Settings.general.keyboard.Set(InputKey.LeftDon, Key.X);
            Open(nav, "controls", "key-LeftDon");
            nav.Confirm();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Capture));
            nav.SetControlsAvailable(false);
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
            Assert.That(nav.Page.Id, Is.EqualTo("general"));
            Assert.That(nav.CaptureKey(Key.F), Is.EqualTo(SettingsNavigator.Result.None));
            nav.SetControlsAvailable(true);
            Open(nav, "controls", "key-LeftDon");
            nav.Confirm();
            Assert.That(nav.CaptureKey(Key.J), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.X, Key.J }));
        }

        [Test]
        public void HotplugPreservesTheAccountTextDialogAndPageObjects()
        {
            var nav = Navigator();
            Open(nav, "accounts");
            var row = nav.Page.Rows.First(r => r.Kind == SettingKind.Text);
            nav.OpenRow(nav.Page.Rows.ToList().IndexOf(row));
            foreach (bool connected in new[] { false, true, false, true })
            {
                nav.SetControlsAvailable(connected);
                Assert.That(nav.Page.Id, Is.EqualTo("accounts"));
                Assert.That(nav.Row, Is.SameAs(row));
                Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Typing));
            }
            Assert.That(nav.SubmitText("player"), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Row.GetText(nav.Settings), Is.EqualTo("player"));
        }

        [Test]
        public void HotplugKeepsPendingAdjustmentAndCloseSelected()
        {
            var nav = Navigator();
            Open(nav, "play", "audio-offset");
            nav.Move(1);
            int pending = nav.AdjustValue;
            nav.SetControlsAvailable(false);
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Adjust));
            Assert.That(nav.Row.Id, Is.EqualTo("audio-offset"));
            Assert.That(nav.AdjustValue, Is.EqualTo(pending));
            nav.Confirm();
            Assert.That(nav.Row.Get(nav.Settings), Is.EqualTo(pending));
            nav.Back();
            nav.Move(nav.Pages.Count);
            foreach (bool connected in new[] { true, false, true })
            {
                nav.SetControlsAvailable(connected);
                Assert.That(nav.OnClose, Is.True);
                Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Close));
            }
        }
    }
}
