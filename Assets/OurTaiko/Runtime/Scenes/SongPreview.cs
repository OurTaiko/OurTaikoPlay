using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace OurTaiko
{
    // Song select's preview: the selected song's audio on its own AudioSource. Play starts loading
    // in the host's coroutine; Stop (any selection change) cancels a load in flight and stops it.
    // While the preview sounds, the selection BGM is paused (issue #44); the preview fades out at
    // its end and fades in again from its start, and the BGM resumes only once the preview stops.
    public sealed class SongPreview
    {
        public const double FadeOutSeconds = 1, FadeInSeconds = 1;
        // How close to the end counts as the end, in seconds.
        const double EndSlack = 0.05;
        readonly MonoBehaviour host;
        readonly AudioSource source, bgm;
        bool bgmPaused;
        int generation;
        CancellationTokenSource cancellation;
        // The stand-in that holds an online song's decoded preview.
        SongDefinition onlineSong;

        // Play was asked for the current selection (it may still be loading).
        public bool Started { get; private set; }

        public SongPreview(MonoBehaviour host, AudioSource source, AudioSource bgm = null)
        {
            this.host = host; this.source = source; this.bgm = bgm;
        }

        public void Play(SongDefinition song, double demoStart)
        {
            Started = true;
            host.StartCoroutine(Load(song, demoStart, ++generation));
        }

        // Selection changes stop the preview; the selection BGM resumes where it was paused.
        public void Stop()
        {
            cancellation?.Cancel();
            generation++;
            ReleaseOnlineSong();
            if (!Started) return;
            Started = false;
            source.StopAudio();
            ResumeBgm();
        }

        void PauseBgm()
        {
            if (bgm == null || bgmPaused || !bgm.IsAudioPlaying()) return;
            bgm.PauseAudio(); bgmPaused = true;
        }

        void ResumeBgm()
        {
            if (!bgmPaused) return;
            bgmPaused = false;
            if (bgm != null && host != null && host.isActiveAndEnabled) bgm.PlayAudio();
        }

        void ReleaseOnlineSong()
        {
            if (onlineSong == null) return;
            if (source != null) source.GetComponent<AudioBus>()?.Release();
            if (source != null) source.clip = null;
            UnityEngine.Object.Destroy(onlineSong); onlineSong = null;
        }

        // Runs while this play is current: fades the preview out over its last second, then
        // restarts it from its start under a fade-in. The BGM never sounds in between.
        IEnumerator Loop(double start, int generation)
        {
            double restartedAt = -1;
            while (generation == this.generation)
            {
                double now = Time.realtimeSinceStartupAsDouble, length = source.AudioLength();
                if (length <= 0) { yield return null; continue; }
                if (restartedAt >= 0 && now - restartedAt < FadeInSeconds)
                    source.SetAudioFade((float)((now - restartedAt) / FadeInSeconds));
                else
                {
                    double remaining = length - source.AudioPosition();
                    if (remaining <= EndSlack || !source.IsAudioPlaying())
                    {
                        source.SetAudioFade(0);
                        source.SeekAudio(start);
                        source.PlayAudio();
                        restartedAt = now;
                    }
                    else source.SetAudioFade((float)Math.Min(1, remaining / FadeOutSeconds));
                }
                yield return null;
            }
        }

        // An online song's preview file comes from its server (cached by content) and starts at
        // its beginning; a local song decodes its own audio and starts at DEMOSTART.
        IEnumerator Load(SongDefinition song, double demoStart, int generation)
        {
            var engine = AudioEngine.EnsureInstance();
            var online = Online.OnlineManager.Instance;
            bool remote = online?.IsOnline(song) == true;
            if (!remote && !engine.Available) yield break;
            var cancellation = this.cancellation = new CancellationTokenSource();
            bool Stale() => generation != this.generation || cancellation.IsCancellationRequested;
            try
            {
                Func<byte[]> read;
                if (remote)
                {
                    var download = online.Client.PreparePreviewAsync(online.ChartOf(song), cancellation.Token);
                    while (!download.IsCompleted) yield return null;
                    if (Stale()) yield break;
                    if (!download.IsCompletedSuccessfully)
                    {
                        Log.Warn("Preview", "Online preview: " + download.Exception?.GetBaseException().Message);
                        yield break;
                    }
                    string file = download.Result;
                    read = () => System.IO.File.ReadAllBytes(file);
                }
                else
                {
                    byte[] bytes = null;
                    Exception readError = null;
                    try { if (string.IsNullOrEmpty(song.audioPath)) bytes = AudioAssetCatalog.Read(song.music); }
                    catch (Exception error) { readError = error; }
                    if (readError != null) { Log.Warn("Preview", "Preview audio: " + readError.Message); Stop(); yield break; }
                    string path = song.audioPath;
                    read = () => bytes ?? System.IO.File.ReadAllBytes(path);
                }
                if (!engine.Available) yield break;
                using (var decode = new BackgroundAudioDecode(read, engine, false, cancellation.Token))
                {
                    while (!decode.IsDone) yield return null;
                    if (Stale()) yield break;
                    if (!decode.Succeeded)
                    {
                        Log.Warn("Preview", "Preview audio: " + decode.Error?.Message);
                        if (!remote) Stop();
                        yield break;
                    }
                    if (remote && engine.Generation != decode.Generation) yield break;
                    var target = song;
                    if (remote)
                    {
                        ReleaseOnlineSong();
                        target = onlineSong = ScriptableObject.CreateInstance<SongDefinition>();
                        onlineSong.hideFlags = HideFlags.DontSave;
                    }
                    target.SetPreparedAudio(decode.Take());
                    source.SetAudioSong(target, false);
                    double start = remote ? 0 : Math.Clamp(demoStart, 0, Math.Max(0, source.AudioLength() - 0.1));
                    source.SeekAudio(start);
                    source.PlayAudio();
                    PauseBgm();
                    yield return Loop(start, generation);
                }
            }
            finally
            {
                cancellation.Cancel();
                if (this.cancellation == cancellation) this.cancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
