using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // A local stand-in for the Fanmade backend's game API (Fanmade/backend internal/httpapi
    // server.go + game.go), as tests/fanmade/fixture.py is for OurTaikoPlayer: login, bootstrap,
    // category lists, chart details, signed resource manifests (served by the fixture itself
    // unless a test replaces Manifest) and idempotent score submission. It is also the account
    // center its catalog names (OurTaikoSSO's /api/v1/game/account/* and /api/v1/game/tokens).
    public sealed class FanmadeFixture : IDisposable
    {
        public sealed class Chart
        {
            public string Id = Hex(32), Title = "Fixture Song", Subtitle = "", Maker = "Tester";
            public string Encoding, AudioName = "song.ogg";
            public byte[] Tja = new byte[0], Audio = new byte[0];
            public string[] Categories = { "game" };
            // Base course, level and #START player ("" for the single block, "P1" or "P2").
            public List<(string Course, int Level, string Player)> Difficulties =
                new List<(string, int, string)> { ("Oni", 8, "") };
            static long nextBlock;
            readonly long firstBlock = Interlocked.Add(ref nextBlock, 100);
            // The id the API gives a block, as scores name it.
            public long BlockId(string course, string player = "") => firstBlock + Difficulties.FindIndex(d => d.Course == course && d.Player == player);
            public static string Side(string player) => player == "P1" ? "p1" : player == "P2" ? "p2" : "single";
            public double Bpm = 120, DemoStart, Offset;
            // Blocks (Oni, or Oni_1p / Oni_2p for a side) reported with "branching": true; the rest are false.
            public readonly HashSet<string> Branching = new HashSet<string>();

            // A block's content hash as a server would report it: "v1:" + 64 hex, changing with the TJA.
            public string ChartHash(string course, string player)
                => "v1:" + Sha(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(Tja) + "\n" + course + "/" + player));

            public JObject ToJson()
            {
                var value = new JObject
                {
                    ["id"] = IdToken(Id), ["title"] = Title, ["subtitle"] = Subtitle, ["maker"] = Maker,
                    ["categoryIds"] = new JArray(Categories),
                    ["audioHash"] = Sha(Audio), ["audioName"] = AudioName,
                    ["titleTranslations"] = new JObject { ["ja"] = Title + " JA" }, ["subtitleTranslations"] = new JObject(),
                    ["bpm"] = Bpm, ["demoStart"] = DemoStart, ["offset"] = Offset,
                    ["difficulties"] = new JArray(Difficulties.Select(d => new JObject
                    {
                        ["id"] = BlockId(d.Course, d.Player), ["course"] = d.Course, ["side"] = Side(d.Player), ["level"] = d.Level, ["maker"] = Maker,
                        ["branching"] = Branching.Contains(d.Course + (d.Player == "P1" ? "_1p" : d.Player == "P2" ? "_2p" : "")),
                        // The server's content hash of the block: here one that follows the TJA bytes.
                        ["chartHash"] = ChartHash(d.Course, d.Player),
                    })),
                };
                if (Encoding != null) value["encoding"] = Encoding;
                return value;
            }
        }

        // A chart ID of digits only goes out as a JSON number, as OurTaiko Database sends it.
        public static JToken IdToken(string id) => long.TryParse(id, out long n) ? (JToken)n : id;

        public readonly string BaseUrl;
        public readonly Dictionary<string, string> Users = new Dictionary<string, string> { ["don"] = "katsu" };
        public readonly List<(string Id, string Title, string Genre)> Categories = new List<(string, string, string)>
        {
            ("game", "Game", "GAME"), ("pop", "Pop", "J-POP"),
        };
        public readonly List<Chart> Charts = new List<Chart>();
        // Bootstrap capability fields; a test drops one to play an outdated server.
        public readonly JObject Protocol = new JObject
        {
            ["chartSlotVersion"] = 1, ["resourceDownloadVersion"] = 1,
            ["scoreReplayVersion"] = 1, ["audioPreviewVersion"] = 1,
        };
        // The catalog's "sso": this fixture is the account center and "fixture" the service. False:
        // a server that only takes a password.
        public bool AccountCenter = true;
        public const string Service = "fixture";
        // GET /game/dans; null: a server without 段位 courses (no danVersion in the catalog).
        public JArray Dans;
        // POST /game/dan-attempts: what was sent (body and user), and OurTaikoProfile's reply the
        // server passes back (null: Profile unreachable).
        public readonly ConcurrentQueue<(string User, JObject Body)> DanAttempts = new ConcurrentQueue<(string, JObject)>();
        public JObject DanProfileReply = JObject.Parse("{\"granted\":{\"dan\":null,\"titles\":[]},\"nameplate\":{}}");
        // OurTaikoProfile's public nameplates by user id (GET /api/v1/users/{id}/nameplate); a user
        // without one has the plain plate.
        public readonly ConcurrentDictionary<string, JObject> Nameplates = new ConcurrentDictionary<string, JObject>();
        // Account sign-ins issued by /api/v1/game/account/login and still valid.
        public readonly ConcurrentDictionary<string, string> AccountTokens = new ConcurrentDictionary<string, string>();
        // The account center's user id of a fixture user.
        public static string UserId(string username) => FanmadeEndpoint.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(username)).Substring(0, 32);
        public Func<Chart, JObject> Manifest;
        public Action<JObject> ChangeBootstrap;
        public Func<HttpListenerContext, bool> CustomRequest;
        // Status codes returned (and consumed) before the next score submissions succeed.
        public readonly ConcurrentQueue<int> ScoreFailures = new ConcurrentQueue<int>();
        public readonly ConcurrentQueue<JObject> AcceptedScores = new ConcurrentQueue<JObject>();
        // Who sent each accepted score, in the same order.
        public readonly ConcurrentQueue<string> AcceptedUsers = new ConcurrentQueue<string>();
        public readonly ConcurrentDictionary<string, JObject> Idempotent = new ConcurrentDictionary<string, JObject>();
        public readonly ConcurrentBag<string> Requests = new ConcurrentBag<string>();
        public readonly List<JObject> AccountScores = new List<JObject>();
        // GET /game/playlists for any signed-in account.
        public JArray AccountPlaylists = new JArray();
        public int Downloads;
        // Paths answered with this status instead of their normal reply.
        public readonly ConcurrentDictionary<string, int> Fail = new ConcurrentDictionary<string, int>();

        readonly HttpListener listener = new HttpListener();
        readonly ConcurrentDictionary<string, string> tokens = new ConcurrentDictionary<string, string>();
        readonly Thread thread;
        volatile bool stopped;

        public FanmadeFixture()
        {
            int port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            listener.Prefixes.Add(BaseUrl + "/");
            listener.Start();
            thread = new Thread(Serve) { IsBackground = true, Name = "FanmadeFixture" };
            thread.Start();
            Manifest = DefaultManifest;
        }

        // Links to the fixture's own /files/<chart>/<kind>, as the backend signs bucket URLs.
        JObject DefaultManifest(Chart chart)
        {
            JObject Resource(string kind, byte[] bytes, string type) => new JObject
            {
                ["url"] = BaseUrl + "/files/" + chart.Id + "/" + kind + "?signature=get", ["headUrl"] = BaseUrl + "/files/" + chart.Id + "/" + kind + "?signature=head",
                ["sha256"] = Sha(bytes), ["size"] = bytes.Length, ["contentType"] = type,
            };
            string audioType = chart.AudioName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" : "audio/ogg";
            return new JObject
            {
                ["chartId"] = IdToken(chart.Id),
                ["resources"] = new JObject { ["tja"] = Resource("tja", chart.Tja, "application/octet-stream"), ["audio"] = Resource("audio", chart.Audio, audioType) },
            };
        }

        public ServerConfig Server(string username = "", string password = "", string name = "Fixture") => new ServerConfig
        {
            name = name, baseUrl = BaseUrl, username = username, password = password, enabled = true,
        };

        // A playable chart: `measures` 4/4 measures of don at the given BPM.
        public static string SimpleTja(string course = "Oni", int measures = 1, double bpm = 240)
            => $"TITLE:Original Title\nSUBTITLE:--old\nWAVE:old.ogg\nBPM:{bpm}\nOFFSET:0\nCOURSE:{course}\nLEVEL:3\n#START\n"
               + string.Concat(Enumerable.Repeat("1111,\n", measures)) + "#END\n";

        public void Invalidate() => tokens.Clear();  // every issued token expires

        public void Dispose()
        {
            stopped = true;
            try { listener.Stop(); listener.Close(); } catch (ObjectDisposedException) { }
        }

        static int FreePort()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }

        void Serve()
        {
            while (!stopped)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch (Exception) { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(context));
            }
        }

        void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            string path = request.Url.AbsolutePath;
            Requests.Add(request.HttpMethod + " " + path);
            try
            {
                if (CustomRequest?.Invoke(context) == true) return;
                if (Fail.TryGetValue(path, out int failure)) { Reply(context, failure, new JObject { ["error"] = "FIXTURE" }); return; }
                string body = new StreamReader(request.InputStream, Encoding.UTF8).ReadToEnd();
                string auth = request.Headers["Authorization"] ?? "";
                if (path.StartsWith("/api/v1/game/account/", StringComparison.Ordinal) || path == "/api/v1/game/tokens")
                { Account(context, path, auth, body); return; }
                if (request.HttpMethod == "GET" && path.StartsWith("/api/v1/users/", StringComparison.Ordinal) && path.EndsWith("/nameplate", StringComparison.Ordinal))
                {
                    string id = path.Substring("/api/v1/users/".Length, path.Length - "/api/v1/users/".Length - "/nameplate".Length);
                    Reply(context, 200, Nameplates.TryGetValue(id, out var plate) ? plate
                        : new JObject { ["userId"] = id, ["nickname"] = "", ["title"] = null, ["dan"] = null, ["showDan"] = true });
                    return;
                }
                string user = null;
                if (auth.StartsWith("Bearer ", StringComparison.Ordinal) && !tokens.TryGetValue(auth.Substring(7), out user))
                { Reply(context, 401, new JObject { ["error"] = "SESSION" }); return; }
                var parts = path.Trim('/').Split('/');
                if (request.HttpMethod == "POST" && path == "/api/v1/game/login") { Login(context, JObject.Parse(body)); return; }
                if (request.HttpMethod == "GET" && path == "/api/v1/game/catalog") { Bootstrap(context); return; }
                if (request.HttpMethod == "GET" && path == "/api/v1/game/search")
                {
                    var q = new SongSearchQuery(request.QueryString["q"]);
                    string course = request.QueryString["course"];
                    int.TryParse(request.QueryString["level"], out int level);
                    var matches = Charts.Where(c => (q.MatchesText(c.Title) || q.MatchesText(c.Title + " JA") || q.MatchesText(c.Subtitle) || q.MatchesText(c.Maker))
                        && c.Difficulties.Any(d => (string.IsNullOrEmpty(course) || d.Course == course) && (level == 0 || d.Level == level)))
                        .Select(c => c.ToJson()).ToArray();
                    Reply(context, 200, new JObject { ["items"] = new JArray(matches), ["total"] = matches.Length }); return;
                }
                if (request.HttpMethod == "GET" && parts.Length == 6 && parts[3] == "categories" && parts[5] == "charts")
                {
                    var charts = Charts.Where(c => c.Categories.Contains(parts[4])).Select(c => c.ToJson());
                    Reply(context, 200, new JObject { ["categoryId"] = parts[4], ["charts"] = new JArray(charts) });
                    return;
                }
                if (request.HttpMethod == "GET" && parts.Length == 4 && parts[2] == "charts")
                {
                    var chart = Charts.Find(c => c.Id == parts[3]);
                    if (chart == null) Reply(context, 404, new JObject());
                    else Reply(context, 200, chart.ToJson());
                    return;
                }
                if (request.HttpMethod == "GET" && parts.Length == 5 && parts[2] == "charts")
                {
                    var chart = Charts.Find(c => c.Id == parts[3]);
                    if (chart == null) { Reply(context, 404, new JObject()); return; }
                    if (parts[4] == "resources") { Reply(context, 200, Manifest(chart)); return; }
                }
                if (request.HttpMethod == "GET" && parts.Length == 3 && parts[0] == "files" && (parts[2] == "tja" || parts[2] == "audio"))
                {
                    // Signed links carry no API credentials.
                    if (!string.IsNullOrEmpty(auth)) { Reply(context, 400, new JObject()); return; }
                    var chart = Charts.Find(c => c.Id == parts[1]);
                    if (chart == null) { Reply(context, 404, new JObject()); return; }
                    Interlocked.Increment(ref Downloads);
                    Bytes(context, parts[2] == "tja" ? chart.Tja : chart.Audio);
                    return;
                }
                if (request.HttpMethod == "GET" && path == "/api/v1/game/scores")
                {
                    if (user == null) { Reply(context, 401, new JObject()); return; }
                    lock (AccountScores) Reply(context, 200, new JObject { ["scores"] = new JArray(AccountScores.Where(s => (string)s["user"] == user)) });
                    return;
                }
                if (request.HttpMethod == "GET" && path == "/api/v1/game/playlists")
                {
                    if (user == null) { Reply(context, 401, new JObject()); return; }
                    Reply(context, 200, new JObject { ["playlists"] = AccountPlaylists, ["playlistVersion"] = 1 });
                    return;
                }
                if (request.HttpMethod == "POST" && path == "/api/v1/game/scores") { Score(context, user, request.Headers["Idempotency-Key"], JObject.Parse(body)); return; }
                if (request.HttpMethod == "GET" && path == "/api/v1/game/dans")
                {
                    if (Dans == null) Reply(context, 404, new JObject());
                    else Reply(context, 200, new JObject { ["dans"] = Dans, ["danVersion"] = 1 });
                    return;
                }
                if (request.HttpMethod == "POST" && path == "/api/v1/game/dan-attempts")
                {
                    if (user == null) { Reply(context, 401, new JObject()); return; }
                    if (string.IsNullOrEmpty(request.Headers["Idempotency-Key"])) { Reply(context, 400, new JObject()); return; }
                    var attempt = JObject.Parse(body);
                    DanAttempts.Enqueue((user, attempt));
                    Reply(context, 200, new JObject
                    {
                        ["attempt"] = new JObject { ["id"] = Hex(32), ["courseId"] = attempt["courseId"], ["verdict"] = attempt["verdict"] },
                        ["profile"] = DanProfileReply ?? (JToken)JValue.CreateNull(),
                        ["profileError"] = DanProfileReply == null ? "OurTaikoProfile unreachable" : null,
                    });
                    return;
                }
                Reply(context, 404, new JObject());
            }
            catch (Exception error) { Reply(context, 500, new JObject { ["error"] = error.Message }); }
        }

        void Login(HttpListenerContext context, JObject body)
        {
            string username = (string)body["username"], password = (string)body["password"];
            if (!Users.TryGetValue(username ?? "", out var expected) || expected != password)
            { Reply(context, 401, new JObject { ["code"] = "LOGIN_INVALID" }); return; }
            string token = Hex(64);
            tokens[token] = username;
            Reply(context, 200, new JObject
            {
                ["user"] = new JObject { ["id"] = "u-" + username, ["username"] = username, ["nickname"] = username.ToUpperInvariant() },
                ["accessToken"] = token, ["expiresIn"] = 3600,
            });
        }

        // The account center: sign in once, check, sign out, and trade the sign-in for a session here.
        void Account(HttpListenerContext context, string path, string auth, string body)
        {
            string bearer = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth.Substring(7) : "";
            AccountTokens.TryGetValue(bearer, out string username);
            JObject User(string name) => new JObject
            {
                ["id"] = UserId(name), ["username"] = name, ["nickname"] = name.ToUpperInvariant(), ["avatarUrl"] = "", ["preferredLanguage"] = "en",
            };
            switch (path)
            {
                case "/api/v1/game/account/login":
                {
                    var request = JObject.Parse(body);
                    string name = (string)request["username"], password = (string)request["password"];
                    if (!Users.TryGetValue(name ?? "", out var expected) || expected != password)
                    { Reply(context, 401, new JObject { ["code"] = "LOGIN_INVALID" }); return; }
                    string token = Hex(64);
                    AccountTokens[token] = name;
                    Reply(context, 200, new JObject { ["token"] = token, ["expiresAt"] = "2099-01-01T00:00:00+00:00", ["user"] = User(name) });
                    return;
                }
                case "/api/v1/game/account/session":
                    if (username == null) Reply(context, 401, new JObject { ["code"] = "UNAUTHORIZED" });
                    else Reply(context, 200, new JObject { ["expiresAt"] = "2099-01-01T00:00:00+00:00", ["user"] = User(username) });
                    return;
                case "/api/v1/game/account/logout":
                    AccountTokens.TryRemove(bearer, out _);
                    Reply(context, 200, new JObject { ["ok"] = true });
                    return;
                default:
                {
                    if (username == null) { Reply(context, 401, new JObject { ["code"] = "UNAUTHORIZED" }); return; }
                    if ((string)JObject.Parse(body)["service"] != Service) { Reply(context, 403, new JObject { ["code"] = "SERVICE_FORBIDDEN" }); return; }
                    string token = Hex(64);
                    tokens[token] = username;
                    Reply(context, 200, new JObject { ["user"] = User(username), ["accessToken"] = token, ["expiresIn"] = 3600 });
                    return;
                }
            }
        }

        // Every game session (traded or logged in) ends, as when they expire: the next request is 401.
        public void ExpireSessions() => tokens.Clear();

        // GET /game/catalog: public, the same for every caller.
        void Bootstrap(HttpListenerContext context)
        {
            var categories = new JArray(Categories.Select(c => new JObject
            {
                ["id"] = c.Id, ["title"] = c.Title, ["genre"] = c.Genre, ["chartCount"] = Charts.Count(x => x.Categories.Contains(c.Id)),
            }));
            var reply = new JObject
            {
                ["categories"] = categories, ["chartCount"] = Charts.Count,
                ["charts"] = new JArray(Charts.Select(c => c.ToJson())),
            };
            reply.Merge(Protocol);
            if (AccountCenter) reply["sso"] = new JObject { ["issuer"] = BaseUrl, ["service"] = Service };
            if (Dans != null) reply["danVersion"] = 1;
            ChangeBootstrap?.Invoke(reply);
            Reply(context, 200, reply);
        }

        void Score(HttpListenerContext context, string user, string key, JObject body)
        {
            if (user == null) { Reply(context, 401, new JObject()); return; }
            if (string.IsNullOrEmpty(key)) { Reply(context, 400, new JObject()); return; }
            if (Idempotent.TryGetValue(key, out var existing)) { Reply(context, 200, existing); return; }
            if (ScoreFailures.TryDequeue(out int status)) { Reply(context, status, new JObject()); return; }
            if (body.ContainsKey("versionId")) { Reply(context, 400, new JObject()); return; }
            if (body["max_combo"]?.Type != JTokenType.Integer) { Reply(context, 400, new JObject()); return; }
            if (body["difficultyId"]?.Type != JTokenType.Integer) { Reply(context, 400, new JObject()); return; }
            var chart = Charts.Find(c => c.Difficulties.Any(d => c.BlockId(d.Course, d.Player) == (long)body["difficultyId"]));
            if (chart == null) { Reply(context, 404, new JObject()); return; }
            var block = chart.Difficulties.Find(d => chart.BlockId(d.Course, d.Player) == (long)body["difficultyId"]);
            var score = new JObject
            {
                ["id"] = Hex(32), ["songId"] = IdToken(chart.Id), ["difficultyId"] = body["difficultyId"],
                ["course"] = block.Course, ["side"] = Chart.Side(block.Player), ["isLatest"] = true,
                ["good"] = body["good"], ["ok"] = body["ok"], ["bad"] = body["bad"], ["score"] = body["score"],
                ["drumroll"] = body["drumroll"], ["max_combo"] = body["max_combo"],
                ["ClearStatus"] = body["ClearStatus"] ?? new JValue(0),
            };
            Idempotent[key] = score;
            AcceptedUsers.Enqueue(user);
            AcceptedScores.Enqueue(body);
            Reply(context, 200, score);
        }

        // A stored play of the chart's block: a course's single one, or "P1" / "P2".
        public void AddAccountScore(string user, Chart chart, string course, long score, int clearStatus = 0, string player = "", bool latest = true)
        {
            lock (AccountScores)
                AccountScores.Add(new JObject
                {
                    ["user"] = user, ["id"] = Hex(32), ["songId"] = IdToken(chart.Id), ["difficultyId"] = chart.BlockId(course, player),
                    ["course"] = course, ["side"] = Chart.Side(player), ["isLatest"] = latest,
                    ["good"] = 1, ["ok"] = 0, ["bad"] = 0, ["score"] = score, ["drumroll"] = 0, ["max_combo"] = 1,
                    ["ClearStatus"] = clearStatus,
                });
        }

        // Gzip packs JSON replies for a client that asks for it, as OurTaiko Database does.
        public bool Gzip;
        public int GzippedReplies;

        void Reply(HttpListenerContext context, int status, JToken body)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
            if (Gzip && (context.Request.Headers["Accept-Encoding"] ?? "").Contains("gzip"))
            {
                using var packed = new MemoryStream();
                using (var zip = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Optimal, true)) zip.Write(bytes, 0, bytes.Length);
                bytes = packed.ToArray();
                context.Response.AddHeader("Content-Encoding", "gzip");
                Interlocked.Increment(ref GzippedReplies);
            }
            Bytes(context, bytes, status);
        }

        static void Bytes(HttpListenerContext context, byte[] bytes, int status = 200)
        {
            try
            {
                context.Response.StatusCode = status;
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception) { }
        }

        // A block's content hash as a server would report it: "v1:" + 64 hex, changing with the TJA.



        public static string Sha(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        static string Hex(int length)
        {
            var bytes = new byte[length / 2];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}
