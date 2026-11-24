using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class OnlineEndpoint
    {
        // A manifest's file, verified (size and SHA-256): from the player's root, and once more from
        // the canonical root when that fails or sends other bytes (Distribution.FetchAsync).
        // No API credentials, cookies, login recovery or redirects on the resource channel.
        public async Task<byte[]> ResourceBytesAsync(OnlineResource resource, CancellationToken cancel,
            Action<long, long> progress = null)
        {
            var address = (await FilesAsync(cancel)).Locate(resource.Path);
            return await Distribution.FetchAsync(address, url => DownloadAsync(url, resource, cancel, progress), cancel);
        }

        async Task<byte[]> DownloadAsync(string url, OnlineResource resource, CancellationToken cancel, Action<long, long> progress)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            byte[] bytes;
            try
            {
                using var response = await OurTaikoHttpClient.Shared.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                Log.Debug("Online", $"GET {request.RequestUri.Host}{request.RequestUri.AbsolutePath}: {(int)response.StatusCode}");
                if ((int)response.StatusCode != 200) throw new HttpStatusException((int)response.StatusCode);
                long? length = response.Content.Headers.ContentLength;
                if (length.HasValue && length.Value != resource.Size) throw new OnlineException("DOWNLOAD_SIZE_MISMATCH");
                // The manifest size has already passed the business limit. One bounded buffer.
                bytes = new byte[checked((int)resource.Size)];
                using var stream = await response.Content.ReadAsStreamAsync();
                int received = 0;
                while (received < bytes.Length)
                {
                    int read = await stream.ReadAsync(bytes, received, Math.Min(81920, bytes.Length - received), linked.Token);
                    if (read == 0) throw new OnlineException("DOWNLOAD_SIZE_MISMATCH");
                    received += read; progress?.Invoke(received, resource.Size);
                }
                if (await stream.ReadAsync(new byte[1], 0, 1, linked.Token) != 0) throw new OnlineException("DOWNLOAD_SIZE_MISMATCH");
            }
            catch (OperationCanceledException) { throw new OnlineException(cancel.IsCancellationRequested ? "DOWNLOAD_CANCELLED" : "NETWORK_TIMEOUT"); }
            // Transport exceptions are not kept: only the code matters here.
            catch (HttpRequestException error) { throw new OnlineException(NetworkCode(error)); }
            catch (IOException error) { throw new OnlineException(NetworkCode(error)); }
            if (await Task.Run(() => Sha256Hex(bytes), cancel) != resource.Hash) throw new OnlineException("DOWNLOAD_INTEGRITY_FAILED");
            return bytes;
        }
    }
}
