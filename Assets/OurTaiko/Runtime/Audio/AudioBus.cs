using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    [DisallowMultipleComponent]
    public sealed class AudioBus : MonoBehaviour
    {
        AudioSource source;
        AudioEngine engine;
        SettingManager settings;
        float authoredVolume;
        public AudioGroup Group { get; private set; } = AudioGroup.Effects;
        readonly Dictionary<AudioClip, AudioGroup> effectGroups = new();
        public float MainOutputVolume => main?.OutputVolume ?? 0;
        float Volume(AudioGroup group) => source.mute ? 0 : authoredVolume * settings.Settings.audio.volume.Gain(group);
        public void SetGroup(AudioGroup group) { Group = group; ApplyVolumes(settings.Settings); }
        void ApplyVolumes(GameSettings unused)
        {
            if (engine == null || source == null) return;
            main?.SetVolume(Volume(Group));
            foreach (var pair in tracks) pair.Value.SetVolume(Volume(Group));
            foreach (var pair in effects) pair.Value.SetVolume(Volume(effectGroups.TryGetValue(pair.Key, out var group) ? group : Group));
        }
        readonly Dictionary<AudioClip, NativeAudioSample> tracks = new();
        readonly Dictionary<AudioClip, NativeAudioSample> effects = new();
        NativeAudioSample main;
        AudioClip mainClip;
        SongDefinition song;
        double seek, scheduledAt;
        bool pending;
        public int PreparedEffects => effects.Count;
        public static int LiveNativeStreams => NativeAudioSample.LiveStreams;
        public double Length => main?.Length ?? 0;
        public int ActiveVoices
        {
            get { int n = pending || main?.Playing == true ? 1 : 0; foreach (var sample in effects.Values) if (sample.Playing) n++; return n; }
        }
        public bool IsPlaying => ActiveVoices > 0;
        public double Position => pending ? seek : main?.Position ?? seek;
        internal static AudioBus Get(AudioSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var bus = source.GetComponent<AudioBus>() ?? source.gameObject.AddComponent<AudioBus>();
            if (bus.source == null)
            {
                bus.source = source; bus.authoredVolume = source.volume;
                bus.settings = SettingManager.EnsureInstance();
                bus.settings.Changed += bus.ApplyVolumes;
            }
            bus.engine = AudioEngine.EnsureInstance();
            source.playOnAwake = false;
            return bus;
        }
        void ReleaseMain()
        {
            pending = false;
            main?.Stop();
            if (main != null && (mainClip == null || !tracks.ContainsKey(mainClip))) main.Dispose();
            main = null; mainClip = null; song = null;
        }
        public void SetSong(SongDefinition value, bool gameplay = true)
        {
            ReleaseMain(); song = value; source.clip = value.music; SetGroup(AudioGroup.Track);
            if (!engine.Available) return;
            main = value.TakePreparedAudio();
            if (main == null)
            {
                byte[] bytes = !string.IsNullOrEmpty(value.audioPath) ? File.ReadAllBytes(value.audioPath)
                    : value.music != null ? AudioAssetCatalog.Read(value.music) : null;
                if (bytes != null) main = new NativeAudioSample(bytes, engine, true, gameplay);
            }
        }
        public void PrepareMain()
        {
            if (!engine.Available || song != null || source.clip == null || mainClip == source.clip) return;
            ReleaseMain(); mainClip = source.clip;
            if (!tracks.TryGetValue(mainClip, out main)) main = new NativeAudioSample(AudioAssetCatalog.Read(mainClip), engine, false);
        }
        public void PrepareTracks(params AudioClip[] clips)
        {
            if (!engine.Available || clips == null) return;
            foreach (var clip in clips)
                if (clip != null && !tracks.ContainsKey(clip)) tracks.Add(clip, new NativeAudioSample(AudioAssetCatalog.Read(clip), engine, false));
        }
        public void PrepareEffects(params AudioClip[] clips)
        {
            if (!engine.Available || clips == null) return;
            foreach (var clip in clips)
                if (clip != null && !effects.ContainsKey(clip)) effects.Add(clip, new NativeAudioSample(AudioAssetCatalog.Read(clip), engine, false));
        }
        public void Play(double? at = null)
        {
            PrepareMain();
            if (main == null) return;
            scheduledAt = at ?? GameTimeline.AudioNow; pending = true;
            Update();
        }
        void Update()
        {
            // Like MajdataPlay's game clock, trigger native playback when the countdown expires.
            if (!pending || GameTimeline.AudioNow < scheduledAt) return;
            pending = false;
            main.Play(Volume(Group), source.loop, seek, source.pitch);
        }
        public void OneShot(AudioClip clip, AudioGroup? group = null)
        {
            if (clip == null) return;
            var selected = group ?? Group;
            PrepareEffects(clip);
            if (!effects.ContainsKey(clip)) return;
            // MajdataPlay rewinds the existing sample; it does not allocate overlapping voices.
            effectGroups[clip] = selected;
            effects[clip].Play(Volume(selected), false);
        }
        public void Seek(double seconds)
        {
            seek = Math.Max(0, seconds);
        }
        public void Stop()
        {
            pending = false; seek = 0; source?.Stop(); main?.Stop();
            foreach (var sample in effects.Values) sample.Stop();
        }
        public void Release()
        {
            ReleaseMain();
            foreach (var sample in effects.Values) sample.Dispose();
            foreach (var sample in tracks.Values) sample.Dispose();
            effects.Clear(); tracks.Clear(); effectGroups.Clear();
        }
        void OnDisable() => Stop();
        void OnDestroy()
        {
            if (settings != null) settings.Changed -= ApplyVolumes;
            Release();
        }
    }

    public static class AudioPlayback
    {
        public static void SetAudioGroup(this AudioSource source, AudioGroup group) { if (source != null) AudioBus.Get(source).SetGroup(group); }
        public static void SetAudioSong(this AudioSource source, SongDefinition song, bool gameplay = true) => AudioBus.Get(source).SetSong(song, gameplay);
        public static double AudioLength(this AudioSource source) => AudioBus.Get(source).Length;
        public static void PlayAudio(this AudioSource source) => AudioBus.Get(source).Play();
        public static void PlayAudioScheduled(this AudioSource source, double clockTime) => AudioBus.Get(source).Play(clockTime);
        public static void PlayAudioOneShot(this AudioSource source, AudioClip clip, AudioGroup? group = null) => AudioBus.Get(source).OneShot(clip, group);
        public static void StopAudio(this AudioSource source)
        {
            if (source == null) return;
            var bus = source.GetComponent<AudioBus>();
            if (bus != null) bus.Stop(); else source.Stop();
        }
        public static bool IsAudioPlaying(this AudioSource source) => source != null && AudioBus.Get(source).IsPlaying;
        public static double AudioPosition(this AudioSource source) => AudioBus.Get(source).Position;
        public static void SeekAudio(this AudioSource source, double seconds) => AudioBus.Get(source).Seek(seconds);
        public static void PrepareAudio(this AudioSource source) => AudioBus.Get(source).PrepareMain();
        public static void PrepareAudioTracks(this AudioSource source, params AudioClip[] clips) => AudioBus.Get(source).PrepareTracks(clips);
        public static void PrepareAudioEffects(this AudioSource source, params AudioClip[] clips) => AudioBus.Get(source).PrepareEffects(clips);
    }
}
