using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class AudioFallbackTests
    {
        GameSettings saved;
        GameObject root;
        AudioClip clip;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var settings = SettingManager.EnsureInstance();
            saved = settings.Settings.Clone();
            var fallback = saved.Clone(); fallback.audio.backend = AudioBackend.Unity;
            settings.UseUnsaved(fallback);
            if (AudioEngine.Instance != null) Object.Destroy(AudioEngine.Instance.gameObject);
            yield return null;
            AudioEngine.EnsureInstance();
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            if (clip != null) Object.Destroy(clip);
            if (AudioEngine.Instance != null) Object.Destroy(AudioEngine.Instance.gameObject);
            yield return null;
            SettingManager.EnsureInstance().UseUnsaved(saved);
            AudioEngine.EnsureInstance();
        }
        [UnityTest]
        public IEnumerator UnityFallbackSoundSettingsScaleMusicAndSeparateEffectGroups()
        {
            root = new GameObject("Unity volume test");
            var source = root.AddComponent<AudioSource>(); source.volume = .4f;
            clip = AudioClip.Create("Volume PCM", 48000, 1, 48000, false);
            source.clip = clip; source.SetAudioGroup(AudioGroup.Bgm); source.PlayAudio();
            var s = SettingManager.EnsureInstance().Settings.Clone();
            s.audio.volume.master = .5f; s.audio.volume.bgm = .5f;
            s.audio.volume.drum = .25f; s.audio.volume.voice = .75f;
            SettingManager.EnsureInstance().UseUnsaved(s);
            Assert.That(source.volume, Is.EqualTo(.1f).Within(.0001));
            source.PlayAudioOneShot(clip, AudioGroup.Drum);
            source.PlayAudioOneShot(clip, AudioGroup.Voice);
            var outputs = root.GetComponents<AudioSource>();
            Assert.That(outputs.Length, Is.EqualTo(3));
            Assert.That(outputs[1].volume, Is.EqualTo(.05f).Within(.0001));
            Assert.That(outputs[2].volume, Is.EqualTo(.15f).Within(.0001));
            s.audio.volume.master = 0; SettingManager.EnsureInstance().UseUnsaved(s);
            foreach(var output in outputs) Assert.That(output.volume, Is.Zero);
            source.StopAudio(); yield return null;
            foreach(var output in outputs) Assert.That(output.isPlaying, Is.False);
        }
        [UnityTest]
        public IEnumerator UnityAudioFallbackUsesDspSchedulingAndCanCancel()
        {
            Assert.That(AudioEngine.Instance.Backend, Is.EqualTo(AudioBackend.Unity));
            root = new GameObject("Audio fallback test");
            var source = root.AddComponent<AudioSource>(); source.playOnAwake = false;
            clip = AudioClip.Create("Fallback PCM", 48000, 1, 48000, false);
            source.clip = clip;
            source.SeekAudio(0.2);
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.1);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(source.isPlaying, Is.True);
            Assert.That(source.AudioPosition(), Is.InRange(0.3, 0.6));
            source.StopAudio();
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.1);
            source.StopAudio();
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(source.IsAudioPlaying(), Is.False);
        }
    }
}
