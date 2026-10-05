using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    // One session, one content hash. The decoder callbacks only read memory; missing blocks are
    // fetched between decode attempts on a worker, never from the audio or Unity main thread.
    public sealed class PreviewRangeStream : Stream
    {
        public const int BlockSize = 128 * 1024;
        public const int MaxBlocks = 64;
        readonly Dictionary<long, byte[]> blocks = new Dictionary<long, byte[]>();
        readonly FanmadeEndpoint endpoint;
        readonly string chartId;
        readonly bool external;
        readonly CancellationToken cancel;
        FanmadeResources manifest;
        string getUrl, headUrl, etag;
        long length, position;
        bool refreshed, disposed;
        public string Hash { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long MissingBlock { get; private set; } = -1;
        PreviewRangeStream(FanmadeEndpoint endpoint, FanmadeChart chart, CancellationToken cancel)
        {
            this.endpoint = endpoint; chartId = chart.Id; this.cancel = cancel;
            external = endpoint.ResourceDownloadVersion == 1; Hash = chart.AudioHash;
            getUrl = headUrl = "/api/v1/charts/" + chart.Id + (endpoint.SongIdOnly ? "/audio" : "/versions/" + chart.Version + "/audio");
            etag = "\"" + Hash + "\"";
        }
        public static async Task<PreviewRangeStream> OpenAsync(FanmadeEndpoint endpoint, FanmadeChart chart, CancellationToken cancel)
        {
            var stream = new PreviewRangeStream(endpoint, chart, cancel);
            if (stream.external)
            {
                stream.manifest = await endpoint.ResourcesAsync(chart.Id, cancel).ConfigureAwait(false);
                stream.Hash = stream.manifest.Audio.Hash; stream.length = stream.manifest.Audio.Size;
                stream.getUrl = stream.manifest.Audio.Url; stream.headUrl = stream.manifest.Audio.HeadUrl;
                stream.etag = null;
            }
            var head = await stream.RequestAsync(null, 0).ConfigureAwait(false);
            stream.length = head.Length; stream.etag = head.ETag;
            return stream;
        }
        async Task RefreshAsync()
        {
            if (!external || refreshed) throw new FanmadeException("PREVIEW_REFRESH_EXHAUSTED");
            refreshed = true;
            var latest = await endpoint.ResourcesAsync(chartId, cancel).ConfigureAwait(false);
            if (latest.Audio.Hash != Hash || latest.Audio.Size != length) throw new FanmadeException("PREVIEW_RESOURCE_CHANGED");
            manifest = latest; getUrl = latest.Audio.Url; headUrl = latest.Audio.HeadUrl;
        }
        async Task<(byte[] Bytes, long Length, string ETag)> RequestAsync(long? start, long end)
        {
            cancel.ThrowIfCancellationRequested();
            if (external && manifest.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(45)) await RefreshAsync().ConfigureAwait(false);
            for (int attempt = 0; ; attempt++)
            {
                try { return await endpoint.PreviewRequestAsync(start.HasValue ? getUrl : headUrl, external, start, end, length, etag, cancel).ConfigureAwait(false); }
                catch (HttpStatusException error) when (external && !refreshed && (error.Status == 403 || error.Status == 404))
                { await RefreshAsync().ConfigureAwait(false); }
                catch (HttpStatusException error) when (!external && attempt == 0 && error.Status == 401 && endpoint.IsAuthenticated)
                { await endpoint.LoginAsync(cancel).ConfigureAwait(false); }
            }
        }
        public void RestartRead() { position = 0; MissingBlock = -1; }
        public async Task FetchMissingAsync()
        {
            if (MissingBlock < 0) throw new FanmadeException("PREVIEW_DECODE_FAILED");
            await FetchBlockAsync(MissingBlock).ConfigureAwait(false);
        }
        public async Task FetchBlockAsync(long index)
        {
            cancel.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException(nameof(PreviewRangeStream));
            if (blocks.ContainsKey(index)) return;
            if (blocks.Count >= MaxBlocks) throw new FanmadeException("PREVIEW_BYTE_BUDGET_EXCEEDED");
            long start = checked(index * BlockSize);
            if (start < 0 || start >= length) throw new ArgumentOutOfRangeException(nameof(index));
            var response = await RequestAsync(start, Math.Min(length - 1, start + BlockSize - 1)).ConfigureAwait(false);
            cancel.ThrowIfCancellationRequested();
            blocks.Add(index, response.Bytes); DownloadedBytes += response.Bytes.Length;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancel.ThrowIfCancellationRequested();
            if (disposed) throw new ObjectDisposedException(nameof(PreviewRangeStream));
            int read = 0;
            while (read < count && position < length)
            {
                long index = position / BlockSize;
                if (!blocks.TryGetValue(index, out var bytes))
                { if (MissingBlock < 0) MissingBlock = index; break; }
                int within = (int)(position % BlockSize), n = Math.Min(count - read, bytes.Length - within);
                Buffer.BlockCopy(bytes, within, buffer, offset + read, n); read += n; position += n;
            }
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position : length) + offset);
            if (target < 0 || target > length) throw new IOException("PREVIEW_SEEK_INVALID");
            return position = target;
        }
        protected override void Dispose(bool disposing) { disposed = true; blocks.Clear(); base.Dispose(disposing); }
        public override bool CanRead => !disposed;
        public override bool CanSeek => !disposed;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
