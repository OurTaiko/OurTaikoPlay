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
    // category lists, chart details, file downloads and idempotent score submission.
    public sealed class FanmadeFixture : IDisposable
    {
        public sealed class Chart
        {
            public string Id = Hex(32), Version = Hex(32), Title = "Fixture Song", Subtitle = "", Maker = "Tester";
            public string Encoding = "utf-8", AudioName = "song.ogg";
            public byte[] Tja = new byte[0], Audio = new byte[0];
            public string[] Categories = { "game" };
            // course, level, blockIndex, cloud, player
            public List<(string Course, int Level, int Block, bool Cloud, string Player)> Difficulties =
                new List<(string, int, int, bool, string)> { ("Oni", 8, 0, true, "") };
            public double Bpm = 120, DemoStart;

            public JObject ToJson() => new JObject
            {
                ["id"] = Id, ["versionId"] = Version, ["title"] = Title, ["subtitle"] = Subtitle, ["maker"] = Maker,
                ["tjaHash"] = Sha(Tja), ["audioHash"] = Sha(Audio), ["encoding"] = Encoding, ["audioName"] = AudioName,
                ["titleTranslations"] = new JObject { ["ja"] = Title + " JA" }, ["subtitleTranslations"] = new JObject(),
                ["bpm"] = Bpm, ["demoStart"] = DemoStart,
                ["difficulties"] = new JArray(Difficulties.Select(d => new JObject
                {
                    ["course"] = d.Course, ["level"] = d.Level, ["blockIndex"] = d.Block, ["cloudScoreEligible"] = d.Cloud, ["player"] = d.Player,
                })),
            };
        }

        public readonly string BaseUrl;
        public readonly Dictionary<string, string> Users = new Dictionary<string, string> { ["don"] = "katsu" };
        public readonly List<(string Id, string Title, string Genre)> Categories = new List<(string, string, string)>
        {
            ("game", "Game", "GAME"), ("pop", "Pop", "J-POP"),
        };
        public readonly List<Chart> Charts = new List<Chart>();
        public int ScoreReplayVersion = 1;
        // Status codes returned (and consumed) before the next score submissions succeed.
        public readonly ConcurrentQueue<int> ScoreFailures = new ConcurrentQueue<int>();
        public readonly ConcurrentQueue<JObject> AcceptedScores = new ConcurrentQueue<JObject>();
        public readonly ConcurrentDictionary<string, JObject> Idempotent = new ConcurrentDictionary<string, JObject>();
        public readonly ConcurrentBag<string> Requests = new ConcurrentBag<string>();
        public readonly List<JObject> AccountScores = new List<JObject>();
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
                if (Fail.TryGetValue(path, out int failure)) { Reply(context, failure, new JObject { ["error"] = "FIXTURE" }); return; }
                string body = new StreamReader(request.InputStream, Encoding.UTF8).ReadToEnd();
                string auth = request.Headers["Authorization"] ?? "";
                string user = null;
                if (auth.StartsWith("Bearer ", StringComparison.Ordinal) && !tokens.TryGetValue(auth.Substring(7), out user))
                { Reply(context, 401, new JObject { ["error"] = "SESSION" }); return; }
                var parts = path.Trim('/').Split('/');
                if (request.HttpMethod == "POST" && path == "/api/v1/game/login") { Login(context, JObject.Parse(body)); return; }
                if (request.HttpMethod == "GET" && path == "/api/v1/game/bootstrap") { Bootstrap(context, user); return; }
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
                if (request.HttpMethod == "GET" && parts.Length == 7 && parts[4] == "versions")
                {
                    var chart = Charts.Find(c => c.Id == parts[3] && c.Version == parts[5]);
                    if (chart == null) { Reply(context, 404, new JObject()); return; }
                    Interlocked.Increment(ref Downloads);
                    Bytes(context, parts[6] == "tja" ? chart.Tja : chart.Audio);
                    return;
                }
                if (request.HttpMethod == "POST" && path == "/api/v1/game/scores") { Score(context, user, request.Headers["Idempotency-Key"], JObject.Parse(body)); return; }
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

        void Bootstrap(HttpListenerContext context, string user)
        {
            var categories = new JArray(Categories.Select(c => new JObject
            {
                ["id"] = c.Id, ["title"] = c.Title, ["genre"] = c.Genre, ["chartCount"] = Charts.Count(x => x.Categories.Contains(c.Id)),
            }));
            JArray scores;
            lock (AccountScores) scores = user == null ? new JArray() : new JArray(AccountScores.Where(s => (string)s["user"] == user));
            var reply = new JObject
            {
                ["user"] = user == null ? null : new JObject { ["username"] = user },
                ["categories"] = categories, ["chartCount"] = Charts.Count, ["scores"] = scores,
            };
            if (ScoreReplayVersion > 0) reply["scoreReplayVersion"] = ScoreReplayVersion;
            Reply(context, 200, reply);
        }

        void Score(HttpListenerContext context, string user, string key, JObject body)
        {
            if (user == null) { Reply(context, 401, new JObject()); return; }
            if (string.IsNullOrEmpty(key)) { Reply(context, 400, new JObject()); return; }
            if (Idempotent.TryGetValue(key, out var existing)) { Reply(context, 200, existing); return; }
            if (ScoreFailures.TryDequeue(out int status)) { Reply(context, status, new JObject()); return; }
            if (body["max_combo"]?.Type != JTokenType.Integer) { Reply(context, 400, new JObject()); return; }
            var score = new JObject
            {
                ["id"] = Hex(32), ["songId"] = body["songId"], ["versionId"] = body["versionId"], ["difficulty"] = body["difficulty"],
                ["good"] = body["good"], ["ok"] = body["ok"], ["bad"] = body["bad"], ["score"] = body["score"],
                ["drumroll"] = body["drumroll"], ["max_combo"] = body["max_combo"],
                ["ClearStatus"] = body["ClearStatus"] ?? new JValue(0),
            };
            Idempotent[key] = score;
            AcceptedScores.Enqueue(body);
            Reply(context, 200, score);
        }

        public void AddAccountScore(string user, Chart chart, string course, long score, int clearStatus = 0)
        {
            lock (AccountScores)
                AccountScores.Add(new JObject
                {
                    ["user"] = user, ["id"] = Hex(32), ["songId"] = chart.Id, ["versionId"] = chart.Version, ["difficulty"] = course,
                    ["good"] = 1, ["ok"] = 0, ["bad"] = 0, ["score"] = score, ["drumroll"] = 0, ["max_combo"] = 1,
                    ["ClearStatus"] = clearStatus,
                });
        }

        static void Reply(HttpListenerContext context, int status, JToken body)
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            Bytes(context, Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)), status);
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
