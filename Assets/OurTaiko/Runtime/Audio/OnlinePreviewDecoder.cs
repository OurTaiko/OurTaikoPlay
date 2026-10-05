using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
using ManagedBass;
using ManagedBass.Opus;
#endif

namespace OurTaiko
{
    public static class OnlinePreviewDecoder
    {
        public const double DurationSeconds = 12;
        public static async Task<NativeAudioSample> PrepareAsync(FanmadeEndpoint endpoint, FanmadeChart chart,
            AudioEngine engine, int generation, CancellationToken cancel)
        {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
            using var stream = await PreviewRangeStream.OpenAsync(endpoint, chart, cancel).ConfigureAwait(false);
            for (int attempt = 0; attempt <= PreviewRangeStream.MaxBlocks; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                byte[] wave = DecodeAttempt(stream, chart.DemoStart, engine, generation, cancel);
                if (stream.MissingBlock < 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    return new NativeAudioSample(wave, engine, false, false, generation);
                }
                await stream.FetchMissingAsync().ConfigureAwait(false);
            }
            throw new FanmadeException("PREVIEW_BYTE_BUDGET_EXCEEDED");
#else
            await Task.CompletedTask;
            throw new FanmadeException("PREVIEW_PLATFORM_UNSUPPORTED");
#endif
        }
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
        sealed class Context
        {
            public PreviewRangeStream Stream;
            public byte[] Buffer = new byte[81920];
            public Exception Error;
        }
        static Context State(IntPtr user) => (Context)GCHandle.FromIntPtr(user).Target;
        [AOT.MonoPInvokeCallback(typeof(FileCloseProcedure))] static void Close(IntPtr user) { }
        [AOT.MonoPInvokeCallback(typeof(FileLengthProcedure))] static long Length(IntPtr user) => State(user).Stream.Length;
        [AOT.MonoPInvokeCallback(typeof(FileSeekProcedure))] static bool Seek(long offset, IntPtr user)
        {
            var c = State(user);
            try { c.Stream.Seek(offset, SeekOrigin.Begin); return true; }
            catch (Exception error) { c.Error = error; return false; }
        }
        [AOT.MonoPInvokeCallback(typeof(FileReadProcedure))] static int Read(IntPtr address, int length, IntPtr user)
        {
            var c = State(user);
            try
            {
                int total = 0;
                while (total < length)
                {
                    int n = c.Stream.Read(c.Buffer, 0, Math.Min(length - total, c.Buffer.Length));
                    if (n == 0) break;
                    Marshal.Copy(c.Buffer, 0, IntPtr.Add(address, total), n); total += n;
                }
                return total;
            }
            catch (Exception error) { c.Error = error; return 0; }
        }
        static byte[] DecodeAttempt(PreviewRangeStream input, double demoStart, AudioEngine engine, int generation, CancellationToken cancel)
        {
            // No HTTP under DeviceLock: callbacks read cached blocks, return EOF on a miss, and
            // the outer loop fetches that block before retrying. No network on a playback callback.
            lock (AudioEngine.DeviceLock)
            {
                if (!engine.Native || engine.Generation != generation) throw new OperationCanceledException("Audio output changed");
                input.RestartRead();
                var context = new Context { Stream = input };
                var handle = GCHandle.Alloc(context);
                int decoder = 0;
                try
                {
                    var procedures = new FileProcedures { Close = Close, Length = Length, Read = Read, Seek = Seek };
                    decoder = Bass.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, procedures, GCHandle.ToIntPtr(handle));
                    if (decoder == 0 && input.MissingBlock < 0 && Bass.LastError == Errors.FileFormat)
                    {
                        input.RestartRead();
                        decoder = BassOpus.CreateStream(StreamSystem.NoBuffer, BassFlags.Decode, procedures, GCHandle.ToIntPtr(handle));
                    }
                    if (context.Error != null) throw context.Error;
                    if (input.MissingBlock >= 0) return null;
                    if (decoder == 0) throw new FanmadeException("PREVIEW_CODEC_UNSUPPORTED");
                    var info = Bass.ChannelGetInfo(decoder);
                    if (info.Channels < 1 || info.Channels > 2 || info.Frequency < 8000 || info.Frequency > 96000)
                        throw new FanmadeException("PREVIEW_AUDIO_FORMAT_UNSUPPORTED");
                    double length = Bass.ChannelBytes2Seconds(decoder, Bass.ChannelGetLength(decoder));
                    double start = Math.Clamp(double.IsNaN(demoStart) || double.IsInfinity(demoStart) ? 0 : demoStart, 0, Math.Max(0, length - 0.1));
                    bool sought = Bass.ChannelSetPosition(decoder, Bass.ChannelSeconds2Bytes(decoder, start));
                    if (context.Error != null) throw context.Error;
                    if (input.MissingBlock >= 0) return null;
                    if (!sought) throw new FanmadeException("PREVIEW_SEEK_FAILED");
                    int limit = checked((int)(DurationSeconds * info.Frequency * info.Channels * 2));
                    using var wave = new MemoryStream(limit + 44);
                    wave.Write(new byte[44], 0, 44);
                    byte[] buffer = new byte[32768];
                    while (wave.Length - 44 < limit)
                    {
                        cancel.ThrowIfCancellationRequested();
                        int n = Bass.ChannelGetData(decoder, buffer, (int)Math.Min(buffer.Length, limit - (wave.Length - 44)));
                        if (context.Error != null) throw context.Error;
                        if (input.MissingBlock >= 0) return null;
                        if (n <= 0) break;
                        wave.Write(buffer, 0, n);
                    }
                    int size = (int)wave.Length - 44;
                    if (size == 0) throw new FanmadeException("PREVIEW_DECODE_FAILED");
                    wave.Position = 0;
                    using var writer = new BinaryWriter(wave, Encoding.ASCII, true);
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16); writer.Write((short)1); writer.Write((short)info.Channels); writer.Write(info.Frequency);
                    writer.Write(info.Frequency * info.Channels * 2); writer.Write((short)(info.Channels * 2)); writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Flush();
                    return wave.ToArray();
                }
                finally { if (decoder != 0) Bass.StreamFree(decoder); handle.Free(); }
            }
        }
#endif
    }
}
