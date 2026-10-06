using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ManagedBass;
using ManagedBass.Mix;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using System.Runtime.InteropServices;
using ManagedBass.Wasapi;
using ManagedBass.Asio;
using AOT;
#endif

namespace OurTaiko
{
    // One BASS output for the application. Scene AudioSources only keep their authored clip/volume;
    // all playback goes through AudioBus. When no device opens, BASS's "No Sound" device keeps
    // streams and positions running silently.
    public sealed class AudioEngine : MonoBehaviour
    {
        public static AudioEngine Instance { get; private set; }
        public AudioBackend Backend { get; private set; } = AudioBackend.Bass;
        public string Diagnostics { get; private set; }
        // Why the requested output could not open; null while it plays through a device.
        public string Failure { get; private set; }
        public bool Silent { get; private set; }
        // False only when the BASS library itself cannot initialize; playback calls then do nothing.
        public bool Available { get; private set; }
        // Test seam: makes every device fail so the silent output is exercised without hardware.
        public static bool SimulateDeviceFailure { get; set; }
        public int Mixer { get; private set; }
        public float[,] MixingMatrix { get; private set; }
        AudioOptions appliedOptions;
        public bool HasPendingDeviceChanges => appliedOptions != null && !appliedOptions.SameDeviceSettings(SettingManager.EnsureInstance().Settings.audio);
        internal static readonly object DeviceLock = new object();
        public int Generation { get; private set; }
        bool applying;
        bool initialized;
        bool nativeInitialized;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        static readonly WasapiProcedure WasapiCallback = ReadWasapi;
        bool wasapiInitialized, asioInitialized;
        static int callbackMixer;
        [MonoPInvokeCallback(typeof(WasapiProcedure))]
        static int ReadWasapi(IntPtr buffer, int length, IntPtr user)
        {
            int mixer = System.Threading.Volatile.Read(ref callbackMixer);
            return mixer == 0 ? 0 : Math.Max(0, Bass.ChannelGetData(mixer, buffer, length));
        }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();
        public static AudioEngine EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<AudioEngine>();
            if (existing != null) { existing.Initialize(); return existing; }
            return new GameObject(nameof(AudioEngine)).AddComponent<AudioEngine>();
        }
        void Awake() => Initialize();
        void Initialize()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (initialized) return;
            initialized = true;
            DontDestroyOnLoad(gameObject);
            appliedOptions = SettingManager.EnsureInstance().Settings.Clone().audio;
            InitializeOutput(appliedOptions, true);
        }
        void InitializeOutput(AudioOptions options, bool allowFallback)
        {
            Backend = AudioBackend.Bass; Failure = null; Silent = false; Available = false;
            var backend = options.Backend;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Bass.Configure(Configuration.AndroidAAudio, options.androidAAudio);
#endif
                Bass.Configure(Configuration.UpdatePeriod, Math.Clamp(options.updatePeriodMs, 5, 100));
                Bass.Configure(Configuration.PlaybackBufferLength, Math.Clamp(options.playbackBufferMs, Math.Clamp(options.updatePeriodMs, 5, 100) + 1, 5000));
                Bass.Configure(Configuration.DevicePeriod, options.Period(Application.isMobilePlatform));
                Bass.Configure(Configuration.DeviceBufferLength, options.Buffer(Application.isMobilePlatform));
                Bass.Configure(Configuration.DevNonStop, true);
            }
            catch (Exception error)
            {
                // DllNotFoundException and friends: there is no BASS at all.
                if (!allowFallback) throw;
                Failure = error.Message;
                Diagnostics = "No audio output: " + Failure;
                UnityEngine.Debug.LogError("[Audio] " + Diagnostics);
                return;
            }
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (backend != AudioBackend.Bass)
            {
                try
                {
                    if (backend == AudioBackend.Asio) InitAsio(options);
                    else InitWasapi(options);
                }
                catch (Exception error)
                {
                    FreeNative();
                    if (!allowFallback && backend != AudioBackend.Automatic) throw;
                    Failure = error.Message;
                }
            }
#else
            if (!allowFallback && (backend == AudioBackend.Wasapi || backend == AudioBackend.Asio))
                throw new PlatformNotSupportedException("This audio backend requires Windows");
