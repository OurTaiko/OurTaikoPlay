using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace OurTaiko.Tests
{
    public sealed class SettingsNavigatorTests
    {
        static SettingsNavigator Navigator(GameSettings settings = null, SoundPlatform platform = SoundPlatform.Desktop)
            => new SettingsNavigator(SettingsCatalog.Pages(platform), settings ?? new GameSettings());

        static SettingsNavigator OnPage(string id, GameSettings settings = null, SoundPlatform platform = SoundPlatform.Desktop)
        {
            var nav = Navigator(settings, platform);
            nav.Move(nav.Pages.ToList().FindIndex(p => p.Id == id));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Entered));
            return nav;
        }

        static void Focus(SettingsNavigator nav, string rowId)
        {
            int index = nav.Page.Rows.ToList().FindIndex(r => r.Id == rowId);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), rowId);
            nav.Move(index - nav.RowIndex);
        }

        [Test]
        public void TheRailListsTheCategoriesThenCloseAndStopsAtBothEnds()
        {
            var nav = Navigator();
            Assert.That(nav.Pages.Select(p => p.Title), Is.EqualTo(new[] { "General", "Controls", "Play", "Display", "Sound", "Accounts" }));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
            Assert.That(nav.Move(-1), Is.EqualTo(SettingsNavigator.Result.Bumped), "No wrapping past the first category.");
            Assert.That(nav.Move(6), Is.EqualTo(SettingsNavigator.Result.Moved));
            Assert.That(nav.OnClose, Is.True);
            Assert.That(nav.Move(1), Is.EqualTo(SettingsNavigator.Result.Bumped));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Close));
        }

        [Test]
        public void RowsStopAtTheEndsAndBackReturnsToTheRail()
        {
            var nav = OnPage("play");
            Assert.That(nav.Row.Id, Is.EqualTo("audio-offset"));
            Assert.That(nav.Move(-1, repeat: true), Is.EqualTo(SettingsNavigator.Result.Bumped), "A held key's repeats stop at the first row.");
            Assert.That(nav.Move(10), Is.EqualTo(SettingsNavigator.Result.Moved));
            Assert.That(nav.Row.Id, Is.EqualTo("drum-pad"), "The last row, not a Return entry.");
            Assert.That(nav.Move(1, repeat: true), Is.EqualTo(SettingsNavigator.Result.Bumped));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
            Assert.That(nav.Back(), Is.EqualTo(SettingsNavigator.Result.Returned));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
            Assert.That(nav.Page.Id, Is.EqualTo("play"), "The page stays selected.");
            Assert.That(nav.Back(), Is.EqualTo(SettingsNavigator.Result.Close));
        }

        [Test]
        public void AFreshPressPastEitherEndReturnsToTheRail()
        {
            var nav = OnPage("play");
            Assert.That(nav.Move(-1), Is.EqualTo(SettingsNavigator.Result.Returned), "Ka above the first row leaves the page.");
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
            Assert.That(nav.Page.Id, Is.EqualTo("play"), "The page stays selected.");
            nav.Confirm();
            nav.Move(3);
            Assert.That(nav.Move(1), Is.EqualTo(SettingsNavigator.Result.Returned), "Ka below the last row too.");
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));

            // Adjusting and the sheet still stop at their ends.
            nav.Confirm();
            nav.Confirm();
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Adjust));
            Assert.That(nav.Move(-1000), Is.EqualTo(SettingsNavigator.Result.Moved));
            nav.Back();
            var general = OnPage("general");
            general.Confirm();
            Assert.That(general.Move(-1), Is.EqualTo(SettingsNavigator.Result.Bumped));
            Assert.That(general.Zone, Is.EqualTo(SettingsZone.Sheet));
        }

        [Test]
        public void DonFlipsAToggleInPlace()
        {
            var nav = OnPage("play");
            Focus(nav, "drum-pad");
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.play.singlePlayerDrumPad, Is.False);
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.play.singlePlayerDrumPad, Is.True);
        }

        [Test]
        public void AdjustingANumberAppliesOnDonAndBackRestoresIt()
        {
            var nav = OnPage("play");
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Entered));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Adjust));
            nav.Move(-12);
            Assert.That(nav.AdjustValue, Is.EqualTo(-12));
            Assert.That(nav.Settings.play.audioOffsetMs, Is.Zero, "Moving does not apply.");
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.play.audioOffsetMs, Is.EqualTo(-12));

            nav.Confirm();
            nav.Move(5);
            Assert.That(nav.Back(), Is.EqualTo(SettingsNavigator.Result.Returned));
            Assert.That(nav.Settings.play.audioOffsetMs, Is.EqualTo(-12));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
        }

        [Test]
        public void NumbersStopAtTheirLimits()
        {
            var nav = OnPage("play");
            Focus(nav, "input-delay");
            nav.Confirm();
            Assert.That(nav.Move(-1), Is.EqualTo(SettingsNavigator.Result.Bumped), "0 (Off) is the minimum.");
            nav.Move(PlaySettings.InputDelayMaxMs + 5);
            Assert.That(nav.AdjustValue, Is.EqualTo(PlaySettings.InputDelayMaxMs));
            Assert.That(nav.Row.Format(0), Is.EqualTo("Off"));
        }

        [Test]
        public void ArrowsAdjustDirectlyAndLeaveOtherRowsForTheRail()
        {
            var nav = OnPage("display");
            Assert.That(nav.Row.Id, Is.EqualTo("frame-rate"));
            Assert.That(nav.Side(1), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.display.targetFrameRate, Is.EqualTo(60));
            nav.Side(1);
            Assert.That(nav.Side(1), Is.EqualTo(SettingsNavigator.Result.Bumped), "Choices stop at Unlimited.");
            Assert.That(nav.Settings.display.targetFrameRate, Is.EqualTo(DisplaySettings.Unlimited));

            Focus(nav, "vsync");
            Assert.That(nav.Side(1), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.display.vSync, Is.True);
            Assert.That(nav.Side(1), Is.EqualTo(SettingsNavigator.Result.Bumped));

            var general = OnPage("general");
            Assert.That(general.Side(-1), Is.EqualTo(SettingsNavigator.Result.Returned));
            Assert.That(general.Zone, Is.EqualTo(SettingsZone.Rail));
        }

        [Test]
        public void AListOpensTheSheetOnItsValue()
        {
            var nav = OnPage("general");
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.List));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Entered));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Sheet));
            Assert.That(nav.SheetIndex, Is.Zero);
            Assert.That(nav.SheetOptions, Is.EqualTo(GeneralSettings.LanguageNames));
            nav.Move(2);
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.general.language, Is.EqualTo("zh"));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));

            nav.Confirm();
            Assert.That(nav.SheetIndex, Is.EqualTo(2), "The sheet opens on the current language.");
            Assert.That(nav.Back(), Is.EqualTo(SettingsNavigator.Result.Returned));
            Assert.That(nav.Settings.general.language, Is.EqualTo("zh"));
        }

        [Test]
        public void KeyRowsAddRemoveResetAndClear()
        {
            var nav = OnPage("controls");
            Assert.That(nav.Row.Key, Is.EqualTo(InputKey.LeftDon));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("F"));
            nav.Confirm();
            Assert.That(nav.SheetOptions, Is.EqualTo(new[] { "Add a Key", "Remove F", "Reset to Default", "Clear All" }));

            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Entered));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Capture));
            Assert.That(nav.CaptureKey(Key.G), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.G }));

            nav.Confirm();
            nav.Move(1);
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Changed), "Remove F.");
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.G }));

            nav.Confirm();
            nav.Move(3);
            nav.Confirm();
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.Empty, "Clear All.");
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("Unbound"));

            nav.Confirm();
            nav.Move(1);
            nav.Confirm();
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }), "Reset to Default.");

            nav.Confirm(); nav.Confirm();
            Assert.That(nav.Back(), Is.EqualTo(SettingsNavigator.Result.Returned));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Sheet), "Back from capturing returns to the sheet.");
        }

        [Test]
        public void ChangingTheBackendRebuildsTheOutputRowsAndKeepsTheFocus()
        {
            var nav = OnPage("sound", platform: SoundPlatform.Windows);
            Assert.That(nav.Page.Rows.Any(r => r.Id == "wasapi-exclusive"), Is.True, "Automatic is WASAPI on Windows.");
            Focus(nav, "backend");
            Assert.That(nav.Row.AppliesOnClose, Is.True);
            nav.Confirm();
            nav.Move(2);
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.audio.backend, Is.EqualTo(AudioBackend.BassASIO));
            Assert.That(nav.Page.Rows.Any(r => r.Id == "wasapi-exclusive"), Is.False);
            Assert.That(nav.Page.Rows.Any(r => r.Id == "asio-device"), Is.True);
            Assert.That(nav.Row.Id, Is.EqualTo("backend"));
        }

        [Test]
        public void VolumesAreSlidersInStepsOfFivePercent()
        {
            var nav = OnPage("sound");
            Assert.That(nav.Row.Id, Is.EqualTo("master-volume"));
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.Slider));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("100%"));
            nav.Side(-1);
            Assert.That(nav.Settings.audio.volume.master, Is.EqualTo(0.95f).Within(1e-5));
            Assert.That(nav.SetRow(0, 0), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.audio.volume.master, Is.Zero);
            Assert.That(nav.Row.Hint, Is.EqualTo("Default 100%  ·  Step 5%"));
        }

        [Test]
        public void TextIsKeptButAPasswordGoesToItsAction()
        {
            var nav = OnPage("accounts");
            Assert.That(nav.Row.Id, Is.EqualTo("username-0"));
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Entered));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Typing));
            Assert.That(nav.Move(1), Is.EqualTo(SettingsNavigator.Result.None), "Typing takes the keys.");
            Assert.That(nav.SubmitText("  don  "), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.accounts.p1Username, Is.EqualTo("don"));

            nav.Move(1);
            nav.Confirm();
            Assert.That(nav.SubmitText("secret"), Is.EqualTo(SettingsNavigator.Result.Action));
            Assert.That(nav.PendingAction, Is.EqualTo(SettingsCatalog.SignIn + 0));
            Assert.That(nav.PendingText, Is.EqualTo("secret"));
            Assert.That(nav.Settings.ToJson(), Does.Not.Contain("secret"));

            nav.Move(1);
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Action));
            Assert.That(nav.PendingAction, Is.EqualTo(SettingsCatalog.SignOut + 0));
        }

        [Test]
        public void TapsSelectThenConfirm()
        {
            var nav = Navigator();
            Assert.That(nav.TapPage(2), Is.EqualTo(SettingsNavigator.Result.Moved));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
            Assert.That(nav.TapPage(2), Is.EqualTo(SettingsNavigator.Result.Entered), "Tapping the selected category opens it.");
            Assert.That(nav.TapRow(3), Is.EqualTo(SettingsNavigator.Result.Moved));
            Assert.That(nav.Row.Id, Is.EqualTo("drum-pad"));
            Assert.That(nav.TapRow(3), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.SetRow(0, 7), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(nav.Settings.play.audioOffsetMs, Is.EqualTo(7));
            Assert.That(nav.TapPage(nav.Pages.Count), Is.EqualTo(SettingsNavigator.Result.Moved));
            Assert.That(nav.TapPage(nav.Pages.Count), Is.EqualTo(SettingsNavigator.Result.Close));
        }

        [Test]
        public void TheNavigatorWorksOnACopy()
        {
            var settings = new GameSettings();
            var nav = OnPage("play", settings);
            nav.Side(1);
            Assert.That(nav.Settings.play.audioOffsetMs, Is.EqualTo(1));
            Assert.That(settings.play.audioOffsetMs, Is.Zero);
        }
    }
}
