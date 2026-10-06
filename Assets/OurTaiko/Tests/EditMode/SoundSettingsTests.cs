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
            Assert.That(rows.Select(r => r.Label), Is.EqualTo(new[] {
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
    }
}
