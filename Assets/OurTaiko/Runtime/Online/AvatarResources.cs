using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // One resource channel for account plates and multiplayer pictures. Regional
    // lookup sends no account token; the original public URL is always retained.
    internal static class AvatarResources
    {
        static readonly Regex PublicAvatar = new Regex(@"\Ahttps://distrib\.ourtaiko\.org/sso/avatars/[a-f0-9]{32}/[a-f0-9]{32}\.webp\z");
        internal static bool IsPublic(string url) => url != null && PublicAvatar.IsMatch(url);

        public static async Task<byte[]> FetchAsync(HttpClient http, string original, string profileBase, long limit, CancellationToken cancel)
        {
            string selected = original;
            if (IsPublic(original))
            {
                try
                {
                    using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    lookup.CancelAfter(TimeSpan.FromSeconds(3));
                    string endpoint = profileBase.TrimEnd('/') + "/api/v1/distribution/avatar?url=" + Uri.EscapeDataString(original);
                    byte[] data = await ReadAsync(http, endpoint, 16384, lookup.Token);
                    var reply = JObject.Parse(System.Text.Encoding.UTF8.GetString(data));
                    string candidate = reply["url"]?.Type == JTokenType.String ? (string)reply["url"] : null;
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0)
                        selected = candidate;
                }
                catch (Exception) when (!cancel.IsCancellationRequested) { }
            }
            try { return await ReadAsync(http, selected, limit, cancel); }
            catch (Exception error) when (selected != original && !cancel.IsCancellationRequested &&
                (error is HttpRequestException || error is IOException || error is OperationCanceledException))
            { return await ReadAsync(http, original, limit, cancel); }
        }

        static async Task<byte[]> ReadAsync(HttpClient http, string url, long limit, CancellationToken cancel)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new IOException("Avatar HTTP " + (int)response.StatusCode);
            if ((response.Content.Headers.ContentLength ?? 0) > limit) throw new IOException("Avatar exceeds limit");
            using var stream = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            var buffer = new byte[32768];
            while (true)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token);
                if (count == 0) break;
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                if (output.Length + count > limit) throw new IOException("Avatar exceeds limit");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
    }
}
