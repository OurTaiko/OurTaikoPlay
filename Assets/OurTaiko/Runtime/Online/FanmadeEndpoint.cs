using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // A player signed in to one server: 1P (seat 0) or 2P (seat 1). The bearer token lives only in
    // memory. A seat signed in through the OurTaiko account center holds a game session traded for
    // its account (Renew trades again when it expires); 1P on a servers.json server logs in with
    // the configured username and password instead.
    public sealed class FanmadeSeat
    {
        internal volatile string Token = "";
        public bool IsAuthenticated { get; internal set; }
        public string Nickname { get; internal set; } = "";
        // The account center's user id; empty for a username and password sign-in.
        public string UserId { get; internal set; } = "";
        internal Func<CancellationToken, Task<string>> Renew;
        // Account scores from GET /game/scores, kept only while signed in.
        internal readonly List<FanmadeScore> Scores = new List<FanmadeScore>();
        internal readonly Dictionary<string, FanmadeScore> Best = new Dictionary<string, FanmadeScore>();
        public int ScoreCount => Scores.Count;

        internal void IndexScore(FanmadeScore score)
        {
            var key = FanmadeEndpoint.ScoreKey(score.Song, score.Difficulty);
            // A score of the song's current files comes before any score of replaced ones.
            if (!Best.TryGetValue(key, out var best) || (score.IsLatest && !best.IsLatest)
                || (score.IsLatest == best.IsLatest && score.Score > best.Score)) Best[key] = score;
        }

        internal void SignOut()
        {
            Token = ""; IsAuthenticated = false; Nickname = ""; UserId = ""; Renew = null;
            Scores.Clear(); Best.Clear();
        }
    }

    // One configured server (fanmade.cpp Endpoint + request/login/authorized), on System.Net.Http as
    // in MajdataPlay, with a seat per player. Redirects are refused, TLS is verified and an empty
    // proxy is an explicit direct connection.
    public sealed partial class FanmadeEndpoint : IDisposable
    {
        public const long DefaultLimit = 64L * 1024 * 1024;
        public const int SeatCount = 2;
        public readonly ServerConfig Config;
        // sha256(base_url + "\n" + username): accounts on one server keep separate caches and queues.
        public readonly string Id;
        // What 1P's pending scores are queued under (AccountIdOf(0)).
        public string AccountId => AccountIdOf(0);
        public bool IsConnected { get; internal set; }
        // 1P, the seat every servers.json server and the catalog belong to.
        public bool IsAuthenticated => Seats[0].IsAuthenticated;
        public int ChartCount { get; internal set; } = -1;
        public string Nickname => Seats[0].Nickname;
        public readonly FanmadeSeat[] Seats = { new FanmadeSeat(), new FanmadeSeat() };
        // The account center the catalog names ("sso"), and this service's name there; empty when
        // the server only takes a username and password.
        public string SsoIssuer { get; internal set; } = "";
        public string SsoService { get; internal set; } = "";
        // Serializes HTTP per endpoint, like the C++ http_mutex.
        internal readonly SemaphoreSlim Transport = new SemaphoreSlim(1, 1);

        readonly HttpClient http;

        // The key a seat's pending scores are queued under: the account center's user, or else the
        // configured username (sha256(base_url + "\n" + username), the same as Id before 2P existed).
        public string AccountIdOf(int seat)
        {
            string user = Seats[seat].UserId;
            return Sha256Hex(Encoding.UTF8.GetBytes(Config.baseUrl + "\n" + (user.Length > 0 ? "sso:" + user : Config.username)));
        }

        public FanmadeEndpoint(ServerConfig config)
        {
            Config = config.Clone();
            Config.baseUrl = (Config.baseUrl ?? "").TrimEnd('/');
            Config.username ??= ""; Config.password ??= ""; Config.httpProxy ??= "";
            Id = Sha256Hex(Encoding.UTF8.GetBytes(Config.baseUrl + "\n" + Config.username));
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            if (string.IsNullOrEmpty(Config.httpProxy)) handler.UseProxy = false;
            else { handler.UseProxy = true; handler.Proxy = new WebProxy(new Uri(Config.httpProxy)); }
            // Per-request timeouts below; the client-wide one only bounds pathological cases.
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public void Dispose()
        {
            http.Dispose();
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        public bool HasValidUrl()
        {
            string url = Config.baseUrl;
            return (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
                && url.IndexOfAny(new[] { '?', '#', '@', ' ', '\r', '\n' }) < 0 && Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        // Empty credentials stay a guest without a request. Rejected credentials (401/403) leave
        // guest mode; other failures keep the account's pending scores eligible for retry.
        // 1P's username and password sign-in (servers.json servers). Empty credentials stay a guest
        // without a request. Rejected credentials (401/403) leave guest mode; other failures keep
        // the account's pending scores eligible for retry.
        public async Task LoginAsync(CancellationToken cancel = default)
        {
            var seat = Seats[0];
            seat.Token = "";
            if (!Config.HasCredentials) { seat.IsAuthenticated = false; return; }
            var body = new JObject { ["username"] = Config.username, ["password"] = Config.password };
            try
            {
                var reply = Json.Parse(await RequestAsync("/api/v1/game/login", body.ToString(Newtonsoft.Json.Formatting.None), cancel: cancel));
                string accessToken = Json.Str(reply, "accessToken");
                if (!Json.HexId(accessToken, 64)) throw new FanmadeException("API_TOKEN_INVALID");
                seat.Token = accessToken;
                seat.IsAuthenticated = true;
                var user = reply["user"] as JObject;
                string nickname = user?["nickname"]?.Type == JTokenType.String ? (string)user["nickname"] : "";
                seat.Nickname = nickname.Length > 0 ? nickname : Config.username;
            }
            catch (HttpStatusException error)
            {
                if (error.Status == 401 || error.Status == 403) seat.IsAuthenticated = false;
                throw;
            }
        }

        public void BecomeGuest() { Seats[0].Token = ""; Seats[0].IsAuthenticated = false; }

        // Another account on the same server and catalog: signed out until LoginAsync. False when
        // the account is the one already set.
        internal bool SetAccount(string username, string password)
        {
            username ??= ""; password ??= "";
            if (Config.username == username && Config.password == password && Seats[0].UserId.Length == 0) return false;
            Config.username = username; Config.password = password;
            Seats[0].SignOut();
            return true;
        }

        // A seat signed in with a game session traded for its OurTaiko account (user id and
        // nickname from the account center); renew trades again when the session expires.
        internal void UseAccount(int seat, string token, string userId, string nickname, Func<CancellationToken, Task<string>> renew)
        {
            if (!Json.HexId(token ?? "", 64)) throw new FanmadeException("API_TOKEN_INVALID");
            var s = Seats[seat];
            if (s.UserId != userId) { s.Scores.Clear(); s.Best.Clear(); }
            if (seat == 0) { Config.username = ""; Config.password = ""; }
            s.Token = token; s.UserId = userId ?? ""; s.Nickname = nickname ?? ""; s.Renew = renew; s.IsAuthenticated = true;
        }

        // A request with a seat's token. An expired token signs in once more (traded again, or
        // 1P's password login) and repeats the request.
        public async Task<string> AuthorizedAsync(string path, string body = "", string key = "", CancellationToken cancel = default, int seat = 0)
        {
            var s = Seats[seat];
            try { return await RequestAsync(path, body, key, cancel: cancel, bearer: s.Token); }
            catch (HttpStatusException error) when (error.Status == 401 && s.IsAuthenticated && (s.Renew != null || seat == 0))
            {
                if (s.Renew != null)
                {
                    string renewed = await s.Renew(cancel);
                    if (!Json.HexId(renewed ?? "", 64)) throw new FanmadeException("API_TOKEN_INVALID");
                    s.Token = renewed;
                }
                else await LoginAsync(cancel);
                return await RequestAsync(path, body, key, cancel: cancel, bearer: s.Token);
            }
        }

        // bearer null: 1P's token (anonymous requests are unaffected by it).
        public async Task<string> RequestAsync(string path, string body = "", string key = "", long limit = DefaultLimit,
            CancellationToken cancel = default, Action<long, long> progress = null, string bearer = null)
        {
            var bytes = await RequestBytesAsync(path, body, key, limit, cancel, progress, bearer);
            return new UTF8Encoding(false).GetString(bytes);
        }

        public async Task<byte[]> RequestBytesAsync(string path, string body = "", string key = "", long limit = DefaultLimit,
            CancellationToken cancel = default, Action<long, long> progress = null, string bearer = null)
        {
            string token = bearer ?? Seats[0].Token;
            if (cancel.IsCancellationRequested) throw new FanmadeException("DOWNLOAD_CANCELLED");
            // Files come from signed resource links (ResourceBytesAsync); API calls are small.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            using var request = new HttpRequestMessage(string.IsNullOrEmpty(body) ? HttpMethod.Get : HttpMethod.Post, Config.baseUrl + path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // API replies may come gzipped (an OurTaiko Database catalog is 4 MB, 0.8 MB packed). Only
            // here: resource downloads keep the exact bytes and sizes their manifest names.
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(key)) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            if (!string.IsNullOrEmpty(body)) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                int status = (int)response.StatusCode;
                if (status < 200 || status >= 300) throw new HttpStatusException(status);
                long total = response.Content.Headers.ContentLength ?? 0;
                if (total > limit) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                bool gzip = response.Content.Headers.ContentEncoding.Contains("gzip");
                // The packed length says nothing of the unpacked one; the limit still bounds it below.
                if (gzip) total = 0;
                using var raw = await response.Content.ReadAsStreamAsync();
                using var stream = gzip ? new GZipStream(raw, CompressionMode.Decompress) : raw;
                var buffer = new byte[81920];
                var output = new MemoryStream(total > 0 ? (int)Math.Min(total, int.MaxValue) : 0);
                progress?.Invoke(0, total);
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, linked.Token);
                    if (read == 0) break;
                    if (read > limit - output.Length) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                    output.Write(buffer, 0, read);
                    progress?.Invoke(output.Length, total);
                }
                return output.ToArray();
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw new FanmadeException("DOWNLOAD_CANCELLED"); }
            catch (OperationCanceledException error) { throw new FanmadeException("NETWORK_TIMEOUT", error); }
            catch (HttpRequestException error) { throw new FanmadeException(NetworkCode(error), error); }
            catch (IOException error) { throw new FanmadeException(NetworkCode(error), error); }
        }

        // The distinct DNS / connect / TLS codes OurTaikoPlayer shows instead of one network error.
        static string NetworkCode(Exception error)
        {
            for (var e = error; e != null; e = e.InnerException)
            {
                if (e is AuthenticationException) return "TLS_CERTIFICATE_VERIFY_FAILED";
                if (e is SocketException socket)
                    switch (socket.SocketErrorCode)
                    {
                        case SocketError.HostNotFound: case SocketError.NoData: case SocketError.TryAgain: return "NETWORK_DNS_ERROR";
                        case SocketError.ConnectionRefused: case SocketError.NetworkUnreachable: case SocketError.HostUnreachable: return "NETWORK_CONNECT_ERROR";
                        case SocketError.TimedOut: return "NETWORK_TIMEOUT";
                    }
                if (e is WebException web)
                    switch (web.Status)
                    {
                        case WebExceptionStatus.NameResolutionFailure: return "NETWORK_DNS_ERROR";
                        case WebExceptionStatus.ProxyNameResolutionFailure: return "NETWORK_PROXY_DNS_ERROR";
                        case WebExceptionStatus.ConnectFailure: return "NETWORK_CONNECT_ERROR";
                        case WebExceptionStatus.Timeout: return "NETWORK_TIMEOUT";
                        case WebExceptionStatus.TrustFailure: return "TLS_CERTIFICATE_VERIFY_FAILED";
                        case WebExceptionStatus.SecureChannelFailure: return "TLS_HANDSHAKE_FAILED";
                    }
            }
            return "NETWORK_ERROR";
        }

        // 1P's account scores.
        internal List<FanmadeScore> Scores => Seats[0].Scores;
        internal Dictionary<string, FanmadeScore> Best => Seats[0].Best;
        // The catalog sent chartSlotVersion 1: blocks have ids, a course covers all its sides.
        internal static string ScoreKey(string song, string difficulty) => song + "/" + difficulty;

        internal void IndexScore(FanmadeScore score) => Seats[0].IndexScore(score);
    }
}
