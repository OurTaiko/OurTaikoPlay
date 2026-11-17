using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    // The credential-free resource channel, shared by songs and nameplate art. Bytes are
    // bounded and verified before either the object cache or a decoder can use them.
    internal static class ResourceDownload
    {
        public static void ValidateUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
                uri.Fragment.Length != 0 || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
                throw new FanmadeException("RESOURCE_URL_INVALID");
        }

        public static async Task<byte[]> FetchAsync(HttpClient http, FanmadeResource resource, CancellationToken cancel,
            Action<long, long> progress = null, Action<string> validate = null)
        {
            if (resource.Size <= 0 || resource.Size > 256L * 1024 * 1024 || (resource.Hash == null || !Json.HexId(resource.Hash, 64)))
                throw new FanmadeException("RESOURCE_METADATA_INVALID");
            ValidateUrl(resource.Url); validate?.Invoke(resource.Url);
            bool fallback = !string.IsNullOrEmpty(resource.FallbackUrl) && resource.FallbackUrl != resource.Url;
            if (fallback) { ValidateUrl(resource.FallbackUrl); validate?.Invoke(resource.FallbackUrl); }
            using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            overall.CancelAfter(TimeSpan.FromSeconds(120));
            for (int attempt = 0; ; attempt++)
            {
                string url = attempt == 0 ? resource.Url : resource.FallbackUrl;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                if (attempt == 0 && fallback) timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if ((int)response.StatusCode != 200) throw new HttpStatusException((int)response.StatusCode);
                    long? length = response.Content.Headers.ContentLength;
                    if (length.HasValue && length.Value != resource.Size) throw new FanmadeException("DOWNLOAD_SIZE_MISMATCH");
                    var bytes = new byte[checked((int)resource.Size)];
                    using var stream = await response.Content.ReadAsStreamAsync();
                    int received = 0;
                    while (received < bytes.Length)
                    {
                        int count = await stream.ReadAsync(bytes, received, Math.Min(81920, bytes.Length - received), timeout.Token);
                        if (count == 0) throw new FanmadeException("DOWNLOAD_SIZE_MISMATCH");
                        received += count; progress?.Invoke(received, resource.Size);
                    }
                    if (await stream.ReadAsync(new byte[1], 0, 1, timeout.Token) != 0) throw new FanmadeException("DOWNLOAD_SIZE_MISMATCH");
                    string hash = await Task.Run(() => {
                        using var sha = SHA256.Create();
                        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    }, timeout.Token);
                    if (hash != resource.Hash) throw new FanmadeException("DOWNLOAD_INTEGRITY_FAILED");
                    return bytes;
                }
                catch (Exception error) when (attempt == 0 && fallback && !overall.IsCancellationRequested &&
                    (error is HttpRequestException || error is IOException || error is FanmadeException || error is OperationCanceledException)) { }
            }
        }
    }
}
