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
        [TestCase(SoundPlatform.Windows)]
        [TestCase(SoundPlatform.Android)]
        [TestCase(SoundPlatform.IOS)]
        [TestCase(SoundPlatform.Desktop)]
        public void SoundMenuOffersVolumesAndPlatformSupportedBackends(SoundPlatform platform)
        {
            var rows = SoundSettings.Catalog(platform);
            Assert.That(rows.Take(7).Select(r => r.Label), Is.EqualTo(new[] {
                "Master Volume", "BGM Volume", "Track Volume", "Drum Volume", "Effects Volume", "Voice Volume", "Output Backend"
            }));
            var backend = rows[6];
            Assert.That(backend.Label, Is.EqualTo("Output Backend"));
            Assert.That(backend.Choices.Contains("BassASIO"), Is.EqualTo(platform == SoundPlatform.Windows));
            Assert.That(backend.Choices.Contains("BassWASAPI"), Is.EqualTo(platform == SoundPlatform.Windows));
            Assert.That(backend.Choices.Contains("Unity"), Is.False);
            // The cross-platform BassSimple output is always the last choice.
            Assert.That(backend.Choices, Is.EqualTo(platform == SoundPlatform.Windows
                ? new[] { "Automatic", "BassWASAPI", "BassASIO", "BassSimple" } : new[] { "Automatic", "BassSimple" }));
        }
        [Test]
        public void BackendChangesPreserveAdvancedConfiguration()
        {
            var s = new GameSettings();
            s.audio.deviceBufferMs = 73; s.audio.asioDevice = 2; s.audio.volume.master = .35f;
            SoundSettings.Catalog(SoundPlatform.Desktop)[6].Set(s, 1);
            var saved = GameSettings.FromJson(s.ToJson());
            Assert.That(saved.audio.backend, Is.EqualTo(AudioBackend.BassSimple));
            Assert.That(saved.audio.deviceBufferMs, Is.EqualTo(73));
            Assert.That(saved.audio.asioDevice, Is.EqualTo(2));
            Assert.That(saved.audio.volume.master, Is.EqualTo(.35f));
        }

        [Test]
        public void SwitchingBackendRebuildsRowsOnlyOnConfirmAndPreservesValues()
        {
            var types = new[] { new SettingType("Sound", s => SoundSettings.Catalog(SoundPlatform.Windows, s.audio.Backend)) };
            var settings = new GameSettings();
            settings.audio.wasapiBufferSeconds = .0123f;
            settings.audio.asioDevice = 3;
            settings.audio.devicePeriodMs = 4;
            var menu = new SettingsMenu(types, settings);
            var other = new SettingsMenu(types, new GameSettings());
            menu.Don(); menu.Ka(6); menu.Don(); menu.Ka(2);
            Assert.That(menu.CurrentType.Items.Any(i => i.Label == "WASAPI Buffer"), Is.True);
            menu.Back(); menu.Don(); menu.Ka(2); menu.Don();
            Assert.That(menu.CurrentItem.Label, Is.EqualTo("Output Backend"));
            Assert.That(menu.CurrentType.Items.Any(i => i.Label == "WASAPI Buffer"), Is.False);
            Assert.That(menu.CurrentType.Items.Single(i => i.Label == "ASIO Device Index").Get(menu.Settings), Is.EqualTo(3));
            Assert.That(other.Types[0].Items.Any(i => i.Label == "WASAPI Buffer"), Is.True, "Menus must not share mutable rows");
            menu.Don(); menu.Ka(1); menu.Don();
            Assert.That(menu.CurrentType.Items.Any(i => i.Label.StartsWith("ASIO")), Is.False);
            Assert.That(menu.CurrentType.Items.Single(i => i.Label == "Audio Output Period").Get(menu.Settings), Is.EqualTo(4));
            menu.Don(); menu.Ka(-3); menu.Don();
            Assert.That(menu.CurrentType.Items.Any(i => i.Label == "WASAPI Buffer"), Is.True);
            var saved = GameSettings.FromJson(menu.Settings.ToJson());
            Assert.That(saved.audio.wasapiBufferSeconds, Is.EqualTo(.0123f));
            Assert.That(saved.audio.asioDevice, Is.EqualTo(3));
            Assert.That(saved.audio.devicePeriodMs, Is.EqualTo(4));
        }

        [Test]
        public void BackendNumericControlsUseBoundsAndCorrectUnits()
        {
            var rows = SoundSettings.Catalog(SoundPlatform.Windows, AudioBackend.BassWASAPI);
            var buffer = rows.Single(i => i.Label == "WASAPI Buffer");
            var settings = new GameSettings();
            Assert.That(buffer.Format(settings), Is.EqualTo("6.0 ms"));
            Assert.That(buffer.StepDisplay, Is.EqualTo("0.1 ms"));
            Assert.That(buffer.Move(50, -1), Is.EqualTo(50));
            buffer.Set(settings, 123);
            Assert.That(GameSettings.FromJson(settings.ToJson()).audio.wasapiBufferSeconds, Is.EqualTo(.0123f));
            var asio = SoundSettings.Catalog(SoundPlatform.Windows, AudioBackend.BassASIO).Single(i => i.Label == "ASIO Buffer");
            Assert.That(asio.Move(0, -1), Is.Zero);
            Assert.That(asio.Format(settings), Is.EqualTo("Driver Default"));
            Assert.That(SoundSettings.Catalog(SoundPlatform.Android).Any(i => i.Label == "Android AAudio"), Is.True);
            Assert.That(SoundSettings.Catalog(SoundPlatform.IOS).Any(i => i.Label == "Android AAudio"), Is.False);
        }
    }
}