#endif
            if (Mixer == 0)
            {
                try { InitBass(options, -1); }
                catch (Exception error)
                {
                    FreeNative();
                    if (!allowFallback) throw;
                    Failure = Failure == null ? error.Message : Failure + "; " + error.Message;
                    try { InitBass(options, Bass.NoSoundDevice); Silent = true; }
                    catch (Exception silent)
                    {
                        FreeNative();
                        Failure += "; " + silent.Message;
                    }
                }
            }
            if (!Available)
            {
                Diagnostics = "No audio output: " + Failure;
                UnityEngine.Debug.LogError("[Audio] " + Diagnostics);
                return;
            }
            Diagnostics = (Silent ? "Silent (no device)" : Backend.ToString()) + $"; {options.Rate} Hz requested; device period {options.Period(Application.isMobilePlatform)} ms requested; buffer {options.Buffer(Application.isMobilePlatform)} ms requested; stream buffering disabled";
            if (Failure != null) Diagnostics += "; fallback: " + Failure;
            if (Silent) UnityEngine.Debug.LogWarning("[Audio] " + Diagnostics);
            else UnityEngine.Debug.Log("[Audio] " + Diagnostics);
        }
        static void Check(bool success, string operation)
        {
            if (!success) throw new InvalidOperationException(operation + ": " + Bass.LastError);
        }
        void InitBass(AudioOptions options, int device)
        {
            if (SimulateDeviceFailure && device != Bass.NoSoundDevice) throw new InvalidOperationException("BASS device initialization: simulated failure");
            Check(Bass.Init(device, options.Rate), "BASS device initialization");
            nativeInitialized = true;
            Backend = AudioBackend.Bass;
            Available = true;
        }
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        void InitWasapi(AudioOptions options)
        {
            if (SimulateDeviceFailure) throw new InvalidOperationException("WASAPI initialization: simulated failure");
            Check(Bass.Init(Bass.NoSoundDevice, options.Rate), "BASS decode device");
            nativeInitialized = true;
            var combinations = new[] { (true, true), (true, false), (false, true), (false, false) };
            bool attempt = false;
            foreach (var pair in combinations)
            {
                if (pair == (options.wasapiExclusive, options.wasapiRaw)) attempt = true;
                if (!attempt) continue;
                var flags = WasapiInitFlags.EventDriven;
                if (pair.Item1) flags |= WasapiInitFlags.Exclusive | (options.wasapiAsync ? WasapiInitFlags.Async : 0);
                if (pair.Item2) flags |= WasapiInitFlags.Raw;
                if (BassWasapi.Init(-1, 0, 0, flags,
                    pair.Item1 ? Mathf.Clamp(options.wasapiBufferSeconds, 0.005f, 0.5f) : 0,
                    pair.Item1 && options.wasapiPeriodSeconds > 0 ? Mathf.Clamp(options.wasapiPeriodSeconds, 0.001f, 0.1f) : 0, WasapiCallback))
                {
                    wasapiInitialized = true;
                    break;
                }
                BassWasapi.Free();
            }
            if (!wasapiInitialized) throw new InvalidOperationException("WASAPI exclusive/shared initialization failed");
            Check(BassWasapi.GetInfo(out var info), "WASAPI format");
            Mixer = BassMix.CreateMixerStream(info.Frequency, info.Channels, BassFlags.Float | BassFlags.Decode | BassFlags.MixerNonStop);
            Check(Mixer != 0, "WASAPI mixer");
            Bass.ChannelSetAttribute(Mixer, ChannelAttribute.Buffer, 0);
            Bass.ChannelSetAttribute(Mixer, (ChannelAttribute)86017, 8);
            MixingMatrix = CreateMixingMatrix(Bass.ChannelGetInfo(Mixer).Channels);
            System.Threading.Volatile.Write(ref callbackMixer, Mixer);
            Check(BassWasapi.Start(), "Start WASAPI");
            Backend = AudioBackend.Wasapi;
            Available = true;
        }
        void InitAsio(AudioOptions options)
        {
            if (SimulateDeviceFailure) throw new InvalidOperationException("ASIO initialization: simulated failure");
            Check(Bass.Init(Bass.NoSoundDevice, options.Rate), "BASS decode device");
            nativeInitialized = true;
            if (!BassAsio.Init(options.asioDevice, AsioInitFlags.Thread)) throw new InvalidOperationException("ASIO initialization: " + BassAsio.LastError);
            asioInitialized = true;
            BassAsio.Rate = options.Rate;
            Mixer = BassMix.CreateMixerStream((int)BassAsio.Rate, BassAsio.Info.Outputs, BassFlags.Float | BassFlags.Decode | BassFlags.MixerNonStop);
            Check(Mixer != 0, "ASIO mixer");
            Bass.ChannelSetAttribute(Mixer, ChannelAttribute.Buffer, 0);
            Bass.ChannelSetAttribute(Mixer, (ChannelAttribute)86017, 8);
            MixingMatrix = CreateMixingMatrix(Bass.ChannelGetInfo(Mixer).Channels);
            BassAsio.ChannelEnableBass(false, 0, Mixer, true);
            BassAsio.ChannelSetFormat(false, 0, AsioSampleFormat.Float);
            BassAsio.ChannelJoin(false, 1, 0);
            BassAsio.ChannelSetFormat(false, 1, AsioSampleFormat.Float);
            if (!BassAsio.Start(Math.Max(0, options.asioBufferSamples)))
                throw new InvalidOperationException("ASIO stereo output: " + BassAsio.LastError);
            Backend = AudioBackend.Asio;
            Available = true;
        }
