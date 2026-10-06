using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Without an output device the game keeps BASS on its "No Sound" device: no Unity audio,
    // but playback, positions and volume groups behave as with a real device.
    public sealed class AudioFallbackTests
    {
        GameObject root;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AudioEngine.SimulateDeviceFailure = true;
            if (AudioEngine.Instance != null) Object.Destroy(AudioEngine.Instance.gameObject);
            yield return null;
            AudioEngine.EnsureInstance();
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            AudioEngine.SimulateDeviceFailure = false;
            if (AudioEngine.Instance != null) Object.Destroy(AudioEngine.Instance.gameObject);
            yield return null;
            AudioEngine.EnsureInstance();
        }
        [UnityTest]
        public IEnumerator MissingDeviceFallsBackToSilentBassWithRunningPositions()
        {
            var engine = AudioEngine.Instance;
            Assert.That(engine.Available, Is.True, engine.Diagnostics);
            Assert.That(engine.Silent, Is.True, engine.Diagnostics);
            Assert.That(engine.Backend, Is.EqualTo(AudioBackend.Bass));
            Assert.That(engine.Failure, Does.Contain("simulated failure"));

            root = new GameObject("Silent output test");
            var source = root.AddComponent<AudioSource>(); source.playOnAwake = false; source.volume = .4f;
            source.clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            source.SetAudioGroup(AudioGroup.Bgm);
            source.SeekAudio(0.2);
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.1);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(source.isPlaying, Is.False, "Unity's mixer must stay unused.");
            Assert.That(source.IsAudioPlaying(), Is.True);
            Assert.That(source.AudioPosition(), Is.InRange(0.35, 0.7));

            var s = SettingManager.EnsureInstance().Settings.Clone();
            s.audio.volume.master = .5f; s.audio.volume.bgm = .5f;
            SettingManager.EnsureInstance().UseUnsaved(s);
            var bus = source.GetComponent<AudioBus>();
            Assert.That(bus.MainOutputVolume, Is.GreaterThan(0));
            float quarter = bus.MainOutputVolume;
            s.audio.volume.master = 1; s.audio.volume.bgm = 1; SettingManager.EnsureInstance().UseUnsaved(s);
            Assert.That(bus.MainOutputVolume, Is.EqualTo(quarter * 4).Within(.0001));

            source.StopAudio();
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.1);
            source.StopAudio();
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(source.IsAudioPlaying(), Is.False);
        }
    }
}
