using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class LinuxAlsaAudioTests
    {
        static IEnumerator WaitFor(System.Threading.Tasks.Task task, bool fails = false)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Audio operation timed out");
            Assert.That(task.IsFaulted, Is.EqualTo(fails), task.Exception?.ToString());
        }

        [UnityTest]
        public IEnumerator MissingAlsaDeviceRollsBackTheWorkingOutputAndConfiguration()
        {
            if (SoundSettings.Platform != SoundPlatform.Linux) Assert.Ignore("Linux only");
            var manager = SettingManager.EnsureInstance();
            manager.UseUnsaved(new GameSettings());
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            var previous = manager.Settings.Clone();
            var requested = previous.Clone();
            requested.audio.backend = AudioBackend.LinuxAlsaExclusive;
            requested.audio.linuxAlsaDevice = "nonexistent-test-device";
            manager.Set(requested);
            var apply = engine.ApplyPendingSettingsAsync();
            yield return WaitFor(apply, true);
            Assert.That(apply.Exception.GetBaseException().Message, Does.Contain("missing"));
            Assert.That(engine.Backend, Is.EqualTo(AudioBackend.BassSimple));
            Assert.That(engine.Available, Is.True);
            Assert.That(engine.Silent, Is.False, engine.Diagnostics);
            Assert.That(manager.Settings.audio.SameDeviceSettings(previous.audio), Is.True);
            Assert.That(engine.HasPendingDeviceChanges, Is.False);
        }

        // Explicit opt-in only. The operator must first check that the selected card's output AND
        // microphone are idle. No hardcoded hardware or silent first-device selection in this test.
        [UnityTest, Category("AudioDevice")]
        public IEnumerator SelectedHardwareOpensExclusivelyAndReturnsToShared()
        {
            if (SoundSettings.Platform != SoundPlatform.Linux) Assert.Ignore("Linux only");
            string id = Environment.GetEnvironmentVariable("OURTAIKO_TEST_ALSA_DEVICE");
            if (string.IsNullOrEmpty(id)) Assert.Ignore("Requires explicit idle hardware selection: OURTAIKO_TEST_ALSA_DEVICE");
            var device = LinuxAudioDevices.Resolve(LinuxAudioDevices.Enumerate(out _), id);
            Assert.That(device, Is.Not.Null, "Opt-in device no longer exists");
            var manager = SettingManager.EnsureInstance();
            manager.UseUnsaved(new GameSettings());
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            try
            {
                var requested = manager.Settings.Clone();
                var rows = SettingsCatalog.Sound(SoundPlatform.Linux, AudioBackend.LinuxAlsaExclusive).SelectMany(g => g.Rows).ToArray();
                var backendRow = rows.Single(r => r.Id == "backend");
                backendRow.Set(requested, backendRow.Options.ToList().IndexOf(nameof(AudioBackend.LinuxAlsaExclusive)));
                requested.audio.linuxAlsaDevice = id;
                var deviceRow = rows.Single(r => r.Id == "linux-alsa-device");
                int selected = deviceRow.Get(requested);
                Assert.That(selected, Is.GreaterThan(0));
                requested.audio.linuxAlsaDevice = "";
                deviceRow.Set(requested, selected);
                Assert.That(requested.audio.linuxAlsaDevice, Is.EqualTo(id), "The row saves stable identity, not its list index");
                requested.audio.sampleRate = 48000;
                manager.Set(requested);
                yield return WaitFor(engine.ApplyPendingSettingsAsync());
                Assert.That(engine.Backend, Is.EqualTo(AudioBackend.LinuxAlsaExclusive));
                Assert.That(engine.Silent, Is.False, engine.Diagnostics);
                Assert.That(engine.Failure, Is.Null);
                Assert.That(engine.Diagnostics, Does.Contain("ALSA actual:"));
                Assert.That(engine.Diagnostics, Does.Contain("not end-to-end"));
                string owner = File.ReadAllText($"/proc/asound/card{device.Card}/pcm{device.Pcm}p/sub0/status");
                Assert.That(owner, Does.Contain(System.Diagnostics.Process.GetCurrentProcess().Id.ToString()));
                // Exercise the regular native playback path, not just an idle BASS device.
                using (var sample = new NativeAudioSample(File.ReadAllBytes("Assets/OurTaiko/Audio/entry/bgm.ogg"), engine, false))
                {
                    sample.Play(0, false);
                    yield return new WaitForSecondsRealtime(.2f);
                    Assert.That(sample.Playing, Is.True);
                    Assert.That(sample.Position, Is.GreaterThan(0));
                }
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var overlay = SettingsOverlay.Open();
                overlay.Move(4); overlay.Confirm();
                yield return new WaitForSecondsRealtime(.4f);
                TestCapture.Capture("linux-alsa-diagnostics.png", Screen.width, Screen.height, canvas: overlay.View.GetComponent<Canvas>());
                var nav = overlay.Navigator;
                int row = nav.Page.Rows.ToList().FindIndex(r => r.Id == "linux-alsa-device");
                nav.TapRow(row); overlay.Move(0); overlay.Confirm();
                yield return new WaitForSecondsRealtime(.4f);
                TestCapture.Capture("linux-alsa-devices.png", Screen.width, Screen.height, canvas: overlay.View.GetComponent<Canvas>());
                UnityEngine.Object.Destroy(overlay.gameObject);
                yield return null;
            }
            finally { manager.UseUnsaved(new GameSettings()); }
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            Assert.That(engine.Backend, Is.EqualTo(AudioBackend.BassSimple));
            Assert.That(engine.Silent, Is.False, engine.Diagnostics);
            Assert.That(engine.HasPendingDeviceChanges, Is.False);
            using (var shared = new NativeAudioSample(File.ReadAllBytes("Assets/OurTaiko/Audio/entry/bgm.ogg"), engine, false))
            {
                shared.Play(0, false);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(shared.Playing, Is.True);
                Assert.That(shared.Position, Is.GreaterThan(0));
                string restored = File.ReadAllText($"/proc/asound/card{device.Card}/pcm{device.Pcm}p/sub0/status");
                Assert.That(restored, Does.Contain("owner_pid"), "The shared stream must reach the real card, not a dummy output");
            }
        }
    }
}
