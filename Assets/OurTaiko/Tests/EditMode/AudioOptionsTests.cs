using NUnit.Framework;
using UnityEditor;

namespace OurTaiko.Tests
{
    public sealed class AudioOptionsTests
    {
        [Test]
        public void NativeLibrariesAreRestrictedToTheirOwnPlatform()
        {
            int count = 0;
            var platforms = new[] { BuildTarget.StandaloneOSX, BuildTarget.StandaloneLinux64,
                BuildTarget.StandaloneWindows64, BuildTarget.Android, BuildTarget.iOS };
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/Plugins/Un4seen.Bass/") || path.Contains(".xcframework/")) continue;
                if (!(AssetImporter.GetAtPath(path) is PluginImporter plugin)) continue;
                count++;
                Assert.That(plugin.GetCompatibleWithAnyPlatform(), Is.False, path);
                Assert.That(plugin.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.False, path);
                Assert.That(plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows), Is.False, path);
                var expected = path.Contains("/macOS/") ? BuildTarget.StandaloneOSX
                    : path.Contains("/Windows/") ? BuildTarget.StandaloneWindows64
                    : path.Contains("/Linux/") ? BuildTarget.StandaloneLinux64
                    : path.Contains("/Android/") ? BuildTarget.Android : BuildTarget.iOS;
                foreach (var target in platforms)
                    Assert.That(plugin.GetCompatibleWithPlatform(target), Is.EqualTo(target == expected), path + " " + target);
            }
            Assert.That(count, Is.EqualTo(40), "Every supported platform must include its native decoders.");
        }

        [Test]
        public void OldAndInvalidSettingsKeepUsableAudioDefaults()
        {
            var options = GameSettings.FromJson("{\"play\":{\"singlePlayerDrumPad\":false}}").audio;
            Assert.That(options.backend, Is.EqualTo(AudioBackend.Automatic));
            Assert.That(options.Period(true), Is.EqualTo(8));
            Assert.That(options.Period(false), Is.EqualTo(16));
            options.devicePeriodMs = 100000; options.deviceBufferMs = -1; options.sampleRate = -1;
            Assert.That(options.Buffer(false), Is.GreaterThanOrEqualTo(options.Period(false) * 2));
            Assert.That(options.Rate, Is.EqualTo(44100));
            Assert.That(GameSettings.FromJson("{\"audio\":null}").audio, Is.Not.Null);
        }
    }
}
