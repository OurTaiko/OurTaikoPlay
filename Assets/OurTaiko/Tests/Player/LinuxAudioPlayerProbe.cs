#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR && OURTAIKO_AUDIO_PROBE
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using ManagedBass;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // Explicit validation build only; absent from normal Player builds. Data lives beside the
    // isolated validation Player, never in an installed game's directory.
    public sealed class LinuxAudioPlayerProbe : MonoBehaviour
    {
        [Serializable] public sealed class Item { public string resource, hash; public float length; public int samples, frequency, channels; }
        [Serializable] public sealed class Manifest { public Item[] items; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void IsolateOnline() => OnlineManager.EnsureInstance().UseUnsaved(Path.Combine(GamePaths.Root, "probe-cache"));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartProbe() => new GameObject(nameof(LinuxAudioPlayerProbe)).AddComponent<LinuxAudioPlayerProbe>();
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("PLAYER AUDIO CHECK: " + message); }
        void Awake() => DontDestroyOnLoad(gameObject);
        IEnumerator Start()
        {
            try { VerifyCatalog(); }
            catch (Exception error) { Debug.LogException(error); Application.Quit(2); yield break; }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            var entry = FindAnyObjectByType<EntryScene>();
            // Card/server activation after a deliberately busy PCM is asynchronous. Require
            // actual native progress, not a fixed delay or merely AudioEngine.Available.
            double audioReadyBy = GameTimeline.Realtime + 5;
            while (entry != null && entry.bgm != null && (!entry.bgm.IsAudioPlaying() || entry.bgm.AudioPosition() <= 0)
                && GameTimeline.Realtime < audioReadyBy) yield return null;
            var engine = AudioEngine.EnsureInstance();
            NativeAudioSample native = null;
            try
            {
                Check(engine.Available && !engine.Silent, engine.Diagnostics);
                Check(entry != null && entry.bgm.clip != null && entry.don != null && entry.entryStart != null, "Entry clip references lost");
                Check(entry.bgm.IsAudioPlaying() && entry.bgm.AudioPosition() > 0, "Menu BGM did not advance");
                Check(entry.bgm.GetComponent<AudioBus>().MainOutputVolume > 0, "Menu BGM gain lost with Unity output disabled");
                entry.sfx.PlayAudioOneShot(entry.don);
                entry.voice.PlayAudioOneShot(entry.entryStart, AudioGroup.Voice);
                Check(entry.sfx.GetComponent<AudioBus>().EffectPlays > 0 && entry.voice.GetComponent<AudioBus>().EffectPlays > 0, "Effects/voice not started");
                foreach (var bus in new[] { entry.sfx.GetComponent<AudioBus>(), entry.voice.GetComponent<AudioBus>() })
                {
                    var effects = (System.Collections.IDictionary)typeof(AudioBus).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bus);
                    bool audible = false;
                    foreach (NativeAudioSample effect in effects.Values) audible |= effect.Playing && effect.OutputVolume > 0;
                    Check(audible, "Effects/voice gain lost with Unity output disabled");
                }
                native = new NativeAudioSample(AudioAssetCatalog.Read(entry.bgm.clip), engine, false);
                native.Play(.1f, false);
            }
            catch (Exception error) { native?.Dispose(); Debug.LogException(error); Application.Quit(2); yield break; }
            int peak = 0;
            for (int i = 0; i < 20; i++)
            {
                int stream = (int)typeof(NativeAudioSample).GetField("stream", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(native);
                int level = Bass.ChannelGetLevel(stream);
                if (level >= 0) peak = Math.Max(peak, Math.Max(level & 65535, (level >> 16) & 65535));
                yield return new WaitForSecondsRealtime(.025f);
            }
            bool progressed = native.Position > .2 && native.Playing;
            native.Dispose();
            try { Check(progressed && peak > 0, "Decoded BASS PCM is silent or not advancing"); Debug.Log("PLAYER AUDIO PCM PASS peak=" + peak); }
            catch (Exception error) { Debug.LogException(error); Application.Quit(2); yield break; }

            var go = new GameObject("Validation song");
            var source = go.AddComponent<AudioSource>();
            source.volume = .01f;
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.music = entry.bgm.clip;
            source.SetAudioSong(song);
            double start = GameTimeline.AudioNow + .1;
            source.PlayAudioScheduled(start);
            yield return new WaitForSecondsRealtime(.45f);
            try
            {
                Check(source.IsAudioPlaying() && source.AudioPosition() > .15, "Song track/scheduled clock did not advance");
                Check(source.GetComponent<AudioBus>().MainOutputVolume > 0, "Song gain lost with Unity output disabled");
                Check(Math.Abs(source.AudioLength() - song.music.length) < .1, "Native duration / AudioClip metadata mismatch");
                Debug.Log("PLAYER AUDIO SONG PASS position=" + source.AudioPosition());
            }
            catch (Exception error) { Debug.LogException(error); Application.Quit(2); yield break; }
            Destroy(go); Destroy(song);
            var movieGo = new GameObject("Validation movie");
            var movie = movieGo.AddComponent<FFmpegMoviePlayer>();
            bool texture = false; string failure = null;
            movie.TextureChanged += value => texture |= value != null && value.width > 0;
            movie.Failed += message => failure = message;
            movie.Open(Path.Combine(GamePaths.Root, "testsrc.mp4"));
            double deadline = GameTimeline.Realtime + 15;
            while (!movie.IsPrepared && failure == null && GameTimeline.Realtime < deadline) yield return null;
            if (movie.IsPrepared) movie.Play();
            while (movie.Position < .2 && failure == null && GameTimeline.Realtime < deadline) yield return null;
            try
            {
                Check(movie.IsPrepared && movie.Position >= .2 && texture && failure == null, "FFmpeg video failed: " + failure);
                Debug.Log("PLAYER AUDIO VIDEO PASS");
            }
            catch (Exception error) { Debug.LogException(error); Application.Quit(2); yield break; }
            movie.Close(); Destroy(movieGo);
            Debug.Log("PLAYER AUDIO CHECKS PASS");
            Application.Quit(0);
        }

        static void VerifyCatalog()
        {
            var manifest = JsonUtility.FromJson<Manifest>(Resources.Load<TextAsset>("LinuxAudioProbeManifest").text);
            var catalog = Resources.Load<AudioAssetCatalog>("NativeAudioCatalog");
            Check(catalog != null && catalog.entries.Length == manifest.items.Length && catalog.entries.Length > 0, "Catalog missing/changed");
            using var hash = SHA256.Create();
            foreach (var item in manifest.items)
            {
                AudioClip clip = null;
                foreach (var entry in catalog.entries) if (entry.resource == item.resource) clip = entry.clip;
                Check(clip != null, "AudioClip stripped: " + item.resource);
                Check(clip.samples == item.samples && clip.frequency == item.frequency && clip.channels == item.channels
                    && Math.Abs(clip.length - item.length) < .001, "Metadata changed: " + item.resource);
                string actual = BitConverter.ToString(hash.ComputeHash(AudioAssetCatalog.Read(clip)));
                Check(actual == item.hash, "Original encoded bytes changed: " + item.resource);
            }
            Debug.Log("PLAYER AUDIO CATALOG PASS count=" + manifest.items.Length);
        }
    }
}
#endif