#endif
        // Called under the settings scene's closed transition, before the next scene loads.
        // Native preparation holds the same lock; wait without blocking the main thread.
        public async Task ApplyPendingSettingsAsync()
        {
            if (!HasPendingDeviceChanges) return;
            if (applying) throw new InvalidOperationException("Audio settings are already being applied");
            applying = true;
            bool entered = false;
            try
            {
                while (!(entered = Monitor.TryEnter(DeviceLock)))
                    await Awaitable.NextFrameAsync(destroyCancellationToken);
                var manager = SettingManager.EnsureInstance();
                var requested = manager.Settings.Clone().audio;
                var previous = appliedOptions;
                Generation++;
                foreach (var bus in FindObjectsByType<AudioBus>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                { bus.Stop(); bus.Release(); }
                NativeAudioSample.ReleaseAll();
                FreeNative();
                try
                {
                    InitializeOutput(requested, false);
                    appliedOptions = requested;
                }
                catch (Exception error)
                {
                    FreeNative();
                    InitializeOutput(previous, true);
                    // Keep live volume changes while restoring the last usable device configuration.
                    var restored = manager.Settings.Clone();
                    previous.volume = restored.audio.volume;
                    restored.audio = previous;
                    manager.Set(restored);
                    throw new InvalidOperationException("Could not apply audio settings; restored " + Backend + ": " + error.Message, error);
                }
            }
            finally
            {
                if (entered) Monitor.Exit(DeviceLock);
                applying = false;
            }
        }
        public static float[,] CreateMixingMatrix(int channels)
        {
            var matrix = new float[channels, 2];
            if (channels == 1) { matrix[0, 0] = matrix[0, 1] = 0.5f; return matrix; }
            for (int row = 0; row < channels; row++) matrix[row, row % 2] = 1;
            if (channels == 3) { matrix[1, 0] = matrix[1, 1] = 0.5f; matrix[2, 0] = 0; matrix[2, 1] = 1; }
            return matrix;
        }
        void OnApplicationPause(bool paused)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (!Available) return;
            Bass.GlobalMusicVolume = paused ? 0 : 10000;
            Bass.GlobalSampleVolume = paused ? 0 : 10000;
            Bass.GlobalStreamVolume = paused ? 0 : 10000;
#endif
        }
        void FreeNative()
        {
            Available = false;
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (wasapiInitialized) { BassWasapi.Stop(); BassWasapi.Free(); wasapiInitialized = false; }
            if (asioInitialized) { BassAsio.Stop(); BassAsio.Free(); asioInitialized = false; }
            System.Threading.Volatile.Write(ref callbackMixer, 0);
#endif
            if (Mixer != 0) { Bass.StreamFree(Mixer); Mixer = 0; }
            if (nativeInitialized) { Bass.Free(); nativeInitialized = false; }
        }
        void OnDestroy()
        {
            if (Instance != this) return;
            // Release pinned stream memory before unloading the native device, including Play-mode exit.
            foreach (var bus in FindObjectsByType<AudioBus>(FindObjectsInactive.Include, FindObjectsSortMode.None)) bus.Release();
            lock (DeviceLock)
            {
                Generation++;
                NativeAudioSample.ReleaseAll();
                FreeNative();
            }
            Instance = null;
        }
    }
}
