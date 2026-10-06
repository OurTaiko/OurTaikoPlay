using System.Collections;
using System.IO;
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
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
                yield return null;
                var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
                var view = scene.view;
                scene.Ka(3); scene.Don();
                Assert.That(scene.Menu.CurrentType.Label, Is.EqualTo("Sound"));
                Assert.That(scene.Menu.ItemCount, Is.EqualTo(8)); // six volumes, backend, Return
                Assert.That(scene.Menu.CurrentItem.Label, Is.EqualTo("Master Volume"));
                Assert.That(view.nextItems.gameObject.activeInHierarchy, Is.True);
                Assert.That(scene.previewVoice, Is.Not.Null);
                TestCapture.Capture("SoundSettings.png");
                scene.Don(); scene.Ka(3); // 35% -> 50%
                TestCapture.Capture("SoundVolumeChoice.png");
                scene.Don();
                Assert.That(settings.Settings.audio.volume.master, Is.EqualTo(.5f));
                Assert.That(scene.bgm.GetComponent<AudioBus>().MainOutputVolume, Is.EqualTo(scene.bgm.volume * .5f).Within(.001));
                Assert.That(engine.HasPendingDeviceChanges, Is.False);
                AssertRaycast(view.nextItems);
                view.nextItems.Clicked(); // Effects Volume
                view.itemSwipe.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) });
                Assert.That(scene.Menu.CurrentItem.Label, Is.EqualTo("Voice Volume"));
                scene.Don(); scene.Don(); // previews voice using the Voice group
                scene.Ka(1);
                Assert.That(scene.Menu.CurrentItem.Label, Is.EqualTo("Output Backend"));
                scene.Don();
                scene.Ka(scene.Menu.CurrentItem.Choices.Count - 1); // BassSimple, the last choice everywhere
                TestCapture.Capture("SoundBackendChoice.png");
                int slot = scene.Menu.ChoiceIndex - view.FirstChoice;
                AssertRaycast(view.choiceRows[slot].click);
                int before = engine.Generation;
                view.choiceRows[slot].click.Clicked();
                Assert.That(engine.Generation, Is.EqualTo(before));
                Assert.That(engine.HasPendingDeviceChanges, Is.True);
                Assert.That(view.outputStatus.text, Does.Contain("Applies on exit"));
                var saved = GameSettings.FromJson(File.ReadAllText(settings.FilePath));
                Assert.That(saved.audio.backend, Is.EqualTo(AudioBackend.BassSimple));
                Assert.That(saved.audio.deviceBufferMs, Is.EqualTo(73));
                Assert.That(saved.audio.volume.master, Is.EqualTo(.5f));
                Assert.That(scene.bgm.IsAudioPlaying(), Is.True);
                scene.Ka(1); scene.Don();
                Assert.That(scene.Menu.Focus, Is.EqualTo(SettingsFocus.Types));
            }
            finally
            {
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
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
                yield return null;
                var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
                int before = engine.Generation;
                bool changed = settings.Settings.audio.backend != backend;
                scene.Menu.Settings.audio.backend = backend;
                scene.Menu.Settings.audio.volume.master = .5f;
                settings.Set(scene.Menu.Settings);
                Assert.That(engine.Generation, Is.EqualTo(before), "Editing must not reopen the device");
                scene.Ka(scene.Menu.TypeCount - 1);
                scene.Don(); scene.Don(); // second confirm is blocked throughout the transition
                Assert.That(engine.Generation, Is.EqualTo(before), "Apply only after the fade closes");
                float deadline = Time.realtimeSinceStartup + 15;
                while ((scene != null || SceneSwitcher.Instance.IsInputBlocked) && Time.realtimeSinceStartup < deadline) yield return null;
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

            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
            yield return null;
            var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
            // Invalid driver on Windows, unsupported backend on other platforms.
            scene.Menu.Settings.audio.backend = AudioBackend.BassASIO;
            scene.Menu.Settings.audio.asioDevice = int.MaxValue;
            scene.Menu.Settings.audio.volume.master = .6f;
            scene.Ka(scene.Menu.TypeCount - 1); scene.Don();
            float deadline = Time.realtimeSinceStartup + 15;
            while (scene.HasLeft && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(scene.HasLeft, Is.False);
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.False);
            Assert.That(engine.Available, Is.True);
            Assert.That(settings.Settings.audio.backend, Is.EqualTo(AudioBackend.Automatic));
            Assert.That(settings.Settings.audio.volume.master, Is.EqualTo(.6f));
            Assert.That(scene.view.outputStatus.text, Does.Contain("Could not apply audio settings"));
            Assert.That(scene.bgm.IsAudioPlaying(), Is.True);
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
