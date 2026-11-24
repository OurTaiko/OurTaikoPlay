using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class NativeAudioTests
    {
        GameObject root;
        AudioClip clip;
        AudioSource source;
        int streamsBefore;
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            streamsBefore = AudioBus.LiveNativeStreams;
            root = new GameObject("NativeAudioTest");
            source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            source.volume = 0.001f;
            clip.UnloadAudioData();
            source.clip = clip;
            source.PrepareAudio();
            source.PrepareAudioEffects(clip);
            Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Unloaded), "Native decode must not load Unity PCM.");
            yield return null;
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(root);
            yield return null;

            Assert.That(AudioBus.LiveNativeStreams, Is.EqualTo(streamsBefore), "Native streams must be released on scene/object destruction.");
        }
        [UnityTest]
        public IEnumerator NativeOutputSchedulesSeeksStopsAndReplaysWithoutUnityMixer()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX
            Assert.That(AudioEngine.Instance.Available, Is.True, AudioEngine.Instance.Diagnostics);
#endif
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.3);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(source.AudioPosition(), Is.LessThan(0.04), "The countdown must not consume PCM.");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(source.IsAudioPlaying(), Is.True);
            Assert.That(source.AudioPosition(), Is.InRange(0.15, 0.5));
            if (AudioEngine.Instance.Available) Assert.That(source.isPlaying, Is.False, "Native audio must bypass Unity's mixer.");
            source.StopAudio();
            Assert.That(source.IsAudioPlaying(), Is.False);
            Assert.That(source.AudioPosition(), Is.Zero);
            source.SeekAudio(1);
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.05);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(source.AudioPosition(), Is.InRange(1.08, 1.4));
            source.StopAudio();
            source.PlayAudioScheduled(GameTimeline.AudioNow + 0.15);
            source.StopAudio();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(source.IsAudioPlaying(), Is.False, "Stopping a pending start must cancel it.");
        }
        [UnityTest]
        public IEnumerator RapidEffectsRestartSameSampleLoopAndDisableStopsEverything()
        {
            for (int i = 0; i < 12; i++) { source.PlayAudioOneShot(clip); yield return null; }
            Assert.That(source.IsAudioPlaying(), Is.True);
            if (AudioEngine.Instance.Available) Assert.That(source.GetComponent<AudioBus>().ActiveVoices, Is.EqualTo(1), "Like MajdataPlay, repeated hits restart the same sample.");
            source.StopAudio();
            source.loop = true;
            source.SeekAudio(source.AudioLength() - 0.1);
            source.PlayAudio();
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(source.IsAudioPlaying(), Is.True);
            Assert.That(source.AudioPosition(), Is.LessThan(1));
            root.SetActive(false);
            Assert.That(source.IsAudioPlaying(), Is.False);
        }
    }
}
