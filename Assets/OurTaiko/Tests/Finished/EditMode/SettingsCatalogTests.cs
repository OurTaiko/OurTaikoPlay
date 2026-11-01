using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SettingsCatalogTests
    {
        static SettingsNavigator OnPage(string id, GameSettings settings = null)
        {
            var nav = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Desktop), settings ?? new GameSettings());
            nav.Move(nav.Pages.ToList().FindIndex(p => p.Id == id));
            nav.Confirm();
            return nav;
        }

        [Test]
        public void SettingsJsonKeepsDefaultsForMissingFields()
        {
            Assert.That(new GameSettings().play.singlePlayerDrumPad, Is.True);
            Assert.That(GameSettings.FromJson("{}").play.singlePlayerDrumPad, Is.True);
            Assert.That(GameSettings.FromJson("").play.singlePlayerDrumPad, Is.True);
            var off = new GameSettings { play = new PlaySettings { singlePlayerDrumPad = false } };
            Assert.That(GameSettings.FromJson(off.ToJson()).play.singlePlayerDrumPad, Is.False);
            Assert.That(GameSettings.FromJson("{\"play\":{}}").display.targetFrameRate, Is.EqualTo(120));
            Assert.That(GameSettings.FromJson("{\"display\":{\"targetFrameRate\":60}}").display.vSync, Is.False);
        }

        [Test]
        public void FrameRateOffers120By60AndUnlimited()
        {
            var nav = OnPage("display");
            Assert.That(nav.Row.Label, Is.EqualTo("Frame Rate"));
            Assert.That(nav.Row.Options, Is.EqualTo(new[] { "120", "60", "Unlimited" }));
            Assert.That(nav.Row.Get(nav.Settings), Is.Zero, "120 is the default.");
            nav.Confirm(); nav.Move(1); nav.Confirm();
            Assert.That(nav.Settings.display.targetFrameRate, Is.EqualTo(60));
            nav.Confirm(); nav.Move(1); nav.Confirm();
            Assert.That(nav.Settings.display.targetFrameRate, Is.EqualTo(DisplaySettings.Unlimited));
            Assert.That(nav.Settings.display.TargetFrameRate, Is.EqualTo(-1));
            Assert.That(GameSettings.FromJson(nav.Settings.ToJson()).display.targetFrameRate, Is.EqualTo(-1));

            // An unknown value shows and applies as the default.
            var odd = new GameSettings { display = new DisplaySettings { targetFrameRate = 75 } };
            Assert.That(odd.display.TargetFrameRate, Is.EqualTo(120));
            nav = OnPage("display", odd);
            Assert.That(nav.Row.Get(nav.Settings), Is.Zero);
        }

        [Test]
        public void VSyncIsADisplayToggleOffByDefault()
        {
            var nav = OnPage("display");
            nav.Move(1);
            Assert.That(nav.Row.Label, Is.EqualTo("VSync"));
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.Toggle));
            Assert.That(nav.Page.Rows.Select(r => r.Label), Is.EqualTo(new[] { "Frame Rate", "VSync", "Background Movie", "Lane Opacity" }));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("Off"), "Off is the default.");
            nav.Confirm();
            Assert.That(nav.Settings.display.vSync, Is.True);
            Assert.That(GameSettings.FromJson(nav.Settings.ToJson()).display.vSync, Is.True);
        }

        [Test]
        public void BackgroundMovieIsOnAndLanesAreFortyPercentByDefault()
        {
            var nav = OnPage("display");
            nav.Move(2);
            Assert.That(nav.Row.Label, Is.EqualTo("Background Movie"));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("On"), "On is the default.");
            nav.Confirm();
            Assert.That(nav.Settings.display.backgroundMovie, Is.False);
            nav.Move(1);
            Assert.That(nav.Row.Label, Is.EqualTo("Lane Opacity"));
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.Slider));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo("40%"));
            Assert.That(nav.Row.Move(100, 1), Is.EqualTo(100));
            Assert.That(nav.Row.Move(0, -1), Is.Zero);
            Assert.That(GameSettings.FromJson("{}").display.MovieLaneAlpha, Is.EqualTo(0.4f));
            Assert.That(GameSettings.FromJson(nav.Settings.ToJson()).display.backgroundMovie, Is.False);
        }

        [Test]
        public void EntryModeListClampsAtBothEnds()
        {
            var flow = new EntryFlow(0, 2);
            Assert.That(flow.MoveMode(1, 10), Is.False, "No moves before the list is up.");
            flow.Join(EntryFlow.SideInputLockMs);
            double ready = EntryFlow.SideInputLockMs + EntryFlow.CloudGateMs + 1;
            Assert.That(flow.MoveMode(-1, ready), Is.False);
            Assert.That(flow.MoveMode(1, ready), Is.True);
            Assert.That(flow.SelectedMode, Is.EqualTo(1));
            Assert.That(flow.MoveMode(1, ready), Is.False);
            flow.Select(ready);
            Assert.That(flow.MoveMode(-1, ready), Is.False, "No moves after the decide.");
        }
    }
}
