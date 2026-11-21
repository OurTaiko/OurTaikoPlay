using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Aac;
using ManagedBass.Opus;
using ManagedBass.Fx;
using ManagedBass.Mix;

namespace OurTaiko
{
    // Port of TeamMajdata's BassHelper, BassSimpleAudioSample and BassAudioSample.
    // Owns the encoded file, never an AudioClip-derived PCM copy.
    public sealed class NativeAudioSample : IDisposable
    {
        static readonly HashSet<NativeAudioSample> samples = new();
        public bool IsDisposed { get; private set; }
        internal static void ReleaseAll()
        {
            lock (AudioEngine.DeviceLock)
                foreach (var sample in new List<NativeAudioSample>(samples)) sample.Dispose();
        }
        static int liveStreams;
        public static int LiveStreams => System.Threading.Volatile.Read(ref liveStreams);
        public int EncodedBytes { get; private set; }
        public double Length { get; private set; }
        // What the decode measured (Unmeasured unless normalize); a caller may put a better
        // measurement of the same song here (an online preview takes its full song's, AudioLevelStore).
        public AudioLevel Level { get; set; } = AudioLevel.Unmeasured;
        // Set from the player's setting (AudioOptions.balanceLoudness) by the bus that plays it.
        public bool BalanceLoudness { get; set; }
        public float Gain => Level.Gain(BalanceLoudness);
        public string Format { get; private set; }
        GCHandle data;
        int stream, decode, resampler;
        readonly bool mixed;
        bool disposed => IsDisposed;
        readonly bool speedChange;
        public bool Playing => !disposed && Bass.ChannelIsActive(stream) == PlaybackState.Playing
            && (!mixed || !BassMix.ChannelHasFlag(stream, BassFlags.MixerChanPause));
        public double Position => disposed ? 0 : Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetPosition(stream));
        static int Open(IntPtr address, long size, BassFlags flags)
        {
            int handle = Bass.CreateStream(address, 0, size, flags);
            if (handle == 0 && Bass.LastError == Errors.FileFormat)
                handle = BassOpus.CreateStream(address, 0, size, flags);
#if UNITY_ANDROID || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX
            if (handle == 0 && Bass.LastError == Errors.FileFormat)
                handle = BassAac.CreateStream(address, 0, size, flags);
#endif
            if (handle == 0) throw new InvalidOperationException("BASS decode: " + Bass.LastError);
            System.Threading.Interlocked.Increment(ref liveStreams);
            return handle;
        }
        static void Check(bool success, string action)
        {
            if (!success) throw new InvalidOperationException(action + ": " + Bass.LastError);
        }
        public NativeAudioSample(byte[] encoded, AudioEngine engine, bool normalize = true, bool speedChange = false, int? generation = null)
        {
            lock (AudioEngine.DeviceLock)
            {
                if ((generation.HasValue && generation != engine.Generation) || !engine.Available)
                    throw new OperationCanceledException("Audio output changed during preparation");
                if (encoded == null || encoded.Length == 0) throw new ArgumentException("Empty audio file");
                mixed = engine.Backend == AudioBackend.BassWASAPI || engine.Backend == AudioBackend.BassASIO;
                this.speedChange = speedChange;
                EncodedBytes = encoded.Length;
                data = GCHandle.Alloc(encoded, GCHandleType.Pinned);
                try
                {
                    decode = Open(data.AddrOfPinnedObject(), encoded.LongLength, BassFlags.Decode | BassFlags.Prescan | BassFlags.AsyncFile);
                    Format = Bass.ChannelGetInfo(decode).ChannelType.ToString();
                    if (normalize) Level = Measure(data.AddrOfPinnedObject(), encoded.LongLength);
                    if (speedChange)
                    {
                        stream = BassFx.TempoCreate(decode, mixed ? BassFlags.Decode : BassFlags.Default);
                        if (stream == 0) throw new InvalidOperationException("BASS FX: " + Bass.LastError);
                        System.Threading.Interlocked.Increment(ref liveStreams);
                    }
                    else stream = mixed ? decode : Open(data.AddrOfPinnedObject(), encoded.LongLength, BassFlags.Prescan | BassFlags.AsyncFile);
                    // Decode-only channels have no playback buffer; BASS may return BASS_ERROR_ILLTYPE.
                    if (mixed) Bass.ChannelSetAttribute(stream, ChannelAttribute.Buffer, 0);
                    else Check(Bass.ChannelSetAttribute(stream, ChannelAttribute.Buffer, 0), "Disable stream buffering");
                    Length = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
                    if (mixed)
                    {
                        int rate = (int)Bass.ChannelGetAttribute(engine.Mixer, ChannelAttribute.Frequency);
                        resampler = BassMix.CreateMixerStream(rate, 2, BassFlags.Decode | BassFlags.Float);
                        if (resampler == 0) throw new InvalidOperationException("BASS resampler: " + Bass.LastError);
                        System.Threading.Interlocked.Increment(ref liveStreams);
                        Bass.ChannelSetAttribute(resampler, ChannelAttribute.Buffer, 0);
                        Check(BassMix.MixerAddChannel(resampler, stream, BassFlags.MixerChanPause), "Attach sample");
                        Check(BassMix.MixerAddChannel(engine.Mixer, resampler, BassFlags.MixerChanMatrix), "Attach resampler");
                        Check(BassMix.ChannelSetMatrix(resampler, engine.MixingMatrix), "Set output matrix");
                    }
                    else Bass.ChannelStop(stream);
                }
                catch { Dispose(); throw; }
                samples.Add(this);
            }
        }
        // Decodes the whole file once more as float, apart from the playing channels, for its
        // loudness and peak; the samples are clamped to full scale as the 16-bit playback decode clips them.
        static AudioLevel Measure(IntPtr address, long size)
        {
            int channel = Open(address, size, BassFlags.Decode | BassFlags.Float);
            try
            {
                var info = Bass.ChannelGetInfo(channel);
                int channels = Math.Max(1, info.Channels);
                var meter = new LoudnessMeter(info.Frequency, channels);
                var buffer = new float[16384 / channels * channels];
                int read;
                while ((read = Bass.ChannelGetData(channel, buffer, buffer.Length * sizeof(float))) > 0)
                {
                    int count = read / sizeof(float);
                    for (int i = 0; i < count; i++) buffer[i] = Math.Clamp(buffer[i], -1f, 1f);
                    meter.Add(buffer, count);
                }
                return new AudioLevel(meter.Peak, meter.Integrated());
            }
            finally { Free(channel); }
        }
        public float OutputVolume => disposed ? 0 : (float)Bass.ChannelGetAttribute(stream, ChannelAttribute.Volume);
        public void SetVolume(float volume)
        {
            if (!disposed) Check(Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, Math.Max(0, volume) * Gain), "Set volume");
        }
        public void Play(float volume, bool loop, double position = 0, float speed = 1)
        {
            if (disposed) throw new ObjectDisposedException(nameof(NativeAudioSample));
            SetVolume(volume);
            Bass.ChannelFlags(stream, loop ? BassFlags.Loop : BassFlags.Default, BassFlags.Loop);
            if (speedChange) Bass.ChannelSetAttribute(stream, ChannelAttribute.Tempo, (speed - 1) * 100);
            if (mixed)
            {
                Check(BassMix.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, position)), "Seek sample");
                BassMix.ChannelRemoveFlag(stream, BassFlags.MixerChanPause);
            }
            else
            {
                Check(Bass.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, position)), "Seek sample");
                Check(Bass.ChannelPlay(stream), "Play sample");
            }
        }
        public void Stop()
        {
            if (disposed || stream == 0) return;
            if (mixed) BassMix.ChannelAddFlag(stream, BassFlags.MixerChanPause);
            else Bass.ChannelStop(stream);
            Bass.ChannelSetPosition(stream, 0);
        }
        static void Free(int handle)
        {
            if (handle == 0) return;
            Bass.StreamFree(handle); System.Threading.Interlocked.Decrement(ref liveStreams);
        }
        public void Dispose()
        {
            lock (AudioEngine.DeviceLock)
            {
                if (disposed) return;
                Stop(); IsDisposed = true;
                samples.Remove(this);
                if (resampler != 0) BassMix.MixerRemoveChannel(resampler);
                Free(resampler); Free(stream);
                if (decode != stream) Free(decode);
                stream = decode = resampler = 0;
                if (data.IsAllocated) data.Free();
            }
        }
    }
}
