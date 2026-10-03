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
        readonly Dictionary<AudioClip, NativeAudioSample> tracks = new();
        readonly Dictionary<AudioClip, NativeAudioSample> effects = new();
        NativeAudioSample main;
        AudioClip mainClip;
        SongDefinition song;
        double seek, scheduledAt;
        bool pending;
        public int PreparedEffects => effects.Count;
        public static int LiveNativeStreams => NativeAudioSample.LiveStreams;
        public double Length => engine != null && engine.Native ? main?.Length ?? 0 : source.clip != null ? source.clip.length : 0;
        public int ActiveVoices
        {
            get { int n = pending || main?.Playing == true ? 1 : 0; foreach (var sample in effects.Values) if (sample.Playing) n++; return n; }
        }
        public bool IsPlaying => engine != null && engine.Native ? ActiveVoices > 0 : source != null && source.isPlaying;
        public double Position => engine != null && engine.Native ? pending ? seek : main?.Position ?? seek : source != null ? source.time : 0;
        internal static AudioBus Get(AudioSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var bus = source.GetComponent<AudioBus>() ?? source.gameObject.AddComponent<AudioBus>();
            bus.source = source; bus.engine = AudioEngine.EnsureInstance(); source.playOnAwake = false;
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
            ReleaseMain(); song = value; source.clip = value.music;
            if (!engine.Native) return;
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
            if (!engine.Native || song != null || source.clip == null || mainClip == source.clip) return;
            ReleaseMain(); mainClip = source.clip;
            if (!tracks.TryGetValue(mainClip, out main)) main = new NativeAudioSample(AudioAssetCatalog.Read(mainClip), engine, false);
        }
        public void PrepareTracks(params AudioClip[] clips)
        {
            if (!engine.Native || clips == null) return;
            foreach (var clip in clips)
                if (clip != null && !tracks.ContainsKey(clip)) tracks.Add(clip, new NativeAudioSample(AudioAssetCatalog.Read(clip), engine, false));
        }
        public void PrepareEffects(params AudioClip[] clips)
        {
            if (!engine.Native || clips == null) return;
            foreach (var clip in clips)
                if (clip != null && !effects.ContainsKey(clip)) effects.Add(clip, new NativeAudioSample(AudioAssetCatalog.Read(clip), engine, false));
        }
        public void Play(double? at = null)
        {
            if (!engine.Native)
            {
                if (source.clip == null) return;
                source.time = (float)seek;
                if (at.HasValue) source.PlayScheduled(at.Value); else source.Play();
                return;
            }
            PrepareMain();
            if (main == null) return;
            scheduledAt = at ?? AudioEngine.Clock; pending = true;
            Update();
        }
        void Update()
        {
            // Like MajdataPlay's game clock, trigger native playback when the countdown expires.
            if (!pending || AudioEngine.Clock < scheduledAt) return;
            pending = false;
            main.Play(source.mute ? 0 : source.volume, source.loop, seek, source.pitch);
        }
        public void OneShot(AudioClip clip)
        {
            if (clip == null) return;
            if (!engine.Native) { source.PlayOneShot(clip); return; }
            PrepareEffects(clip);
            // MajdataPlay rewinds the existing sample; it does not allocate overlapping voices.
            effects[clip].Play(source.mute ? 0 : source.volume, false);
        }
        public void Seek(double seconds)
        {
            seek = Math.Max(0, seconds);
            if (!engine.Native && source.clip != null) source.time = (float)seek;
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
            effects.Clear(); tracks.Clear();
        }
        void OnDisable() => Stop();
        void OnDestroy() => Release();
    }

    public static class AudioPlayback
    {
        public static void SetAudioSong(this AudioSource source, SongDefinition song, bool gameplay = true) => AudioBus.Get(source).SetSong(song, gameplay);
        public static double AudioLength(this AudioSource source) => AudioBus.Get(source).Length;
        public static void PlayAudio(this AudioSource source) => AudioBus.Get(source).Play();
        public static void PlayAudioScheduled(this AudioSource source, double clockTime) => AudioBus.Get(source).Play(clockTime);
        public static void PlayAudioOneShot(this AudioSource source, AudioClip clip) => AudioBus.Get(source).OneShot(clip);
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
