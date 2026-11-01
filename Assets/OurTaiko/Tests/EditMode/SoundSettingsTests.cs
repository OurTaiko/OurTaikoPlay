using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SoundSettingsTests
    {
        [Test]
        public void VolumesSurviveJsonAndOldFilesPreserveTheMix()
        {
            var old = GameSettings.FromJson("{\"audio\":{\"backend\":1,\"volume\":null}}");
            Assert.That(old.audio.volume.Gain(AudioGroup.Voice), Is.EqualTo(1));
            var s = new GameSettings();
            s.audio.volume.master = .5f; s.audio.volume.drum = .3f; s.audio.volume.voice = 2;
            var roundtrip = GameSettings.FromJson(s.ToJson());
            Assert.That(roundtrip.audio.volume.Gain(AudioGroup.Drum), Is.EqualTo(.15f).Within(.0001));
            Assert.That(roundtrip.audio.volume.Gain(AudioGroup.Voice), Is.EqualTo(1));
            Assert.That(roundtrip.audio.volume.Gain(AudioGroup.Bgm), Is.EqualTo(.5f));
        }
        static SettingRow[] Rows(SoundPlatform platform, AudioBackend backend = AudioBackend.Automatic)
            => SettingsCatalog.Sound(platform, backend).SelectMany(g => g.Rows).ToArray();

        [TestCase(SoundPlatform.Windows)]
        [TestCase(SoundPlatform.Android)]
        [TestCase(SoundPlatform.IOS)]
        [TestCase(SoundPlatform.Desktop)]
        public void SoundMenuOffersVolumesAndPlatformSupportedBackends(SoundPlatform platform)
        {
            var groups = SettingsCatalog.Sound(platform, AudioBackend.Automatic);
            Assert.That(groups.Select(g => g.Title), Is.EqualTo(new[] { "Volume", "Output" }));
            Assert.That(groups[0].Rows.Select(r => r.Label), Is.EqualTo(new[] { "Master", "BGM", "Track", "Drum", "Effects", "Voice" }));
            Assert.That(groups[0].Rows.All(r => r.Kind == SettingKind.Slider), Is.True);
            var backend = groups[1].Rows[0];
            Assert.That(backend.Label, Is.EqualTo("Output Backend"));
            Assert.That(backend.Kind, Is.EqualTo(SettingKind.List));
            Assert.That(backend.AppliesOnClose, Is.True);
            Assert.That(backend.Options.Contains("BassASIO"), Is.EqualTo(platform == SoundPlatform.Windows));
            Assert.That(backend.Options.Contains("BassWASAPI"), Is.EqualTo(platform == SoundPlatform.Windows));
            Assert.That(backend.Options.Contains("Unity"), Is.False);
            // The cross-platform BassSimple output is always the last choice.
            Assert.That(backend.Options, Is.EqualTo(platform == SoundPlatform.Windows
                ? new[] { "Automatic", "BassWASAPI", "BassASIO", "BassSimple" } : new[] { "Automatic", "BassSimple" }));
            Assert.That(backend.OptionNotes.Count, Is.EqualTo(backend.Options.Count));
            Assert.That(groups[1].Rows.All(r => r.AppliesOnClose), Is.True, "Every output row waits for the overlay to close.");
            Assert.That(groups[0].Rows.Any(r => r.AppliesOnClose), Is.False, "Volumes apply at once.");
        }

        // A Windows settings file with BassWASAPI opened on another platform: the row shows Automatic
        // (the engine falls back to BassSimple there), and the device rows are Automatic's too.
        [Test]
        public void ABackendThePlatformLacksShowsAsAutomaticWithAutomaticRows()
        {
            var settings = new GameSettings { audio = new AudioOptions { backend = AudioBackend.BassWASAPI } };
            var nav = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Desktop), settings);
            nav.Move(nav.Pages.ToList().FindIndex(p => p.Id == "sound")); nav.Confirm();
            var backend = nav.Page.Rows.Single(r => r.Id == "backend");
            Assert.That(backend.Display(nav.Settings), Is.EqualTo("Automatic"));
            Assert.That(nav.Page.Rows.Select(r => r.Id), Has.None.StartsWith("wasapi-"));
            Assert.That(nav.Page.Rows.Select(r => r.Id), Is.EqualTo(new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Desktop), new GameSettings())
                .Pages.Single(p => p.Id == "sound").Rows.Select(r => r.Id)), "The same rows as Automatic.");
            Assert.That(Rows(SoundPlatform.Windows, AudioBackend.BassWASAPI).Select(r => r.Id), Has.Some.StartsWith("wasapi-"), "Windows still offers WASAPI.");
        }

        // Sample Rate explains itself differently under WASAPI; the text must follow the backend.
        [Test]
        public void SwitchingBackendRefreshesDependentDescriptions()
        {
            var nav = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Windows), new GameSettings());
            nav.Move(nav.Pages.ToList().FindIndex(p => p.Id == "sound")); nav.Confirm();
            string Sample() => nav.Page.Rows.Single(r => r.Id == "sample-rate").Description;
            Assert.That(Sample(), Does.StartWith("BASS initialization rate"), "Automatic is WASAPI on Windows.");
            nav.Page.Rows.Single(r => r.Id == "sample-rate").Status = "kept";
            var backend = nav.Page.Rows.Single(r => r.Id == "backend");
            Assert.That(nav.SetRow(nav.Page.Rows.ToList().IndexOf(backend), backend.Options.ToList().IndexOf("BassSimple")), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(Sample(), Does.StartWith("Requested sample rate"));
            Assert.That(nav.Page.Rows.Single(r => r.Id == "sample-rate").Status, Is.EqualTo("kept"), "Statuses survive the rebuild.");
        }

        [Test]
        public void BackendChangesPreserveAdvancedConfiguration()
        {
            var s = new GameSettings();
            s.audio.deviceBufferMs = 73; s.audio.asioDevice = 2; s.audio.volume.master = .35f;
            Rows(SoundPlatform.Desktop).Single(r => r.Id == "backend").Set(s, 1);
            var saved = GameSettings.FromJson(s.ToJson());
            Assert.That(saved.audio.backend, Is.EqualTo(AudioBackend.BassSimple));
            Assert.That(saved.audio.deviceBufferMs, Is.EqualTo(73));
            Assert.That(saved.audio.asioDevice, Is.EqualTo(2));
            Assert.That(saved.audio.volume.master, Is.EqualTo(.35f));
        }

        [Test]
        public void SwitchingBackendRebuildsRowsOnlyOnConfirmAndPreservesValues()
        {
            var settings = new GameSettings();
            settings.audio.wasapiBufferSeconds = .0123f;
            settings.audio.asioDevice = 3;
            settings.audio.devicePeriodMs = 4;
            var nav = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Windows), settings);
            var other = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Windows), new GameSettings());
            nav.Move(4); nav.Confirm();
            other.Move(4); other.Confirm();
            Assert.That(nav.Page.Id, Is.EqualTo("sound"));
            nav.Move(6);
            Assert.That(nav.Row.Label, Is.EqualTo("Output Backend"));
            nav.Confirm(); nav.Move(2);
            Assert.That(nav.Page.Rows.Any(r => r.Label == "WASAPI Buffer"), Is.True, "Moving in the sheet changes nothing.");
            nav.Back(); nav.Confirm(); nav.Move(2); nav.Confirm();
            Assert.That(nav.Row.Label, Is.EqualTo("Output Backend"));
            Assert.That(nav.Page.Rows.Any(r => r.Label == "WASAPI Buffer"), Is.False);
            Assert.That(nav.Page.Rows.Single(r => r.Label == "ASIO Device Index").Get(nav.Settings), Is.EqualTo(3));
            Assert.That(other.Page.Rows.Any(r => r.Label == "WASAPI Buffer"), Is.True, "Navigators must not share mutable rows");
            nav.Confirm(); nav.Move(1); nav.Confirm();
            Assert.That(nav.Page.Rows.Any(r => r.Label.StartsWith("ASIO")), Is.False);
            Assert.That(nav.Page.Rows.Single(r => r.Label == "Output Period").Get(nav.Settings), Is.EqualTo(4));
            nav.Confirm(); nav.Move(-3); nav.Confirm();
            Assert.That(nav.Page.Rows.Any(r => r.Label == "WASAPI Buffer"), Is.True);
            var saved = GameSettings.FromJson(nav.Settings.ToJson());
            Assert.That(saved.audio.wasapiBufferSeconds, Is.EqualTo(.0123f));
            Assert.That(saved.audio.asioDevice, Is.EqualTo(3));
            Assert.That(saved.audio.devicePeriodMs, Is.EqualTo(4));
        }

        [Test]
        public void BackendNumericControlsUseBoundsAndCorrectUnits()
        {
            var buffer = Rows(SoundPlatform.Windows, AudioBackend.BassWASAPI).Single(r => r.Label == "WASAPI Buffer");
            var settings = new GameSettings();
            Assert.That(buffer.Display(settings), Is.EqualTo("6.0 ms"));
            Assert.That(buffer.Hint, Does.EndWith("Step 0.1 ms"));
            Assert.That(buffer.Move(50, -1), Is.EqualTo(50));
            buffer.Set(settings, 123);
            Assert.That(GameSettings.FromJson(settings.ToJson()).audio.wasapiBufferSeconds, Is.EqualTo(.0123f));
            var asio = Rows(SoundPlatform.Windows, AudioBackend.BassASIO).Single(r => r.Label == "ASIO Buffer");
            Assert.That(asio.Move(0, -1), Is.Zero);
            Assert.That(asio.Display(settings), Is.EqualTo("Driver Default"));
            Assert.That(asio.Hint, Does.EndWith("Step 1 samples"));
            Assert.That(Rows(SoundPlatform.Android).Any(r => r.Label == "Android AAudio"), Is.True);
            Assert.That(Rows(SoundPlatform.IOS).Any(r => r.Label == "Android AAudio"), Is.False);
        }
    }
}
