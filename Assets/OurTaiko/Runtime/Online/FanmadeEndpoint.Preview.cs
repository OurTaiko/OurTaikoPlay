using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeEndpoint
    {
        // HEAD and GET have separate signatures. Legacy API requests retain their own auth branch.
        internal async Task<(byte[] Bytes, long Length, string ETag)> PreviewRequestAsync(
            string url, bool external, long? start, long end, long expectedLength, string etag, CancellationToken cancel)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await Task.CompletedTask;
            throw new FanmadeException("PREVIEW_PLATFORM_UNSUPPORTED");
#else
            if (external) ValidateResourceUrl(url);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            using var request = new HttpRequestMessage(start.HasValue ? HttpMethod.Get : HttpMethod.Head, external ? url : Config.baseUrl + url);
            if (!external && !string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (start.HasValue)
            {
                request.Headers.Range = new RangeHeaderValue(start.Value, end);
                if (!string.IsNullOrEmpty(etag)) request.Headers.TryAddWithoutValidation(external ? "If-Match" : "If-Range", etag);
            }
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status != (start.HasValue ? 206 : 200))
                {
                    if (start.HasValue && status == 200) throw new FanmadeException("PREVIEW_RANGE_IGNORED");
                    throw new HttpStatusException(status);
                }
                string actualTag = response.Headers.ETag?.ToString();
                if (string.IsNullOrEmpty(actualTag) || actualTag.StartsWith("W/", StringComparison.Ordinal)) throw new FanmadeException("PREVIEW_ETAG_INVALID");
                if (!string.IsNullOrEmpty(etag) && actualTag != etag) throw new FanmadeException("PREVIEW_RESOURCE_CHANGED");
                long length = response.Content.Headers.ContentLength ?? -1;
                if (!start.HasValue)
                {
                    if (length <= 0 || length > 256L * 1024 * 1024 || (expectedLength > 0 && length != expectedLength))
                        throw new FanmadeException("PREVIEW_LENGTH_INVALID");
                    return (null, length, actualTag);
                }
                var range = response.Content.Headers.ContentRange;
                long count = end - start.Value + 1;
                if (count <= 0 || count > PreviewRangeStream.BlockSize || range == null || range.Unit != "bytes"
                    || range.From != start || range.To != end || range.Length != expectedLength || (length >= 0 && length != count))
                    throw new FanmadeException("PREVIEW_RANGE_INVALID");
                byte[] bytes = new byte[(int)count];
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                int received = 0;
                while (received < bytes.Length)
                {
                    int read = await stream.ReadAsync(bytes, received, bytes.Length - received, linked.Token).ConfigureAwait(false);
                    if (read == 0) throw new FanmadeException("PREVIEW_RANGE_INVALID");
                    received += read;
                }
                if (await stream.ReadAsync(new byte[1], 0, 1, linked.Token).ConfigureAwait(false) != 0) throw new FanmadeException("PREVIEW_RANGE_INVALID");
                return (bytes, expectedLength, actualTag);
            }
            catch (OperationCanceledException) { throw new FanmadeException(cancel.IsCancellationRequested ? "DOWNLOAD_CANCELLED" : "NETWORK_TIMEOUT"); }
            catch (HttpRequestException error) { throw new FanmadeException(NetworkCode(error)); }
            catch (IOException error) { throw new FanmadeException(NetworkCode(error)); }
#endif
        }
    }
}
