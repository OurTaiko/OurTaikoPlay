using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko.Online
{
    // One public file's addresses: on the player's root, and on the canonical root to try once
    // when the first fails (null when the two are the same address).
    public readonly struct FileAddress
    {
        public readonly string Url, Fallback;
        public FileAddress(string url, string fallback) { Url = url; Fallback = fallback == url ? null : fallback; }
    }

    // A root public files are read from: the one the site chose for this player (Base, the mirror
    // in mainland China), and the canonical one (Canonical) to fall back on.
    public sealed class FileRoot
    {
        public readonly string Base, Canonical;
        public readonly bool Mirror;

        public FileRoot(string baseUrl, string canonical, bool mirror = false)
        {
            Base = baseUrl; Canonical = canonical; Mirror = mirror;
        }

        // The addresses of the file at `path`, as the API gives it. A path that is not one
        // (Distribution.ValidPath) is RESOURCE_PATH_INVALID.
        public FileAddress Locate(string path)
        {
            if (!Distribution.ValidPath(path)) throw new FanmadeException("RESOURCE_PATH_INVALID");
            return new FileAddress(Base + path, Canonical + path);
        }
    }

    // Where the public files come from (OurTaikoNet's API version 2, internal/files). The API
    // names every public file by its path under the distribution root ("fanmade/<sha256>.ogg",
    // "3dchara/cos/0.glb", "nameplate/shadow.png"); the site says once which root this player
    // reads from (GET /api/v2/distribution/source): its mirror for mainland China, the canonical
    // root otherwise. This is the one place a path is joined to a root. The answer is asked once
    // per launch for each site (in a release only net.ourtaiko.org) and kept in memory only; when
    // the site cannot be asked, the canonical root is used.
    public static class Distribution
    {
        public const string CanonicalBase = "https://distrib.ourtaiko.org/";
        public const string SourcePath = "/api/v2/distribution/source";
        public static readonly FileRoot CanonicalRoot = new FileRoot(CanonicalBase, CanonicalBase);

        static readonly object gate = new object();
        static readonly Dictionary<string, Task<FileRoot>> roots = new Dictionary<string, Task<FileRoot>>(StringComparer.OrdinalIgnoreCase);
        static HttpClient http;

        // Each Play session asks again, as a launch does (the Editor keeps statics between them).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Forget();

        // Forgets every answer: the next file asks its site again.
        public static void Forget()
        {
            lock (gate) roots.Clear();
        }

        // A root given rather than asked for (tests): the files of `site` come from it.
        public static void Use(string site, FileRoot root)
        {
            lock (gate) roots[Key(site)] = Task.FromResult(root);
        }

        // The root the files of `site` come from, asked of the site the first time. Never fails:
        // the canonical root when the site cannot be asked or gives one that cannot be used.
        // `cancel` stops this caller's wait only; the question goes on for the next one.
        public static Task<FileRoot> RootAsync(string site = ServerConfig.Site, CancellationToken cancel = default)
        {
            Task<FileRoot> task;
            lock (gate)
            {
                string key = Key(site);
                if (!roots.TryGetValue(key, out task)) roots[key] = task = AskAsync(key);
            }
            return task.IsCompleted || !cancel.CanBeCanceled ? task : WaitAsync(task, cancel);
        }

        // The addresses of a file of `site`, the root asked for first when it is not known yet.
        public static async Task<FileAddress> LocateAsync(string path, string site = ServerConfig.Site, CancellationToken cancel = default)
            => (await RootAsync(site, cancel)).Locate(path);

        // Reads a file with `fetch` from its address on the player's root and, when that fails
        // (a network error, a refusal, bytes that are not what the API described: whatever
        // `fetch` throws), once more from the canonical root. A cancellation by `cancel` is never
        // retried; a timeout is, though HttpClient reports it as a cancellation too: a mirror that
        // hangs is the likeliest way for it to fail.
        public static async Task<T> FetchAsync<T>(FileAddress address, Func<string, Task<T>> fetch, CancellationToken cancel = default)
        {
            try { return await fetch(address.Url); }
            catch (Exception error) when (address.Fallback != null && !cancel.IsCancellationRequested)
            {
                Log.Info("Online", "A file did not come from " + Host(address.Url) + " (" + error.Message + "); trying " + Host(address.Fallback));
                return await fetch(address.Fallback);
            }
        }

        // The path of an address on the canonical root ("sso/avatars/a/b.webp"), or null when it
        // is not a plain address there (another host, a query, a path that leaves the root).
        public static string PathOf(string address)
        {
            if (address == null || !address.StartsWith(CanonicalBase, StringComparison.Ordinal)) return null;
            string path = address.Substring(CanonicalBase.Length);
            return ValidPath(path) ? path : null;
        }

        // A path a file may have under the root, as the server checks it (files.Valid): relative,
        // clean, with no query, fragment, escape or backslash.
        public static bool ValidPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || path[0] == '/' || path.IndexOfAny(new[] { '?', '#', '\\', '%', '\0', ':' }) >= 0)
                return false;
            foreach (char c in path)
                if (c <= ' ' || c == 0x7f) return false;
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }

        // The site's answer as the game uses it: {"mirror", "baseUrl", "canonicalBaseUrl"}, each
        // root checked (Root); the canonical root for an answer that cannot be used.
        public static FileRoot Read(JToken answer, string site)
        {
            if (!(answer is JObject o)) return CanonicalRoot;
            string baseUrl = Root(o["baseUrl"], site);
            if (baseUrl == null) return CanonicalRoot;
            return new FileRoot(baseUrl, Root(o["canonicalBaseUrl"], site) ?? CanonicalBase, o["mirror"]?.Type == JTokenType.Boolean && (bool)o["mirror"]);
        }

        // A root is an https address without credentials, query or fragment, ending in a slash.
        // A site on this machine (a development server, a test's) may give plain-HTTP roots on
        // this machine too.
        static string Root(JToken value, string site)
        {
            if (value?.Type != JTokenType.String) return null;
            string text = (string)value;
            if (!text.EndsWith("/", StringComparison.Ordinal) || !Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
            if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || text.IndexOfAny(new[] { '?', '#' }) >= 0) return null;
            bool local = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback
                && Uri.TryCreate(site, UriKind.Absolute, out var from) && from.IsLoopback;
            return uri.Scheme == Uri.UriSchemeHttps || local ? text : null;
        }

        static async Task<FileRoot> AskAsync(string site)
        {
            try
            {
                http ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new HttpRequestMessage(HttpMethod.Get, site + SourcePath);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (text.Length > 16 * 1024) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                var root = Read(Json.Parse(text), site);
                Log.Info("Online", "Public files come from " + Host(root.Base) + (root.Mirror ? " (mirror)" : ""));
                return root;
            }
            catch (Exception error)
            {
                Log.Warn("Online", "Could not ask where public files come from (" + error.Message + "); using " + Host(CanonicalBase));
                return CanonicalRoot;
            }
        }

        static async Task<FileRoot> WaitAsync(Task<FileRoot> task, CancellationToken cancel)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancel.Register(() => cancelled.TrySetResult(true)))
                if (await Task.WhenAny(task, cancelled.Task) != task) throw new OperationCanceledException(cancel);
            return await task;
        }

        // The site as a key and a request address: no trailing slash.
        static string Key(string site) => (string.IsNullOrEmpty(site) ? ServerConfig.Site : site).Trim().TrimEnd('/');

        // Only the host, for the log.
        static string Host(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Authority : "?";
    }
}
