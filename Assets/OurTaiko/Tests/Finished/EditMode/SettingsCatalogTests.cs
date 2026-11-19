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
            bool drumPad = new PlaySettings().singlePlayerDrumPad;
            Assert.That(new GameSettings().play.singlePlayerDrumPad, Is.EqualTo(drumPad));
            Assert.That(GameSettings.FromJson("{}").play.singlePlayerDrumPad, Is.EqualTo(drumPad));
            Assert.That(GameSettings.FromJson("").play.singlePlayerDrumPad, Is.EqualTo(drumPad));
            Assert.That(GameSettings.FromJson("{\"play\":{\"audioOffsetMs\":3}}").play.singlePlayerDrumPad, Is.EqualTo(drumPad));
            Assert.That(GameSettings.FromJson("{\"play\":null}").play.singlePlayerDrumPad, Is.EqualTo(drumPad));
            Assert.That(GameSettings.FromJson("{\"play\":{}}").display.targetFrameRate, Is.EqualTo(120));
            Assert.That(GameSettings.FromJson("{\"display\":{\"targetFrameRate\":60}}").display.vSync, Is.False);
        }

        [TestCase(SoundPlatform.Android)]
        [TestCase(SoundPlatform.Desktop)]
        public void SidebarCanHideAndRestoreEveryCategoryAcrossSaves(SoundPlatform platform)
        {
            var catalog = SettingsCatalog.Pages(platform);
            var settings = GameSettings.FromJson("{\"general\":{\"language\":\"en\"},\"play\":{\"audioOffsetMs\":17}}");
            var nav = new SettingsNavigator(catalog, settings);
            var categories = new[] { "general", "controls", "play", "display", "sound", "accounts" };
            Assert.That(nav.Pages.Select(p => p.Id), Is.EqualTo(categories.Append("sidebar")));
            nav.Move(categories.Length);
            nav.Confirm();
            Assert.That(nav.Page.Rows.Select(r => r.Id), Is.EqualTo(categories.Select(id => "show-" + id)));

            for (int i = 0; i < categories.Length; i++)
            {
                Assert.That(nav.SetRow(i, 0), Is.EqualTo(SettingsNavigator.Result.Changed));
                Assert.That(nav.Page.Id, Is.EqualTo("sidebar"));
                Assert.That(nav.Row.Id, Is.EqualTo("show-" + categories[i]));
                Assert.That(nav.Pages.Select(p => p.Id), Is.EqualTo(categories.Skip(i + 1).Append("sidebar")));
            }

            nav = new SettingsNavigator(SettingsCatalog.Pages(platform), GameSettings.FromJson(nav.Settings.ToJson()));
            Assert.That(nav.Pages.Select(p => p.Id), Is.EqualTo(new[] { "sidebar" }));
            nav.Move(1);
            Assert.That(nav.Confirm(), Is.EqualTo(SettingsNavigator.Result.Close));
            nav.Move(-1);
            nav.Confirm();
            for (int i = categories.Length - 1; i >= 0; i--)
            {
                Assert.That(nav.SetRow(i, 1), Is.EqualTo(SettingsNavigator.Result.Changed));
                Assert.That(nav.Page.Id, Is.EqualTo("sidebar"));
            }
            Assert.That(nav.Pages.Select(p => p.Id), Is.EqualTo(categories.Append("sidebar")));
            Assert.That(nav.Settings.play.audioOffsetMs, Is.EqualTo(17));
        }

        [Test]
        public void DrumPadStartsOffInTheEditorWhateverTheBuildTarget()
        {
            // Only Android and iOS players default to on; the Editor compiles with the active build
            // target's defines, so a Mac switched to iOS must not flip the default.
            Assert.That(new PlaySettings().singlePlayerDrumPad, Is.False);
        }

        [TestCase(true), TestCase(false)]
        public void ExplicitDrumPadChoiceSurvivesLoadingAndSaving(bool enabled)
        {
            var settings = GameSettings.FromJson("{\"play\":{\"singlePlayerDrumPad\":"
                + (enabled ? "true" : "false") + "}}");
            Assert.That(settings.play.singlePlayerDrumPad, Is.EqualTo(enabled));
            Assert.That(GameSettings.FromJson(settings.ToJson()).play.singlePlayerDrumPad, Is.EqualTo(enabled));
        }

        [Test]
        public void FrameRateOffers120By60AndUnlimited()
        {
            var nav = OnPage("display");
            Assert.That(nav.Row.Label, Is.EqualTo(L10n.Get("Settings", "display.frameRate")));
            Assert.That(nav.Row.Options, Is.EqualTo(new[] { "120", "60", L10n.Get("Settings", "display.unlimited") }));
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
        public void AndroidKeepsKeyboardMappingsAndHidesFixedFrameRateControls()
        {
            var pages = SettingsCatalog.Pages(SoundPlatform.Android);
            Assert.That(pages.Select(page => page.Id), Does.Contain("controls"));

            var display = pages.Single(page => page.Id == "display");
            display.Refresh(new GameSettings());
            Assert.That(display.Rows.Select(row => row.Id), Is.EqualTo(new[] { "chara-3d", "movie", "lane-opacity" }));
        }

        [Test]
        public void VSyncIsADisplayToggleOffByDefault()
        {
            var nav = OnPage("display");
            nav.Move(1);
            Assert.That(nav.Row.Label, Is.EqualTo(L10n.Get("Settings", "display.vsync")));
            Assert.That(nav.Row.Kind, Is.EqualTo(SettingKind.Toggle));
            Assert.That(nav.Page.Rows.Select(r => r.Label), Is.EqualTo(new[] { "frameRate", "vsync", "chara3D", "movie", "laneOpacity" }.Select(key => L10n.Get("Settings", "display." + key))));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo(L10n.Get("Settings", "value.off")), "Off is the default.");
            nav.Confirm();
            Assert.That(nav.Settings.display.vSync, Is.True);
            Assert.That(GameSettings.FromJson(nav.Settings.ToJson()).display.vSync, Is.True);
        }

        [Test]
        public void BackgroundMovieIsOnAndLanesAreFortyPercentByDefault()
        {
            var nav = OnPage("display");
            nav.Move(2);
            Assert.That(nav.Row.Label, Is.EqualTo(L10n.Get("Settings", "display.chara3D")));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo(L10n.Get("Settings", "value.on")), "The 3D Don is on by default.");
            nav.Move(1);
            Assert.That(nav.Row.Label, Is.EqualTo(L10n.Get("Settings", "display.movie")));
            Assert.That(nav.Row.Display(nav.Settings), Is.EqualTo(L10n.Get("Settings", "value.on")), "On is the default.");
            nav.Confirm();
            Assert.That(nav.Settings.display.backgroundMovie, Is.False);
            nav.Move(1);
            Assert.That(nav.Row.Label, Is.EqualTo(L10n.Get("Settings", "display.laneOpacity")));
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
