using System;
using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    public enum AudioGroup { Effects, Bgm, Track, Drum, Voice }
    public enum SoundPlatform { Desktop, Windows, Android, IOS }

    [Serializable]
    public sealed class SoundVolumes
    {
        // Multipliers preserve the authored mix when loading an older settings file.
        public float master = 1, bgm = 1, track = 1, drum = 1, effects = 1, voice = 1;
        static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp(value, 0, 2);
        public float Gain(AudioGroup group) => Safe(master) * Safe(group switch
        {
            AudioGroup.Bgm => bgm, AudioGroup.Track => track, AudioGroup.Drum => drum,
            AudioGroup.Voice => voice, _ => effects,
        });
    }

    public static class SoundSettings
    {
        public static SoundPlatform Platform
        {
            get
            {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                return SoundPlatform.Windows;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return SoundPlatform.Android;
#elif UNITY_IOS && !UNITY_EDITOR
                return SoundPlatform.IOS;
#else
                return SoundPlatform.Desktop;
#endif
            }
        }
        static SettingItem Volume(string name, string description, Func<SoundVolumes, float> get, Action<SoundVolumes, float> set)
        {
            var choices = new string[41];
            for (int i = 0; i < choices.Length; i++) choices[i] = (i * 5) + "%";
            return new SettingItem(name, description + " Applies immediately; 0% mutes this group.", choices,
                s => Mathf.Clamp(Mathf.RoundToInt(get(s.audio.volume) * 20), 0, 40),
                (s, i) => set(s.audio.volume, i / 20f));
        }

        const string ApplyOnExit = " Applies when leaving settings.";

        static SettingItem Number(string label, string description, int defaultValue, int min, int max, int step, string unit,
            Func<AudioOptions, int> get, Action<AudioOptions, int> set, string zeroLabel = null)
            => SettingItem.Number(label, description + ApplyOnExit, defaultValue, step, unit,
                s => Math.Clamp(get(s.audio), min, max), (s, v) => set(s.audio, Math.Clamp(v, min, max)), min, max,
                v => v == 0 && zeroLabel != null ? zeroLabel : (v + " " + unit).TrimEnd());

        // Store seconds as before; edit in 0.1 ms increments.
        static SettingItem Milliseconds(string label, string description, int defaultValue, int min, int max,
            Func<AudioOptions, float> get, Action<AudioOptions, float> set)
            => SettingItem.Number(label, description + ApplyOnExit, defaultValue, 1, "ms",
                s => Math.Clamp(Mathf.RoundToInt(get(s.audio) * 10000), min, max),
                (s, v) => set(s.audio, Math.Clamp(v, min, max) / 10000f), min, max,
                v => v == 0 ? "System Default" : (v / 10m).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");

        static SettingItem Toggle(string label, string description, Func<AudioOptions, bool> get, Action<AudioOptions, bool> set)
            => SettingItem.Toggle(label, description + ApplyOnExit, s => get(s.audio), (s, v) => set(s.audio, v));

        public static IReadOnlyList<SettingItem> Catalog(SoundPlatform platform, AudioBackend backend = AudioBackend.Automatic)
        {
            var backends = platform == SoundPlatform.Windows
                // The cross-platform BassSimple output comes last.
                ? new[] { AudioBackend.Automatic, AudioBackend.BassWASAPI, AudioBackend.BassASIO, AudioBackend.BassSimple }
                : new[] { AudioBackend.Automatic, AudioBackend.BassSimple };
            var items = new List<SettingItem>
            {
                Volume("Master Volume", "Overall volume.", v => v.master, (v, n) => v.master = n),
                Volume("BGM Volume", "Music in menus and the result screen.", v => v.bgm, (v, n) => v.bgm = n),
                Volume("Track Volume", "Song playback and song previews.", v => v.track, (v, n) => v.track = n),
                Volume("Drum Volume", "Don and ka during gameplay and drum-sound previews.", v => v.drum, (v, n) => v.drum = n),
                Volume("Effects Volume", "Menu feedback, balloon pops and result effects.", v => v.effects, (v, n) => v.effects = n),
                Volume("Voice Volume", "Entry, song selection, combo and result voices.", v => v.voice, (v, n) => v.voice = n),
                new SettingItem("Output Backend", "Automatic: WASAPI on Windows, BassSimple elsewhere. ASIO requires a driver. Confirm to show backend settings; changes apply on exit.",
                Array.ConvertAll(backends, b => b.ToString()),
                s => Math.Max(0, Array.IndexOf(backends, s.audio.backend)), (s,i) => s.audio.backend = backends[i]),
            };
            var selected = backend == AudioBackend.Automatic
                ? (platform == SoundPlatform.Windows ? AudioBackend.BassWASAPI : AudioBackend.BassSimple) : backend;
            if (selected == AudioBackend.BassSimple)
            {
                items.Add(Number("Audio Output Period", "Shorter periods reduce delay but may crackle. "
                    + (platform == SoundPlatform.Windows ? "Windows controls the actual period and ignores this request." : "The device may choose a different period."),
                    0, 0, 100, 1, "ms", a => a.devicePeriodMs, (a, v) => a.devicePeriodMs = v,
                    $"Default ({new AudioOptions().Period(platform)} ms)"));
                items.Add(Number("Device Buffer", "0 = default; other values are raised to at least twice the output period. "
                    + (platform == SoundPlatform.IOS || platform == SoundPlatform.Desktop ? "macOS and iOS ignore this request." : "The system may choose a different buffer."),
                    0, 0, 1000, 1, "ms", a => a.deviceBufferMs, (a, v) => a.deviceBufferMs = v, "Default"));
                if (platform == SoundPlatform.Android)
                    items.Add(Toggle("Android AAudio", "Use AAudio output when available.", a => a.androidAAudio, (a, v) => a.androidAAudio = v));
            }
            else if (selected == AudioBackend.BassWASAPI)
            {
                items.Add(Toggle("WASAPI Exclusive", "Request exclusive access to the output device. Falls back to shared mode if unavailable.", a => a.wasapiExclusive, (a, v) => a.wasapiExclusive = v));
                items.Add(Toggle("WASAPI Raw Mode", "Request output without Windows audio enhancements. Falls back if unsupported.", a => a.wasapiRaw, (a, v) => a.wasapiRaw = v));
                items.Add(Toggle("WASAPI Async Mode", "Prefill audio to reduce dropouts at the cost of extra delay. Used in exclusive mode only.", a => a.wasapiAsync, (a, v) => a.wasapiAsync = v));
                items.Add(Milliseconds("WASAPI Buffer", "Buffer per block in exclusive mode (two blocks). Shared mode uses its own buffer.",
                    60, 50, 5000, a => a.wasapiBufferSeconds, (a, v) => a.wasapiBufferSeconds = v));
                items.Add(Milliseconds("WASAPI Period", "Shared-mode period; 0 = system default. Ignored in exclusive mode.",
                    0, 0, 1000, a => a.wasapiPeriodSeconds, (a, v) => a.wasapiPeriodSeconds = v));
            }
            else if (selected == AudioBackend.BassASIO)
            {
                items.Add(Number("ASIO Device Index", "Driver index, starting at 0. Requires an installed ASIO driver.",
                    0, 0, int.MaxValue, 1, "", a => a.asioDevice, (a, v) => a.asioDevice = v));
                items.Add(Number("ASIO Buffer", "Buffer length in samples; 0 = driver default. Driver-supported sizes only.",
                    0, 0, 65536, 1, "samples", a => a.asioBufferSamples, (a, v) => a.asioBufferSamples = v, "Driver Default"));
            }
            items.Add(Number("Sample Rate", selected == AudioBackend.BassWASAPI
                ? "BASS initialization rate. WASAPI output uses the device's negotiated format."
                : "Requested sample rate. The output device may use a different rate.",
                44100, 8000, 192000, 100, "Hz", a => a.Rate, (a, v) => a.sampleRate = v));
            items.Add(Number("BASS Stream Update Period", "Stream buffering is disabled during playback; this does not control the output-device period.",
                100, 5, 100, 1, "ms", a => a.updatePeriodMs, (a, v) => a.updatePeriodMs = v));
            items.Add(Number("BASS Stream Buffer", "At least 1 ms above the stream update period. Playback streams disable this buffer.",
                1000, 6, 5000, 1, "ms", a => a.playbackBufferMs, (a, v) => a.playbackBufferMs = v));
            return items;
        }
    }
}
