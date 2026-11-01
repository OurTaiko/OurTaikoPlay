using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.EventSystems;

namespace OurTaiko.Tests
{
    public sealed class SoundSettingsFlowTests
    {
        static void AssertRaycast(PointerRelay target)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)target.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var results = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, results);
            Assert.That(results.Count, Is.GreaterThan(0));
            Assert.That(results[0].gameObject, Is.SameAs(target.gameObject), "The visible button must own the tap.");
        }
        [UnityTest]
        public IEnumerator SoundMenuShowsVolumesAndBackendAndPreservesAdvancedSettings()
        {
            var settings = SettingManager.EnsureInstance();
            string dir = Path.Combine(Application.temporaryCachePath, "sound-settings-" + System.Guid.NewGuid());
            settings.Load(Path.Combine(dir, "settings.json"));
            var configured = settings.Settings.Clone();
            configured.audio.deviceBufferMs = 73; configured.audio.volume.master = .35f;
            settings.Set(configured);
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var entry = Object.FindAnyObjectByType<EntryScene>();
                var overlay = SettingsOverlay.Open();
                yield return null;
                var nav = overlay.Navigator;
                overlay.Move(4); overlay.Confirm();
                Assert.That(nav.Page.Title, Is.EqualTo("Sound"));
                Assert.That(nav.Page.Rows.Count, Is.EqualTo(SettingsCatalog.Sound(SoundSettings.Platform, settings.Settings.audio.Backend).Sum(g => g.Rows.Count)));
                Assert.That(nav.Row.Label, Is.EqualTo("Master"));
                Assert.That(overlay.previewVoice, Is.Not.Null);
                overlay.Confirm(); overlay.Move(3); // 35% -> 50%
                overlay.Confirm();
                Assert.That(settings.Settings.audio.volume.master, Is.EqualTo(.5f));
                Assert.That(entry.bgm.GetComponent<AudioBus>().MainOutputVolume, Is.EqualTo(entry.bgm.volume * .5f).Within(.001));
                Assert.That(engine.HasPendingDeviceChanges, Is.False);
                overlay.Move(5);
                Assert.That(nav.Row.Label, Is.EqualTo("Voice"));
                overlay.Side(-1); // previews voice using the Voice group
                overlay.Move(1);
                Assert.That(nav.Row.Label, Is.EqualTo("Output Backend"));
                overlay.Confirm();
                overlay.Move(nav.SheetOptions.Count - 1); // BassSimple, the last choice everywhere
                yield return new WaitForSecondsRealtime(0.3f);
                var option = overlay.View.GetComponentsInChildren<PointerRelay>().Single(r => r.name == "Option" + nav.SheetIndex);
                AssertRaycast(option);
                int before = engine.Generation;
                option.Clicked();
                Assert.That(engine.Generation, Is.EqualTo(before));
                Assert.That(engine.HasPendingDeviceChanges, Is.True);
                var saved = GameSettings.FromJson(File.ReadAllText(settings.FilePath));
                Assert.That(saved.audio.backend, Is.EqualTo(AudioBackend.BassSimple));
                Assert.That(saved.audio.deviceBufferMs, Is.EqualTo(73));
                Assert.That(saved.audio.volume.master, Is.EqualTo(.5f));
                Assert.That(entry.bgm.IsAudioPlaying(), Is.True);
                overlay.Move(1);
                Assert.That(nav.Row.Label, Is.EqualTo("Output Period"));
                overlay.Confirm(); overlay.Move(4); overlay.Confirm();
                Assert.That(settings.Settings.audio.devicePeriodMs, Is.EqualTo(4));
                Assert.That(engine.Generation, Is.EqualTo(before), "Period changes also wait until the overlay closes");
                saved = GameSettings.FromJson(File.ReadAllText(settings.FilePath));
                Assert.That(saved.audio.devicePeriodMs, Is.EqualTo(4));
                overlay.Back();
                Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
                // Every Windows-only row draws, focused and not, without opening a Windows audio device.
                foreach (var backend in new[] { AudioBackend.BassSimple, AudioBackend.BassWASAPI, AudioBackend.BassASIO })
                {
                    var preview = new SettingsNavigator(SettingsCatalog.Pages(SoundPlatform.Windows), new GameSettings { audio = new AudioOptions { backend = backend } });
                    preview.Move(4); preview.Confirm();
                    for (int i = 0; i < preview.Page.Rows.Count; i++) { preview.TapRow(i); overlay.View.Show(preview, "", null, false); }
                }
            }
            finally
            {
                if (SettingsOverlay.Instance != null) Object.Destroy(SettingsOverlay.Instance.gameObject);
                settings.UseUnsaved(new GameSettings());
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
        }

        static IEnumerator WaitFor(System.Threading.Tasks.Task task)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Audio operation timed out");
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        [UnityTest]
        public IEnumerator SoundSettingsExitSwitchesBackendsAndSkipsUnchangedOutput()
        {
            var settings = SettingManager.EnsureInstance();
            settings.UseUnsaved(new GameSettings());
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            // Automatic -> BASS -> Automatic reopens the device each time, then leave with volume-only changes.
            foreach (var backend in new[] { AudioBackend.BassSimple, AudioBackend.Automatic, AudioBackend.Automatic })
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var overlay = SettingsOverlay.Open();
                yield return null;
                int before = engine.Generation;
                bool changed = settings.Settings.audio.backend != backend;
                overlay.Navigator.Settings.audio.backend = backend;
                overlay.Navigator.Settings.audio.volume.master = .5f;
                settings.Set(overlay.Navigator.Settings);
                Assert.That(engine.Generation, Is.EqualTo(before), "Editing must not reopen the device");
                overlay.Back(); overlay.Back(); // the second is ignored once closed
                Assert.That(SettingsOverlay.IsOpen, Is.False);
                Assert.That(engine.Generation, Is.EqualTo(before), "Apply only after the fade closes");
                float deadline = Time.realtimeSinceStartup + 15;
                yield return null;
                while (SceneSwitcher.Instance.IsInputBlocked && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.EntryScene));
                Assert.That(engine.Backend, Is.EqualTo(backend == AudioBackend.Automatic && SoundSettings.Platform == SoundPlatform.Windows ? AudioBackend.BassWASAPI : AudioBackend.BassSimple));
                Assert.That(engine.Silent, Is.False, engine.Diagnostics);
                Assert.That(engine.Generation, Is.EqualTo(before + (changed ? 1 : 0)));
                Assert.That(engine.HasPendingDeviceChanges, Is.False);
                // Entry and later scenes can decode and play through the newly initialized output.
                var go = new GameObject("Reload playback");
                var output = go.AddComponent<AudioSource>(); output.volume = .2f;
                output.clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
                output.PlayAudio(); yield return new WaitForSecondsRealtime(.1f);
                Assert.That(output.IsAudioPlaying(), Is.True);
                Assert.That(output.GetComponent<AudioBus>().MainOutputVolume, Is.EqualTo(.1f).Within(.001));
                Object.Destroy(go); yield return null;
            }
            settings.UseUnsaved(new GameSettings());
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
        }

        // A scene switch under the open overlay dismisses it; the device change it left pending is
        // applied once that switch is over, not left hanging until the next time Settings closes.
        [UnityTest]
        public IEnumerator ASceneSwitchUnderTheOverlayAppliesPendingDeviceChanges()
        {
            var settings = SettingManager.EnsureInstance();
            settings.UseUnsaved(new GameSettings());
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var overlay = SettingsOverlay.Open();
            yield return null;
            int before = engine.Generation;
            overlay.Navigator.Settings.audio.backend = AudioBackend.BassSimple;
            settings.Set(overlay.Navigator.Settings);
            Assert.That(engine.HasPendingDeviceChanges, Is.True);
            var switcher = SceneSwitcher.EnsureInstance();
            switcher.SwitchScene(SceneSwitcher.EntryScene);
            float deadline = Time.realtimeSinceStartup + 20;
            while (SettingsOverlay.IsOpen && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SettingsOverlay.IsOpen, Is.False, "The switch dismisses the overlay.");
            while ((switcher.IsInputBlocked || engine.HasPendingDeviceChanges) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(engine.HasPendingDeviceChanges, Is.False);
            Assert.That(engine.Backend, Is.EqualTo(AudioBackend.BassSimple));
            Assert.That(engine.Generation, Is.EqualTo(before + 1));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.EntryScene));
            settings.UseUnsaved(new GameSettings());
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
        }

        [UnityTest]
        public IEnumerator SoundSettingsReloadWaitsForBackgroundPreparation()
        {
            var settings = SettingManager.EnsureInstance(); settings.UseUnsaved(new GameSettings());
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            var bytes = File.ReadAllBytes("Assets/OurTaiko/Audio/entry/bgm.ogg");
            var deviceLock = typeof(AudioEngine).GetField("DeviceLock", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            using var entered = new System.Threading.ManualResetEventSlim();
            using var release = new System.Threading.ManualResetEventSlim();
            int generation = engine.Generation;
            NativeAudioSample sample = null;
            var worker = System.Threading.Tasks.Task.Run(() =>
            {
                lock (deviceLock)
                {
                    sample = new NativeAudioSample(bytes, engine, false, false, generation);
                    entered.Set();
                    if (!release.Wait(10000)) throw new System.TimeoutException("Test did not release preparation");
                }
            });
            try
            {
                float deadline = Time.realtimeSinceStartup + 5;
                while (!entered.IsSet && !worker.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(entered.IsSet, Is.True);
                var next = settings.Settings.Clone(); next.audio.backend = AudioBackend.BassSimple; settings.Set(next);
                var applying = engine.ApplyPendingSettingsAsync();
                yield return null; yield return null;
                Assert.That(applying.IsCompleted, Is.False);
                Assert.That(sample.IsDisposed, Is.False, "The output must remain alive until preparation finishes");
                release.Set();
                yield return WaitFor(worker);
                yield return WaitFor(applying);
                Assert.That(sample.IsDisposed, Is.True);
                Assert.That(NativeAudioSample.LiveStreams, Is.Zero);
                Assert.That(engine.Backend, Is.EqualTo(AudioBackend.BassSimple));
            }
            finally { release.Set(); }
            settings.UseUnsaved(new GameSettings());
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
        }

        [UnityTest]
        public IEnumerator SoundSettingsReloadInvalidatesPreparedSamplesAndRollsBackFailure()
        {
            var settings = SettingManager.EnsureInstance();
            settings.UseUnsaved(new GameSettings());
            var engine = AudioEngine.EnsureInstance();
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            var bytes = File.ReadAllBytes("Assets/OurTaiko/Audio/entry/bgm.ogg");
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            var sample = new NativeAudioSample(bytes, engine, false);
            song.SetPreparedAudio(sample);
            int oldGeneration = engine.Generation;
            var changed = settings.Settings.Clone(); changed.audio.backend = AudioBackend.BassSimple;
            settings.Set(changed);
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            Assert.That(sample.IsDisposed, Is.True);
            Assert.That(song.HasPreparedAudio, Is.False);
            Assert.That(song.TakePreparedAudio(), Is.Null);
            Assert.That(NativeAudioSample.LiveStreams, Is.Zero);
            sample.Dispose(); // late cancelled task completion is harmless
            changed.audio.backend = AudioBackend.Automatic; settings.Set(changed);
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
            Assert.Throws<System.OperationCanceledException>(() => new NativeAudioSample(bytes, engine, false, false, oldGeneration));
            Object.Destroy(song);

            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var overlay = SettingsOverlay.Open();
            yield return null;
            // Invalid driver on Windows, unsupported backend on other platforms.
            overlay.Navigator.Settings.audio.backend = AudioBackend.BassASIO;
            overlay.Navigator.Settings.audio.asioDevice = int.MaxValue;
            overlay.Navigator.Settings.audio.volume.master = .6f;
            overlay.Back();
            Assert.That(SettingsOverlay.IsOpen, Is.False);
            // The device is restored, Entry reloads for its music and the overlay reopens on Sound with the reason.
            float deadline = Time.realtimeSinceStartup + 20;
            while ((!SettingsOverlay.IsOpen || SceneSwitcher.Instance.IsSwitching) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SettingsOverlay.IsOpen, Is.True);
            Assert.That(overlay.Navigator.Page.Id, Is.EqualTo("sound"));
            Assert.That(overlay.Status, Does.Contain("Could not apply audio settings"));
            Assert.That(engine.Available, Is.True);
            Assert.That(settings.Settings.audio.backend, Is.EqualTo(AudioBackend.Automatic));
            Assert.That(settings.Settings.audio.volume.master, Is.EqualTo(.6f));
            yield return null;
            Assert.That(Object.FindAnyObjectByType<EntryScene>().bgm.IsAudioPlaying(), Is.True);
            overlay.Back(); overlay.Back();
            Object.Destroy(overlay.gameObject);
            settings.UseUnsaved(new GameSettings());
            yield return WaitFor(engine.ApplyPendingSettingsAsync());
        }

        [UnityTest]
        public IEnumerator NativeMusicVolumeUpdatesWithoutSeekingOrRestarting()
        {
            var settings=SettingManager.EnsureInstance(); settings.UseUnsaved(new GameSettings());
            var go=new GameObject("VolumeTest"); var source=go.AddComponent<AudioSource>();
            source.clip=UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            source.volume=.2f; source.SetAudioGroup(AudioGroup.Bgm); source.PlayAudio();
            try
            {
                yield return new WaitForSecondsRealtime(.15f);
                double before=source.AudioPosition();
                var s=new GameSettings(); s.audio.volume.master=.5f; s.audio.volume.bgm=.25f;
                settings.UseUnsaved(s);
                Assert.That(source.GetComponent<AudioBus>().MainOutputVolume,Is.EqualTo(.025f).Within(.0001));
                Assert.That(source.AudioPosition(),Is.GreaterThanOrEqualTo(before-.01));
                s.audio.volume.master=0; settings.UseUnsaved(s);
                Assert.That(source.GetComponent<AudioBus>().MainOutputVolume,Is.Zero);
                Assert.That(source.IsAudioPlaying(),Is.True);
            }
            finally { Object.Destroy(go); settings.UseUnsaved(new GameSettings()); }
        }
    }
}
