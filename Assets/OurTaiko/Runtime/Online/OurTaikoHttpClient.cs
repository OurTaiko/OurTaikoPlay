using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OurTaiko.Online
{
    // The one HttpClient of the game: every HTTP request (the servers' API, the account center,
    // the public files and the mirror, pictures) goes through Shared. One client keeps one pool of
    // connections for the whole game and one way of asking:
    //
    //   - redirects are not followed, cookies are not kept, and the system proxy is not used (what
    //     the API client always did; the public files are served where they are asked for);
    //   - every request says what asks, in User-Agent: the game, its version, the platform, the
    //     operating system and the processor, e.g.
    //       OurTaikoPlay/1.0.2 (Windows; Windows 11 10.0.26100 64bit; X64)
    //     so the servers' logs can tell the game's versions and platforms apart (a version before
    //     this one sends none);
    //   - the client has no timeout of its own: a request is bounded by the caller's, through
    //     SendAsync/GetAsync with a timeout here, or by the caller's own token.
    //
    // マルチ演奏's gRPC stream is HTTP/2, which this client does not speak (MultiTransport has its
    // own handler); it says the same User-Agent through UserAgentHandler.
    public static class OurTaikoHttpClient
    {
        public const string Product = "OurTaikoPlay";

        static string userAgent;
        static readonly Lazy<HttpClient> shared = new Lazy<HttpClient>(Create, LazyThreadSafetyMode.ExecutionAndPublication);

        // What the game says it is. Unity's version and platform can only be read on the main
        // thread, while the first request may start on any; so it is read at load, before any.
        public static string UserAgent => userAgent ?? Product;

        public static HttpClient Shared => shared.Value;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Describe()
        {
            userAgent ??= Describe(Application.version, Application.platform, SystemInfo.operatingSystem,
                RuntimeInformation.ProcessArchitecture.ToString(), Application.isEditor);
        }

        // The User-Agent of a game of that version on that platform. Each part keeps only what a
        // header's comment may hold, so no system's name can break it.
        public static string Describe(string version, RuntimePlatform platform, string system, string architecture, bool editor)
        {
            var text = new StringBuilder(Product).Append('/').Append(Token(version));
            text.Append(" (").Append(PlatformName(platform));
            string os = Comment(system);
            if (os.Length > 0) text.Append("; ").Append(os);
            string cpu = Comment(architecture);
            if (cpu.Length > 0) text.Append("; ").Append(cpu);
            if (editor) text.Append("; Editor");
            return text.Append(')').ToString();
        }

        static string PlatformName(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: return "Windows";
                case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return "macOS";
                case RuntimePlatform.LinuxPlayer: case RuntimePlatform.LinuxEditor: return "Linux";
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                default: return Comment(platform.ToString());
            }
        }

        // A product version: letters, digits and . - + _ only; "0" when nothing is left.
        static string Token(string value)
        {
            var text = new StringBuilder();
            foreach (char c in value ?? "")
                if (c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '+' || c == '_')) text.Append(c);
            return text.Length > 0 ? text.ToString() : "0";
        }

        // Printable ASCII without the characters that would end or nest the comment, runs of
        // space as one, at most 80 characters.
        static string Comment(string value)
        {
            var text = new StringBuilder();
            foreach (char c in value ?? "")
            {
                bool keep = c > ' ' && c < 127 && c != '(' && c != ')' && c != '\\' && c != ';';
                if (keep) text.Append(c);
                else if (text.Length > 0 && text[text.Length - 1] != ' ') text.Append(' ');
                if (text.Length >= 80) break;
            }
            return text.ToString().Trim();
        }

        static HttpClient Create()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            return client;
        }

        // Sends a request that must have its answer within `timeout`: its headers, and with
        // ResponseContentRead its whole body as well (what HttpClient.Timeout bounded). A request
        // that runs out of time throws OperationCanceledException with `cancel` not cancelled.
        public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancel = default,
            HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(timeout);
            return await Shared.SendAsync(request, completion, limit.Token).ConfigureAwait(false);
        }

        public static async Task<HttpResponseMessage> GetAsync(string url, TimeSpan timeout, CancellationToken cancel = default,
            HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            return await SendAsync(request, timeout, cancel, completion).ConfigureAwait(false);
        }

        // Puts the game's User-Agent on what a handler of another client sends (gRPC's, whose own
        // would otherwise name only the library).
        public sealed class UserAgentHandler : DelegatingHandler
        {
            public UserAgentHandler(HttpMessageHandler inner) : base(inner) { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                return base.SendAsync(request, cancel);
            }
        }
    }
}
