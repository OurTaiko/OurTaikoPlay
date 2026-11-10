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

        [TestCase("pipewire", true)]
        [TestCase("pulseaudio", true)]
        [TestCase("jackd", true)]
        [TestCase("jackdbus", true)]
        [TestCase("raw-client", false)]
        [TestCase("OurTaikoPlay", false)]
        [TestCase(null, false)]
        public void OnlySessionAudioServerOwnersMayBeAskedToYield(string process, bool expected)
            => Assert.That(LinuxAudioDevices.IsSessionAudioServer(process), Is.EqualTo(expected));

        [TestCase(SoundPlatform.Windows)]
        [TestCase(SoundPlatform.Android)]
        [TestCase(SoundPlatform.IOS)]
        [TestCase(SoundPlatform.Desktop)]
        [TestCase(SoundPlatform.Linux)]
        public void SoundMenuOffersVolumesAndPlatformSupportedBackends(SoundPlatform platform)
        {
            var groups = SettingsCatalog.Sound(platform, AudioBackend.Automatic);
            Assert.That(groups.Select(g => g.Title), Is.EqualTo(new[] { "volume", "output", "timing" }.Select(key => L10n.Get("Settings", "sound.group." + key))));
            Assert.That(groups[0].Rows.Select(r => r.Label), Is.EqualTo(new[] { "master", "bgm", "track", "drum", "effects", "voice" }.Select(key => L10n.Get("Settings", "sound." + key))));
            Assert.That(groups[0].Rows.All(r => r.Kind == SettingKind.Slider), Is.True);
            var backend = groups[1].Rows[0];
            Assert.That(backend.Label, Is.EqualTo(L10n.Get("Settings", "sound.backend")));
            Assert.That(backend.Kind, Is.EqualTo(SettingKind.List));
            Assert.That(backend.AppliesOnClose, Is.True);
            Assert.That(backend.Options.Contains("BassASIO"), Is.EqualTo(platform == SoundPlatform.Windows));
            Assert.That(backend.Options.Contains("BassWASAPI"), Is.EqualTo(platform == SoundPlatform.Windows));
            Assert.That(backend.Options.Contains("Unity"), Is.False);
            // The cross-platform BassSimple output is always the last choice. The backends keep their names.
            string automatic = L10n.Get("Settings", "sound.automatic");
            Assert.That(backend.Options, Is.EqualTo(platform == SoundPlatform.Windows
                ? new[] { automatic, "BassWASAPI", "BassASIO", "BassSimple" }
                : platform == SoundPlatform.Linux ? new[] { automatic, "LinuxAlsaExclusive", "BassSimple" } : new[] { automatic, "BassSimple" }));
            Assert.That(backend.OptionNotes.Count, Is.EqualTo(backend.Options.Count));
            Assert.That(groups[1].Rows.All(r => r.AppliesOnClose), Is.True, "Every output row waits for the overlay to close.");
            Assert.That(groups[0].Rows.Any(r => r.AppliesOnClose), Is.False, "Volumes apply at once.");
        }

        [Test]
        public void AlsaUsesTheExistingParametersAndOnlyLinuxExposesItsDevice()
        {
            var linux = Rows(SoundPlatform.Linux, AudioBackend.LinuxAlsaExclusive);
            Assert.That(linux.Count(r => r.Id == "sample-rate"), Is.EqualTo(1));
            Assert.That(linux.Count(r => r.Id == "device-period"), Is.EqualTo(1));
            Assert.That(linux.Count(r => r.Id == "device-buffer"), Is.EqualTo(1));
            Assert.That(linux.Single(r => r.Id == "device-buffer").Description, Is.EqualTo(L10n.Get("Settings", "sound.deviceBufferDesc.other")));
            Assert.That(linux.Single(r => r.Id == "linux-alsa-device").Description, Is.EqualTo(L10n.Get("Settings", "sound.alsa.deviceDesc")));
            Assert.That(linux.Single(r => r.Id == "linux-alsa-device").AppliesOnClose, Is.True);
            foreach (var platform in new[] { SoundPlatform.Windows, SoundPlatform.Desktop, SoundPlatform.Android, SoundPlatform.IOS })
            {
                var rows = Rows(platform, AudioBackend.LinuxAlsaExclusive);
                Assert.That(rows.Single(r => r.Id == "backend").Options, Does.Not.Contain("LinuxAlsaExclusive"));
                Assert.That(rows.Any(r => r.Id == "linux-alsa-device"), Is.False);
            }
            Assert.That(Rows(SoundPlatform.Linux).Any(r => r.Id == "linux-alsa-device"), Is.False);
        }

        [TestCase("en", "microphone", "Heard on LinuxAlsaExclusive")]
        [TestCase("ja", "マイク", "LinuxAlsaExclusive では聴こえる音")]
        [TestCase("zh", "麦克风", "LinuxAlsaExclusive 上为听到")]
        public void AlsaWarningAndPositionClockFollowTheSelectedLanguage(string language, string inputWarning, string clockNote)
        {
            string previous = L10n.Language;
            try
            {
                L10n.SetLanguage(language);
                var rows = Rows(SoundPlatform.Linux, AudioBackend.LinuxAlsaExclusive);
                Assert.That(rows.Single(r => r.Id == "linux-alsa-device").Description, Does.Contain(inputWarning));
                Assert.That(rows.Single(r => r.Id == "backend").OptionNotes[1], Does.Contain(inputWarning));
                Assert.That(rows.Single(r => r.Id == "clock").OptionNotes[0], Is.EqualTo(clockNote));
            }
            finally { L10n.SetLanguage(previous); }
        }

        [TestCase(0, AudioBackend.Automatic)]
        [TestCase(1, AudioBackend.BassSimple)]
        [TestCase(2, AudioBackend.BassWASAPI)]
        [TestCase(3, AudioBackend.BassASIO)]
        [TestCase(4, AudioBackend.Automatic)]
        [TestCase(5, AudioBackend.LinuxAlsaExclusive)]
        [TestCase(99, AudioBackend.Automatic)]
        public void PersistedBackendValuesRemainCompatible(int value, AudioBackend expected)
        {
            Assert.That(GameSettings.FromJson("{\"audio\":{\"backend\":" + value + "}}").audio.backend, Is.EqualTo(expected));
        }

        [Test]
        public void AlsaStableSelectionRoundTripsAndRequiresDeviceReopening()
        {
            var original = new GameSettings();
            Assert.That(original.audio.backend, Is.EqualTo(AudioBackend.Automatic));
            Assert.That(original.audio.linuxAlsaDevice, Is.Empty);
            var settings = original.Clone();
            settings.audio.backend = AudioBackend.LinuxAlsaExclusive;
            settings.audio.linuxAlsaDevice = LinuxAudioDevices.StableId("/sys/devices/example-usb-port", 3);
            settings.audio.sampleRate = 48000; settings.audio.devicePeriodMs = 7; settings.audio.deviceBufferMs = 28;
            var restored = settings.Clone();
            Assert.That(restored.audio.SameDeviceSettings(settings.audio), Is.True);
            Assert.That(restored.audio.linuxAlsaDevice, Is.EqualTo(settings.audio.linuxAlsaDevice));
            Assert.That(restored.audio.sampleRate, Is.EqualTo(48000));
            Assert.That(restored.audio.devicePeriodMs, Is.EqualTo(7));
            Assert.That(restored.audio.deviceBufferMs, Is.EqualTo(28));
            restored.audio.linuxAlsaDevice += "-different";
            Assert.That(restored.audio.SameDeviceSettings(settings.audio), Is.False);
        }

        [Test]
        public void StableDeviceMatchingDoesNotDependOnEnumerationOrderNamesOrCardNumbers()
        {
            string id = LinuxAudioDevices.StableId("/sys/devices/example-port", 0);
            var selected = new LinuxAudioDevice(id, "Renamed", "hw:7,0", 10, 7, 0);
            var other = new LinuxAudioDevice(LinuxAudioDevices.StableId("/sys/devices/other-port", 0), "Renamed", "hw:2,0", 3, 2, 0);
            Assert.That(LinuxAudioDevices.Resolve(new[] { other, selected }, id), Is.SameAs(selected));
            Assert.That(LinuxAudioDevices.Resolve(new[] { selected, other }, id), Is.SameAs(selected));
            Assert.That(LinuxAudioDevices.Resolve(new[] { other }, id), Is.Null);
            Assert.That(LinuxAudioDevices.Resolve(new[] { selected }, ""), Is.Null);
            Assert.That(LinuxAudioDevices.Resolve(new[] { selected, selected }, id), Is.Null);
            Assert.That(LinuxAudioDevices.StableId("/sys/devices/example-port", 0, "serial-one"),
                Is.Not.EqualTo(LinuxAudioDevices.StableId("/sys/devices/example-port", 0, "serial-two")));
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
            Assert.That(backend.Display(nav.Settings), Is.EqualTo(L10n.Get("Settings", "sound.automatic")));
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
            Assert.That(Sample(), Is.EqualTo(L10n.Get("Settings", "sound.sampleRateDesc.wasapi")), "Automatic is WASAPI on Windows.");
            nav.Page.Rows.Single(r => r.Id == "sample-rate").Status = "kept";
            var backend = nav.Page.Rows.Single(r => r.Id == "backend");
            Assert.That(nav.SetRow(nav.Page.Rows.ToList().IndexOf(backend), backend.Options.ToList().IndexOf("BassSimple")), Is.EqualTo(SettingsNavigator.Result.Changed));
            Assert.That(Sample(), Is.EqualTo(L10n.Get("Settings", "sound.sampleRateDesc.other")));
            Assert.That(nav.Page.Rows.Single(r => r.Id == "sample-rate").Status, Is.EqualTo("kept"), "Statuses survive the rebuild.");
        }

        // The overlay previews a volume through the row's group and shows the output banner on the
        // page that says so; neither is keyed on a row or page id.
        [Test]
        public void VolumeRowsNamePreviewGroupsAndTheSoundPageShowsTheOutput()
        {
            var pages = SettingsCatalog.Pages(SoundPlatform.Desktop);
            Assert.That(pages.Where(p => p.ShowsAudioOutput).Select(p => p.Title), Is.EqualTo(new[] { L10n.Get("Settings", "sound.title") }));
            var rows = Rows(SoundPlatform.Desktop);
            Assert.That(rows.Single(r => r.Label == L10n.Get("Settings", "sound.drum")).Preview, Is.EqualTo(AudioGroup.Drum));
            Assert.That(rows.Single(r => r.Label == L10n.Get("Settings", "sound.voice")).Preview, Is.EqualTo(AudioGroup.Voice));
            Assert.That(rows.Where(r => r.Preview != null).Count(), Is.EqualTo(2), "Only the drum and voice volumes preview.");
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
            Assert.That(nav.Row.Id, Is.EqualTo("backend"));
            nav.Confirm(); nav.Move(2);
            Assert.That(nav.Page.Rows.Any(r => r.Id == "wasapi-buffer"), Is.True, "Moving in the sheet changes nothing.");
            nav.Back(); nav.Confirm(); nav.Move(2); nav.Confirm();
            Assert.That(nav.Row.Id, Is.EqualTo("backend"));
            Assert.That(nav.Page.Rows.Any(r => r.Id == "wasapi-buffer"), Is.False);
            Assert.That(nav.Page.Rows.Single(r => r.Id == "asio-device").Get(nav.Settings), Is.EqualTo(3));
            Assert.That(other.Page.Rows.Any(r => r.Id == "wasapi-buffer"), Is.True, "Navigators must not share mutable rows");
            nav.Confirm(); nav.Move(1); nav.Confirm();
            Assert.That(nav.Page.Rows.Any(r => r.Id.StartsWith("asio-")), Is.False);
            Assert.That(nav.Page.Rows.Single(r => r.Id == "device-period").Get(nav.Settings), Is.EqualTo(4));
            nav.Confirm(); nav.Move(-3); nav.Confirm();
            Assert.That(nav.Page.Rows.Any(r => r.Id == "wasapi-buffer"), Is.True);
            var saved = GameSettings.FromJson(nav.Settings.ToJson());
            Assert.That(saved.audio.wasapiBufferSeconds, Is.EqualTo(.0123f));
            Assert.That(saved.audio.asioDevice, Is.EqualTo(3));
            Assert.That(saved.audio.devicePeriodMs, Is.EqualTo(4));
        }

        [Test]
        public void BackendNumericControlsUseBoundsAndCorrectUnits()
        {
            var buffer = Rows(SoundPlatform.Windows, AudioBackend.BassWASAPI).Single(r => r.Id == "wasapi-buffer");
            var settings = new GameSettings();
            Assert.That(buffer.Display(settings), Is.EqualTo("6.0 ms"));
            Assert.That(buffer.Hint, Is.EqualTo(L10n.Format("Settings", "hint.number", "6.0 ms", "0.1 ms")));
            Assert.That(buffer.Move(50, -1), Is.EqualTo(50));
            buffer.Set(settings, 123);
            Assert.That(GameSettings.FromJson(settings.ToJson()).audio.wasapiBufferSeconds, Is.EqualTo(.0123f));
            var asio = Rows(SoundPlatform.Windows, AudioBackend.BassASIO).Single(r => r.Id == "asio-buffer");
            Assert.That(asio.Move(0, -1), Is.Zero);
            Assert.That(asio.Display(settings), Is.EqualTo(L10n.Get("Settings", "sound.driverDefault")));
            Assert.That(asio.Hint, Is.EqualTo(L10n.Format("Settings", "hint.number", asio.Display(settings), "1 " + L10n.Get("Settings", "sound.samples"))));
            Assert.That(Rows(SoundPlatform.Android).Any(r => r.Id == "android-aaudio"), Is.True);
            Assert.That(Rows(SoundPlatform.IOS).Any(r => r.Id == "android-aaudio"), Is.False);
        }
    }
}
