using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // A player's OurTaiko account as this device remembers it: the account center's token (never
    // the password) and who it belongs to. The token slides forward on each use; the center
    // forgets it after 30 days without use.
    public sealed class OurTaikoAccount
    {
        public string Token = "", UserId = "", Username = "", Nickname = "", AvatarUrl = "";

        public bool IsSignedIn => Token.Length > 0;

        public JObject ToJson() => new JObject
        {
            ["token"] = Token, ["userId"] = UserId, ["username"] = Username, ["nickname"] = Nickname, ["avatarUrl"] = AvatarUrl,
        };

        public static OurTaikoAccount From(JToken value)
        {
            string Text(string key) => value?[key]?.Type == JTokenType.String ? (string)value[key] : "";
            var account = new OurTaikoAccount
            {
                Token = Text("token"), UserId = Text("userId"), Username = Text("username"), Nickname = Text("nickname"), AvatarUrl = Text("avatarUrl"),
            };
            return Json.HexId(account.Token, 64) && Json.HexId(account.UserId, 32) ? account : new OurTaikoAccount();
        }

        // The user object of the account center's replies.
        internal void SetUser(JToken user)
        {
            UserId = Json.Str(user, "id");
            if (!Json.HexId(UserId, 32)) throw new FanmadeException("API_USER_INVALID");
            Username = Json.Str(user, "username");
            Nickname = user["nickname"]?.Type == JTokenType.String ? (string)user["nickname"] : "";
            AvatarUrl = user["avatarUrl"]?.Type == JTokenType.String ? (string)user["avatarUrl"] : "";
        }
    }

    // The 1P and 2P accounts on this device, in one file beside settings.json. The file is
    // encrypted with a key of this device (AES-CBC and HMAC-SHA256): a copy taken to another device
    // is unreadable. It does not stop software running as the player on this device; the account
    // center's 登录设备 page signs a device out.
    public sealed class AccountStore
    {
        public const string FileName = "account.dat";
        readonly string path;
        readonly byte[] encryptionKey, macKey;
        public readonly OurTaikoAccount[] Seats = { new OurTaikoAccount(), new OurTaikoAccount() };

        // path null: kept in memory only (tests).
        public AccountStore(string path, string deviceSecret)
        {
            this.path = path;
            using var sha = SHA256.Create();
            encryptionKey = sha.ComputeHash(Encoding.UTF8.GetBytes("OurTaikoPlay account encryption\n" + deviceSecret));
            macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("OurTaikoPlay account authentication\n" + deviceSecret));
        }

        // A missing, foreign or damaged file is no account: the player signs in again. An
        // in-memory store keeps what it holds.
        public void Load()
        {
            if (path == null) return;
            for (int i = 0; i < Seats.Length; i++) Seats[i] = new OurTaikoAccount();
            try
            {
                if (!File.Exists(path)) return;
                var data = JToken.Parse(Decrypt(File.ReadAllBytes(path)));
                if (!(data["seats"] is JArray seats)) return;
                for (int i = 0; i < Seats.Length && i < seats.Count; i++) Seats[i] = OurTaikoAccount.From(seats[i]);
            }
            catch (Exception) { for (int i = 0; i < Seats.Length; i++) Seats[i] = new OurTaikoAccount(); }
        }

        public void Save()
        {
            if (path == null) return;
            var json = new JObject { ["seats"] = new JArray(Seats.Select(s => (JToken)s.ToJson())) };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, Encrypt(json.ToString(Newtonsoft.Json.Formatting.None)));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        byte[] Encrypt(string text)
        {
            using var aes = Aes.Create();
            aes.Key = encryptionKey; aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            byte[] plain = Encoding.UTF8.GetBytes(text);
            byte[] cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            byte[] body = aes.IV.Concat(cipher).ToArray();
            using var hmac = new HMACSHA256(macKey);
            return body.Concat(hmac.ComputeHash(body)).ToArray();
        }

        string Decrypt(byte[] data)
        {
            if (data.Length < 16 + 16 + 32) throw new FanmadeException("ACCOUNT_FILE_INVALID");
            byte[] body = data.Take(data.Length - 32).ToArray(), mac = data.Skip(data.Length - 32).ToArray();
            using var hmac = new HMACSHA256(macKey);
            byte[] expected = hmac.ComputeHash(body);
            int difference = 0;
            for (int i = 0; i < 32; i++) difference |= expected[i] ^ mac[i];
            if (difference != 0) throw new FanmadeException("ACCOUNT_FILE_INVALID");
            using var aes = Aes.Create();
            aes.Key = encryptionKey; aes.IV = body.Take(16).ToArray();
            using var decryptor = aes.CreateDecryptor();
            return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(body, 16, body.Length - 16));
        }
    }

    // The account center's game account routes (sso.ourtaiko.org /api/v1/game/...). The password
    // only ever goes here, never to a game server; the token it returns is traded here for a game
    // session of each server that names this account center in its catalog.
    public sealed class AccountCenter : IDisposable
    {
        public const string DefaultIssuer = "https://sso.ourtaiko.org";
        public readonly string Issuer;
        readonly HttpClient http;

        public AccountCenter(string issuer = DefaultIssuer)
        {
            Issuer = (issuer ?? DefaultIssuer).TrimEnd('/');
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public void Dispose() => http.Dispose();

        // Signs in with a username and password; the account (with its token) on success. A wrong
        // password is HTTP 401, too many attempts 429.
        public async Task<OurTaikoAccount> LoginAsync(string username, string password, string deviceName, CancellationToken cancel = default)
        {
            var body = new JObject { ["username"] = username ?? "", ["password"] = password ?? "", ["deviceName"] = deviceName ?? "" };
            var reply = await SendAsync(HttpMethod.Post, "/api/v1/game/account/login", null, body, cancel);
            var account = new OurTaikoAccount { Token = Json.Str(reply, "token") };
            if (!Json.HexId(account.Token, 64)) throw new FanmadeException("API_TOKEN_INVALID");
            account.SetUser(reply["user"]);
            return account;
        }

        // Checks a remembered account and moves its expiry forward; the nickname and avatar are
        // refreshed. HTTP 401 means the sign-in expired or was revoked.
        public async Task RefreshAsync(OurTaikoAccount account, CancellationToken cancel = default)
        {
            var reply = await SendAsync(HttpMethod.Get, "/api/v1/game/account/session", account.Token, null, cancel);
            string token = account.Token;
            account.SetUser(reply["user"]);
            account.Token = token;
        }

        public Task LogoutAsync(string token, CancellationToken cancel = default)
            => SendAsync(HttpMethod.Post, "/api/v1/game/account/logout", token, new JObject(), cancel);

        // A game session of one server (its catalog's sso.service), the same kind its own game/login gives.
        public async Task<string> TradeAsync(string token, string service, CancellationToken cancel = default)
        {
            var reply = await SendAsync(HttpMethod.Post, "/api/v1/game/tokens", token, new JObject { ["service"] = service }, cancel);
            string session = Json.Str(reply, "accessToken");
            if (!Json.HexId(session, 64)) throw new FanmadeException("API_TOKEN_INVALID");
            return session;
        }

        async Task<JObject> SendAsync(HttpMethod method, string path, string token, JObject body, CancellationToken cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            using var request = new HttpRequestMessage(method, Issuer + path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            try
            {
                using var response = await http.SendAsync(request, linked.Token);
                int status = (int)response.StatusCode;
                if (status < 200 || status >= 300) throw new HttpStatusException(status);
                string text = await response.Content.ReadAsStringAsync();
                if (text.Length > 64 * 1024) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                return Json.Parse(text);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw new FanmadeException("DOWNLOAD_CANCELLED"); }
            catch (OperationCanceledException error) { throw new FanmadeException("NETWORK_TIMEOUT", error); }
            catch (HttpRequestException error) { throw new FanmadeException("NETWORK_ERROR", error); }
            catch (IOException error) { throw new FanmadeException("NETWORK_ERROR", error); }
        }
    }
}
