using System;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko
{
    // Decodes a song off the main thread for a coroutine to wait on. Whoever calls Take owns the
    // sample; Dispose releases a decode nobody took, now or once it completes, so a cancelled
    // scene or a stale preview never leaks native audio.
    public sealed class BackgroundAudioDecode : IDisposable
    {
        readonly Task<NativeAudioSample> task;
        bool taken;

        // The engine generation the decode belongs to; a device change since makes it stale.
        public int Generation { get; }
        public bool IsDone => task.IsCompleted;
        public bool Succeeded => task.IsCompletedSuccessfully;
        public Exception Error => task.Exception?.GetBaseException();

        // `read` runs on the worker too, so a file read does not block the frame.
        public BackgroundAudioDecode(Func<byte[]> read, AudioEngine engine, bool speedChange, CancellationToken cancel = default)
        {
            int generation = Generation = engine.Generation;
            task = Task.Run(() => new NativeAudioSample(read(), engine, true, speedChange, generation), cancel);
        }

        public NativeAudioSample Take()
        {
            taken = true;
            return task.Result;
        }

        public void Dispose()
        {
            if (taken) return;
            taken = true;
            _ = task.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); });
        }
    }
}
