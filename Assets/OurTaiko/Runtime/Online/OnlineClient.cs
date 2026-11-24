using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // fanmade.cpp Client without rendering or Unity state, so it can be tested against a fixture
    // server. One catalog request loads every category and chart when a server connects;
    // song select shows each category as a folder without further catalog requests.
    //
    // Cache layout under the root: objects/<first two hex digits>/<sha256>, one verified file per
    // content hash (TJA, audio or preview), shared by every server, account and chart. Paths stay
    // short enough for Windows' 260-character limit. The playable TJA is never written to disk.
    // Pending uploads live in the separate PendingScoreUploads SQLite table.
    public sealed partial class OnlineClient : IDisposable
    {
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
        // How long a 段位 attempt the server kept, but could not get OurTaikoProfile's answer for, is sent again.
        public static readonly TimeSpan DanAwardRetryPeriod = TimeSpan.FromDays(7);
        public readonly string CacheRoot;
        public string Status { get { lock (sync) return status; } }
        // Bumped whenever charts or scores change, so views know to refresh.
        public long Revision => Interlocked.Read(ref revision);
        public bool IsUploading => uploads != null && !uploads.IsCompleted;

        readonly object sync = new object();
        readonly List<OnlineEndpoint> endpoints = new List<OnlineEndpoint>();
        readonly Dictionary<string, List<OnlineChart>> charts = new Dictionary<string, List<OnlineChart>>();
        readonly Dictionary<string, List<OnlineCategory>> categoryLists = new Dictionary<string, List<OnlineCategory>>();
        readonly Dictionary<string, List<OnlinePlaylist>> playlistLists = new Dictionary<string, List<OnlinePlaylist>>();
        // The signed-in account's own playlists: as received at login, and read against the catalog
        // once both are there (either may arrive first).
        readonly Dictionary<string, JToken> personalPlaylists = new Dictionary<string, JToken>();
        readonly Dictionary<string, List<OnlinePlaylist>> personalLists = new Dictionary<string, List<OnlinePlaylist>>();
        // Each server's 段位 courses (GET /game/dans), as it sent them.
        readonly Dictionary<string, JArray> danLists = new Dictionary<string, JArray>();
        // What each uploaded 段位 attempt (by its queue key) got back: OurTaikoProfile's reply, or why it has none.
        readonly Dictionary<string, DanAward> danAwards = new Dictionary<string, DanAward>();
        string status = "";
        long revision;
        Task uploads;
        DateTime retryAt = DateTime.MinValue;
        // Work queued while a pass runs: that pass goes round once more instead of leaving it for the next one.
        volatile bool rerun;

        public PendingScoreQueue UploadQueue { get; }
        public PendingDanQueue DanQueue { get; }
        // The charts prepared so far, playable while their server is not connected.
        public OfflineChartStore Offline { get; }
        // What each played song's full audio measured, for its preview to sound the same.
        public AudioLevelStore AudioLevels { get; }
        public OnlineClient(string cacheRoot, string databasePath = null)
        {
            CacheRoot = Path.GetFullPath(cacheRoot);
            var database = new GameDatabase(databasePath ?? Path.Combine(CacheRoot, GameDatabase.FileName));
            UploadQueue = new PendingScoreQueue(database);
            DanQueue = new PendingDanQueue(database);
            Offline = new OfflineChartStore(database);
            AudioLevels = new AudioLevelStore(database);
        }

        // A 段位 attempt's answer: Profile is OurTaikoProfile's reply ({granted, nameplate}) or null
        // when the server could not reach it (Error says why).
        public sealed class DanAward
        {
            public JObject Profile;
            public string Error = "";
        }

        // Every connected server's 段位 courses: its base URL and the courses as GET /game/dans lists them.
        public IReadOnlyList<(string Server, JArray Dans)> Dans
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => danLists.ContainsKey(e.Id)).Select(e => (e.Config.baseUrl, danLists[e.Id])).ToArray();
            }
        }

        public IReadOnlyList<OnlineEndpoint> Endpoints { get { lock (sync) return endpoints.ToArray(); } }

        // Every chart of every connected server, in server then category order, one entry per chart id.
        public IReadOnlyList<OnlineChart> Charts
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => charts.ContainsKey(e.Id)).SelectMany(e => charts[e.Id]).ToArray();
            }
        }

        // Every category of every connected server, in server then catalog order (empty ones included).
        public IReadOnlyList<OnlineCategory> Categories
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => categoryLists.ContainsKey(e.Id)).SelectMany(e => categoryLists[e.Id]).ToArray();
            }
        }

        // Every playlist of every connected server: per server the account's own (owned, favorite,
        // personal system ones) and then the catalog's, each in the server's order. Servers without
        // playlists have none; a playlist without a playable catalog chart is left out.
        public IReadOnlyList<OnlinePlaylist> Playlists
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => playlistLists.ContainsKey(e.Id)).SelectMany(e =>
                        personalLists.TryGetValue(e.Id, out var own) ? own.Concat(playlistLists[e.Id]) : playlistLists[e.Id]).ToArray();
            }
        }

        public OnlineChart Chart(string server, string id)
        {
            lock (sync) return charts.TryGetValue(server, out var list) ? list.Find(c => c.Id == id) : null;
        }

        public OnlineEndpoint Endpoint(string id) { lock (sync) return endpoints.Find(e => e.Id == id); }

        // The endpoint of a server whose catalog is loaded now; null otherwise. A server that is
        // listed but not connected (unreachable at start-up, start-up skipped) has
        // no catalog to play from, so its charts play from the offline cache as if it were absent.
        OnlineEndpoint ConnectedEndpoint(string id) { lock (sync) return endpoints.Find(e => e.Id == id && e.IsConnected); }

        void SetStatus(string text) { lock (sync) status = text; }

        // Drops every server and its catalog (uploads finish first). Pending scores stay on disk.
        public void Reset()
        {
            try { uploads?.Wait(); } catch (AggregateException) { }
            uploads = null;
            lock (sync)
            {
                ResetRankThresholds();
                foreach (var e in endpoints) e.Dispose();
                endpoints.Clear(); charts.Clear(); categoryLists.Clear(); playlistLists.Clear(); danLists.Clear();
                personalPlaylists.Clear(); personalLists.Clear();
                retryAt = DateTime.MinValue;
            }
            Interlocked.Increment(ref revision);
        }

        // A second entry for a connected server is reused.
        public OnlineEndpoint Add(ServerConfig config)
        {
            var endpoint = new OnlineEndpoint(config);
            lock (sync)
            {
                int existing = endpoints.FindIndex(e => e.Id == endpoint.Id);
                if (existing >= 0 && endpoints[existing].IsConnected) { endpoint.Dispose(); return endpoints[existing]; }
                // One that has not connected is replaced.
                if (existing >= 0) { endpoints[existing].Dispose(); endpoints.RemoveAt(existing); }
                endpoints.Add(endpoint);
            }
            return endpoint;
        }

        // Drops the matching servers and their catalogs (uploads finish first); the others stay connected.
        public void Remove(Predicate<OnlineEndpoint> match)
        {
            try { uploads?.Wait(); } catch (AggregateException) { }
            uploads = null;
            lock (sync)
            {
                foreach (var e in endpoints.FindAll(match))
                {
                    charts.Remove(e.Id); categoryLists.Remove(e.Id); playlistLists.Remove(e.Id); danLists.Remove(e.Id);
                    personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id);
                    e.Dispose();
                }
                endpoints.RemoveAll(match);
            }
            Interlocked.Increment(ref revision);
        }

        // GET /game/scores (and, for 1P, GET /game/playlists) of a signed-in seat, with the caller
        // holding the endpoint's transport. 2P's own playlists are not shown: the folders are 1P's.
        async Task LoadAccountDataAsync(OnlineEndpoint e, int seat, CancellationToken cancel)
        {
            var scores = new List<OnlineScore>();
            JToken playlists = null;
            try
            {
                if (e.Seats[seat].IsAuthenticated)
                {
                    scores = ReadScores(Json.Parse(await e.AuthorizedAsync("/api/v2/game/scores", cancel: cancel, seat: seat)));
                    if (seat == 0) playlists = Json.Parse(await e.AuthorizedAsync("/api/v2/game/playlists", cancel: cancel))["playlists"];
                }
            }
            catch (Exception error)
            {
                SetStatus(e.Config.DisplayName + ": " + error.Message);
                if (!e.Seats[seat].IsAuthenticated) SetAccountData(e, seat, scores, null);
                throw;
            }
            SetAccountData(e, seat, scores, playlists);
        }

        // The scores of an answer of GET /game/scores.
        static List<OnlineScore> ReadScores(JToken reply)
        {
            if (!(reply?["scores"] is JArray scoreList)) throw new OnlineException("API_SCORES_INVALID");
            var scores = new List<OnlineScore>();
            foreach (var v in scoreList) scores.Add(OnlineScore.From(v));
            return scores;
        }

        // The servers that are sources of one site are loaded together: one request to the site
        // answers for all of them what each would answer by itself (GET /api/v2/game/sources).
        // The part of `answer` for an endpoint, by the name the site knows the source by.
        static JToken SourceOf(JToken answer, OnlineEndpoint e)
        {
            if (answer?["sources"] is JArray sources)
                foreach (var source in sources)
                    if (Json.Str(source, "id") == e.Config.SourceId) return source;
            throw new OnlineException("API_SOURCES_INVALID");
        }

        // Loads the song lists and 段位 courses of every endpoint of a site in one request;
        // accounts and scores are untouched. The request failing fails them all and keeps what
        // each had; otherwise each endpoint's own failure is reported in the result (null: loaded).
        public async Task<Exception[]> LoadSiteAsync(IReadOnlyList<OnlineEndpoint> site, CancellationToken cancel = default)
        {
            if (site.Count == 0) return Array.Empty<Exception>();
            foreach (var e in site) if (!e.HasValidUrl()) throw new OnlineException("SERVER_URL_INVALID");
            var failures = new Exception[site.Count];
            var held = new List<OnlineEndpoint>();
            try
            {
                foreach (var e in site) { await e.Transport.WaitAsync(cancel); held.Add(e); }
                JToken answer;
                try
                {
                    foreach (var e in site) SetStatus(e.Config.DisplayName + ": loading catalog");
                    answer = Json.Parse(await site[0].SiteRequestAsync("/api/v2/game/sources?parts=catalog,dans", cancel));
                }
                catch (Exception error)
                {
                    foreach (var e in site) SetStatus(e.Config.DisplayName + ": " + error.Message);
                    throw;
                }
                for (int i = 0; i < site.Count; i++)
                {
                    var e = site[i];
                    try
                    {
                        var source = SourceOf(answer, e);
                        bool dans = ApplyCatalog(e, source["catalog"] as JObject ?? throw new OnlineException("API_SOURCES_INVALID"), cancel);
                        // As when loaded alone: courses that cannot be read keep the ones loaded before.
                        try
                        {
                            if (dans) ApplyDans(e, source["dans"]);
                            else lock (sync) danLists.Remove(e.Id);
                        }
                        catch (OnlineException error) { SetStatus(e.Config.DisplayName + ": 段位 courses unavailable (" + error.Message + ")"); }
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        SetStatus(e.Config.DisplayName + ": " + error.Message);
                        failures[i] = error;
                    }
                }
            }
            finally { foreach (var e in held) e.Transport.Release(); }
            Update();
            return failures;
        }

        // Signs a seat in to every endpoint of a site with one game session, and loads its scores
        // (and 1P's playlists) on all of them in one request. A session the site refuses is
        // traded for once more, like any request with a seat's session.
        public async Task UseAccountAsync(IReadOnlyList<OnlineEndpoint> site, int seat, string token, string userId, string nickname,
            Func<CancellationToken, Task<string>> renew, CancellationToken cancel = default)
        {
            if (site.Count == 0) return;
            var held = new List<OnlineEndpoint>();
            try
            {
                foreach (var e in site) { await e.Transport.WaitAsync(cancel); held.Add(e); }
                lock (sync)
                    foreach (var e in site)
                    {
                        e.UseAccount(seat, token, userId, nickname, renew);
                        if (seat == 0) { personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id); }
                    }
                string path = "/api/v2/game/sources?parts=" + (seat == 0 ? "scores,playlists" : "scores");
                JToken answer;
                try
                {
                    string reply;
                    try { reply = await site[0].SiteRequestAsync(path, cancel, token); }
                    catch (HttpStatusException error) when (error.Status == 401 && renew != null)
                    {
                        string renewed = await renew(cancel);
                        if (!Json.HexId(renewed ?? "", 64)) throw new OnlineException("API_TOKEN_INVALID");
                        lock (sync) foreach (var e in site) e.Seats[seat].Token = renewed;
                        reply = await site[0].SiteRequestAsync(path, cancel, renewed);
                    }
                    answer = Json.Parse(reply);
                    // Read before anything is kept: every endpoint gets its scores, or none does.
                    var read = site.Select(e =>
                    {
                        var source = SourceOf(answer, e);
                        return (Scores: ReadScores(source["scores"]), Playlists: seat == 0 ? source["playlists"]?["playlists"] : null);
                    }).ToArray();
                    for (int i = 0; i < site.Count; i++) SetAccountData(site[i], seat, read[i].Scores, read[i].Playlists);
                }
                catch (Exception error)
                {
                    foreach (var e in site) SetStatus(e.Config.DisplayName + ": " + error.Message);
                    throw;
                }
            }
            finally { foreach (var e in held) e.Transport.Release(); }
            Interlocked.Increment(ref revision);
            RetryNow();
        }

        // Signs a seat in with a game session traded for its OurTaiko account and loads its scores
        // (and 1P's playlists). renew trades again when the session expires.
        public async Task UseAccountAsync(OnlineEndpoint e, int seat, string token, string userId, string nickname,
            Func<CancellationToken, Task<string>> renew, CancellationToken cancel = default)
        {
            await e.Transport.WaitAsync(cancel);
            try
            {
                lock (sync)
                {
                    e.UseAccount(seat, token, userId, nickname, renew);
                    if (seat == 0) { personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id); }
                }
                await LoadAccountDataAsync(e, seat, cancel);
            }
            finally { e.Transport.Release(); }
            Interlocked.Increment(ref revision);
            RetryNow();
        }

        // A seat back to a guest: its scores (and 1P's playlists) are dropped. Pending scores stay queued.
        public void SignOut(OnlineEndpoint e, int seat)
        {
            lock (sync)
            {
                e.Seats[seat].SignOut();
                if (seat == 0) { personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id); }
            }
            Interlocked.Increment(ref revision);
        }

        void SetAccountData(OnlineEndpoint e, int seat, List<OnlineScore> scores, JToken playlists)
        {
            lock (sync)
            {
                var s = e.Seats[seat];
                s.Scores.Clear(); s.Best.Clear();
                foreach (var score in scores) { s.Scores.Add(score); s.IndexScore(score); }
                if (seat == 0)
                {
                    if (playlists == null) personalPlaylists.Remove(e.Id);
                    else personalPlaylists[e.Id] = playlists;
                    ReadPersonalPlaylists(e);
                }
            }
            Interlocked.Increment(ref revision);
        }

        // Called with `sync` held, whenever the account's playlists or the catalog changed. Playlists
        // the client cannot read are left out; the catalog and the login are not affected.
        void ReadPersonalPlaylists(OnlineEndpoint e)
        {
            personalLists.Remove(e.Id);
            if (!personalPlaylists.TryGetValue(e.Id, out var received) || !charts.TryGetValue(e.Id, out var catalog)) return;
            try { personalLists[e.Id] = ReadPlaylists(received, e, new HashSet<string>(catalog.Select(c => c.Id))); }
            catch (OnlineException error) { status = e.Config.DisplayName + ": " + error.Message; }
        }

        // Loads the complete catalog; the account and its scores are untouched. A failure keeps the
        // catalog loaded before.
        public async Task LoadCatalogAsync(OnlineEndpoint e, CancellationToken cancel = default)
        {
            if (!e.HasValidUrl()) throw new OnlineException("SERVER_URL_INVALID");
            await e.Transport.WaitAsync(cancel);
            try { await BootstrapAsync(e, cancel); }
            finally { e.Transport.Release(); }
            Update();
        }

        // The playlists of a catalog or of GET /game/playlists that have a chart of `catalog`, in
        // their order. The field is optional: servers without playlists omit it.
        static List<OnlinePlaylist> ReadPlaylists(JToken value, OnlineEndpoint e, ICollection<string> catalog)
        {
            var playlists = new List<OnlinePlaylist>();
            if (value == null || value.Type == JTokenType.Null) return playlists;
            if (!(value is JArray list)) throw new OnlineException("API_PLAYLIST_INVALID");
            var ids = new HashSet<string>();
            foreach (var item in list)
            {
                var playlist = OnlinePlaylist.From(item, e.Id, e.Config.DisplayName, catalog);
                if (!ids.Add(playlist.Id)) throw new OnlineException("API_PLAYLIST_INVALID");
                if (playlist.ChartIds.Count > 0) playlists.Add(playlist);
            }
            return playlists;
        }

        // One GET /game/catalog (the public song list, the same signed in or not), with the caller
        // holding the endpoint's transport.
        async Task BootstrapAsync(OnlineEndpoint e, CancellationToken cancel)
        {
            try
            {
                SetStatus(e.Config.DisplayName + ": loading catalog");
                var snapshot = Json.Parse(await e.RequestAsync("/api/v2/game/catalog", cancel: cancel));
                if (ApplyCatalog(e, snapshot, cancel)) await LoadDansAsync(e, cancel);
                else lock (sync) danLists.Remove(e.Id);
            }
            catch (Exception error)
            {
                SetStatus(e.Config.DisplayName + ": " + error.Message);
                throw;
            }
        }

        // Takes a catalog (the answer of GET /game/catalog) as the endpoint's song list. True when
        // the server has 段位 courses to load as well.
        bool ApplyCatalog(OnlineEndpoint e, JObject snapshot, CancellationToken cancel)
        {
            {
                if (!SupportsProtocol(snapshot)) throw new OnlineException("SERVER_PROTOCOL_UNSUPPORTED");
                if (!(snapshot["categories"] is JArray categoryList)
                    || !(snapshot["charts"] is JArray chartList) || Json.Number(snapshot, "chartCount") != chartList.Count)
                    throw new OnlineException("API_BOOTSTRAP_INVALID");
                var folders = new List<OnlineCategory>();
                var byCategory = new Dictionary<string, OnlineCategory>();
                var expectedCounts = new Dictionary<string, long>();
                var actualCounts = new Dictionary<string, long>();
                foreach (var v in categoryList)
                {
                    string id = Json.Str(v, "id");
                    if (!ValidCategory(id) || byCategory.ContainsKey(id)) throw new OnlineException("API_CATEGORY_INVALID");
                    var folder = new OnlineCategory { Server = e.Id, ServerName = e.Config.DisplayName,
                        Id = id, Title = Json.Str(v, "title"), Genre = Json.Str(v, "genre"), ScenePreset = Json.Optional(v, "scenePreset") };
                    folders.Add(folder);
                    byCategory.Add(id, folder);
                    expectedCounts.Add(id, Json.Number(v, "chartCount"));
                    actualCounts.Add(id, 0);
                }
                var byId = new Dictionary<string, OnlineChart>();
                foreach (var value in chartList)
                {
                    cancel.ThrowIfCancellationRequested();
                    var chart = OnlineChart.From(value, e.Id);
                    if (byId.ContainsKey(chart.Id)) throw new OnlineException("API_BOOTSTRAP_INVALID");
                    byId.Add(chart.Id, chart);
                    if (!(value["categoryIds"] is JArray memberships))
                        throw new OnlineException("API_CATEGORY_INVALID");
                    var seenCategories = new HashSet<string>();
                    foreach (var membership in memberships)
                    {
                        if (membership.Type != JTokenType.String || !byCategory.TryGetValue((string)membership, out var folder)
                            || !seenCategories.Add((string)membership)) throw new OnlineException("API_CATEGORY_INVALID");
                        actualCounts[folder.Id]++;
                        if (chart.IsPlayable) folder.ChartIds.Add(chart.Id);
                    }
                }
                var list = new List<OnlineChart>();
                var seen = new HashSet<string>();
                foreach (var folder in folders)
                {
                    if (actualCounts[folder.Id] != expectedCounts[folder.Id]) throw new OnlineException("API_CATEGORY_INVALID");
                    foreach (var id in folder.ChartIds)
                    {
                        if (!seen.Add(id)) continue;
                        var chart = byId[id];
                        chart.Category = folder.Title; chart.Genre = folder.Genre;
                        list.Add(chart);
                    }
                }
                var playlists = ReadPlaylists(snapshot["playlists"], e, seen);
                var (issuer, service) = ReadSso(snapshot["sso"]);
                cancel.ThrowIfCancellationRequested();
                lock (sync)
                {
                    e.SsoIssuer = issuer; e.SsoService = service;
                    e.GenreFrame = ReadGenreFrame(snapshot["genreFrame"]);
                    charts[e.Id] = list;
                    categoryLists[e.Id] = folders;
                    playlistLists[e.Id] = playlists;
                    ReadPersonalPlaylists(e);
                    e.ChartCount = list.Count;
                    e.IsConnected = true;
                }
                Interlocked.Increment(ref revision);
                SetStatus(e.Config.DisplayName + ": " + list.Count + " songs ready" + (e.IsAuthenticated ? "" : " (guest; scores disabled)"));
                return snapshot["danVersion"]?.Type == JTokenType.Integer && (long)snapshot["danVersion"] == 1;
            }
        }

        // The server's 段位 courses; a failure keeps the ones loaded before and the catalog.
        async Task LoadDansAsync(OnlineEndpoint e, CancellationToken cancel)
        {
            try
            {
                ApplyDans(e, Json.Parse(await e.RequestAsync("/api/v2/game/dans", cancel: cancel)));
            }
            catch (Exception error) when (!(error is OperationCanceledException) && !cancel.IsCancellationRequested)
            {
                SetStatus(e.Config.DisplayName + ": 段位 courses unavailable (" + error.Message + ")");
            }
        }

        // Takes the answer of GET /game/dans as the endpoint's 段位 courses.
        void ApplyDans(OnlineEndpoint e, JToken reply)
        {
            if (!(reply?["dans"] is JArray dans)) throw new OnlineException("API_DANS_INVALID");
            lock (sync) danLists[e.Id] = dans;
            Interlocked.Increment(ref revision);
        }

        // The endpoint the seat (1P, or a lone 2P's 2P) is signed in to at `server`, or null.
        OnlineEndpoint DanEndpoint(string server, int seat = 0)
        {
            OnlineEndpoint e;
            lock (sync) e = endpoints.Find(x => string.Equals(x.Config.baseUrl, server.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) && x.IsConnected);
            return e != null && HasAccount(e, seat) ? e : null;
        }

        // A song's score for its 段位 attempt on `server` (SubmitDanAttempt's scores): the score as it
        // is uploaded alone, with the hashes of the block and audio that were played, which the
        // server checks as the game does for a score of its own. null when 1P is not signed in there
        // or the chart is another server's: the score then goes up alone.
        public JObject DanAttemptScore(string server, OnlineChart chart, int difficulty, OnlineScore score, PlayRecord replay, string side, int seat = 0)
        {
            var e = DanEndpoint(server, seat);
            if (e == null || Endpoint(chart.Server) != e) return null;
            var block = SideBlock(chart, difficulty, side);
            if (block == null) return null;
            var body = ScoreBody(block, score, replay);
            body["chartHash"] = block.ChartHash; body["audioHash"] = chart.AudioHash;
            return body;
        }

        // Queues a 段位 attempt of a server's course for 1P's account, then sends it in the
        // background like scores; the server asks OurTaikoProfile for its rewards. The queue key,
        // or null when 1P is not signed in there. scores: the played songs' scores in song order
        // (DanAttemptScore; null for a song without one). They go with the attempt to
        // /game/dan-results, where the server saves them first and the attempt with their ids.
        public string SubmitDanAttempt(string server, string courseId, int verdict, int songsPlayed, IReadOnlyList<JObject> scores = null, int seat = 0)
        {
            var e = DanEndpoint(server, seat);
            if (e == null) return null;
            // One entry a song played: a song whose score is not given has none.
            var sent = new JArray();
            for (int i = 0; i < songsPlayed; i++)
                sent.Add(scores != null && i < scores.Count && scores[i] != null ? scores[i] : (JToken)JValue.CreateNull());
            var body = new JObject { ["courseId"] = courseId, ["verdict"] = verdict, ["scores"] = sent };
            string key = RandomKey();
            try
            {
                DanQueue.Enqueue(e.AccountIdOf(seat), key, body.ToString(Newtonsoft.Json.Formatting.None));
                RetryNow();
                return key;
            }
            catch (Exception error) { SetStatus("段位 queue error: " + error.Message); return null; }
        }

        // The answer an uploaded attempt got; null while it waits in the queue.
        public DanAward Award(string key)
        {
            lock (sync) return key != null && danAwards.TryGetValue(key, out var award) ? award : null;
        }

        // Blocks as slots of course × side with their ids (Fanmade 042), signed resource links and replay uploads.
        // The catalog's "genreFrame": a bar_genre frame 0-12; anything else, or none, is 0.
        public static int ReadGenreFrame(JToken value) =>
            value?.Type == JTokenType.Integer && (long)value >= 0 && (long)value <= 12 ? (int)(long)value : 0;

        static bool SupportsProtocol(JObject snapshot) =>
            snapshot["chartSlotVersion"]?.Type == JTokenType.Integer && (long)snapshot["chartSlotVersion"] == 1
            && snapshot["resourceDownloadVersion"]?.Type == JTokenType.Integer && (long)snapshot["resourceDownloadVersion"] == 1
            && snapshot["scoreReplayVersion"]?.Type == JTokenType.Integer && (long)snapshot["scoreReplayVersion"] == 1;

        // The catalog's "sso": where a game signed in to an OurTaiko account trades it for a session
        // of this server. Optional; anything but an https issuer (http only on loopback, for
        // development) and a plain service name is ignored, and nobody can sign in to the server.
        static (string Issuer, string Service) ReadSso(JToken value)
        {
            if (!(value is JObject sso) || sso["issuer"]?.Type != JTokenType.String || sso["service"]?.Type != JTokenType.String) return ("", "");
            string issuer = ((string)sso["issuer"]).TrimEnd('/'), service = (string)sso["service"];
            bool secure = Uri.TryCreate(issuer, UriKind.Absolute, out var uri) && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && uri.UserInfo.Length == 0 && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback));
            bool named = service.Length > 0 && service.Length <= 100 && service.All(c => c > ' ' && c < 127);
            return secure && named ? (issuer, service) : ("", "");
        }

        static bool ValidCategory(string id) => id.Length > 0 && id.Length <= 64 && id[0] >= 'a' && id[0] <= 'z'
            && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');

        // The block of this course on the side played; without one, what the song list shows (1P alone).
        static OnlineDifficulty SideBlock(OnlineChart chart, int difficulty, string side)
            => chart == null || difficulty < 0 || difficulty >= 5 ? null
                : side == null ? chart.Difficulties[difficulty] : chart.BlockOn(difficulty, side);

        // Each seat has its own account (2P signs in through the account center only).
        static bool HasAccount(OnlineEndpoint e, int seat) => seat >= 0 && seat < OnlineEndpoint.SeatCount && e.Seats[seat].IsAuthenticated;

        // The seat's account's best on this chart and course; null for a guest.
        public OnlineScore Best(OnlineChart chart, int difficulty, int seat = 0, string side = null)
        {
            var block = SideBlock(chart, difficulty, side);
            if (block == null) return null;
            lock (sync)
            {
                var e = endpoints.Find(x => x.Id == chart.Server);
                return e != null && HasAccount(e, seat) && e.Seats[seat].Best.TryGetValue(OnlineEndpoint.ScoreKey(chart.Id, block.Name), out var best) ? best : null;
            }
        }

        // Queues a finished play of a logged-in account, then sends it in the background. Guests
        // and unknown charts are not uploaded. The request body and its idempotency key are fixed
        // when queued, so retries never create a second score.
        // side is the block played (decided before the play), seat the account it belongs to.
        // A play of a cached chart while its server is not connected is queued under
        // `offlineAccount` (the queue key the seat's account has there, OnlineEndpoint.AccountIdOf)
        // and sent once that account is signed in to the server again; null queues nothing offline.
        public bool Submit(OnlineChart chart, int difficulty, OnlineScore score, PlayRecord replay = null, int seat = 0, string side = null,
            string offlineAccount = null)
        {
            var block = SideBlock(chart, difficulty, side);
            if (block == null) return false;
            var e = Endpoint(chart.Server);
            string account;
            if (e != null && e.IsConnected)
            {
                if (!HasAccount(e, seat)) return false;
                account = e.AccountIdOf(seat);
            }
            else
            {
                if (string.IsNullOrEmpty(offlineAccount) || Offline.Get(chart.Server, chart.Id) == null) return false;
                account = offlineAccount;
            }
            try
            {
                UploadQueue.Enqueue(account, RandomKey(), ScoreBody(block, score, replay).ToString(Newtonsoft.Json.Formatting.None), block.ChartHash, chart.AudioHash, chart.Id);
                RetryNow();
                return true;
            }
            catch (Exception error) { SetStatus("Score queue error: " + error.Message); return false; }
        }

        // A score as POST /game/scores takes it.
        static JObject ScoreBody(OnlineDifficulty block, OnlineScore score, PlayRecord replay)
        {
            // The block that was played names the song and course.
            var body = new JObject { ["difficultyId"] = block.Id };
            body["good"] = score.Good; body["ok"] = score.Ok; body["bad"] = score.Bad; body["score"] = score.Score;
            body["drumroll"] = score.Drumroll; body["max_combo"] = score.MaxCombo;
            body["ClearStatus"] = score.ClearStatus;
            // Sent only when there are any, so a play without them reads the same as before they existed.
            if (score.AdLibGood > 0 || score.AdLibOk > 0) body["adlib"] = new JArray(score.AdLibGood, score.AdLibOk);
            if (score.Mines > 0) body["mines"] = score.Mines;
            body["replay_data"] = replay != null ? replay.ToJson() : JValue.CreateNull();
            return body;
        }

        // Called every frame: starts a background upload pass at most every 30 s and never waits for HTTP.
        public void Update()
        {
            lock (sync)
            {
                if (uploads != null && !uploads.IsCompleted) return;
                if (uploads != null && uploads.IsFaulted) status = "Score upload error: " + uploads.Exception?.GetBaseException().Message;
                var now = DateTime.UtcNow;
                if (endpoints.Count == 0 || now < retryAt) return;
                retryAt = now + RetryInterval;
                uploads = Task.Run(DrainAsync);
            }
        }

        // Starts the next upload pass at once instead of after the 30 s retry interval.
        public void RetryNow()
        {
            lock (sync) { retryAt = DateTime.MinValue; rerun = true; }
            Update();
        }

        public Task WaitForUploadsAsync() => uploads ?? Task.CompletedTask;

        async Task DrainAsync()
        {
            do
            {
                rerun = false;
                await DrainScoresAsync();
                foreach (var e in Endpoints) await DrainDansAsync(e);
            } while (rerun);
        }

        async Task DrainScoresAsync()
        {
            foreach (var e in Endpoints)
            for (int seat = 0; seat < OnlineEndpoint.SeatCount; seat++)
            {
                if (!e.IsConnected || !e.Seats[seat].IsAuthenticated) continue;
                // Both seats may be the same account: its scores go up once.
                if (seat == 1 && e.Seats[0].IsAuthenticated && e.AccountIdOf(0) == e.AccountIdOf(1)) continue;
                var account = e.Seats[seat];
                foreach (var queued in UploadQueue.Pending(e.AccountIdOf(seat)))
                {
                    try
                    {
                        OnlineScore score;
                        await e.Transport.WaitAsync();
                        try
                        {
                            // A score belongs to the block it was played on and to the audio; if either
                            // changed since, keep it aside.
                            if (string.IsNullOrEmpty(queued.ChartHash) || !Json.HexId(queued.AudioHash ?? "", 64))
                            { UploadQueue.Reject(queued.Key); continue; }
                            var current = OnlineChart.From(Json.Parse(await e.AuthorizedAsync("/api/v2/charts/" + (queued.Song ?? Json.Str(Json.Parse(queued.Body), "songId")) + "?by=id")), e.Id);
                            var manifest = await e.ResourcesAsync(current.Id);
                            if (manifest.Audio.Hash != current.AudioHash)
                                throw new OnlineException("CHART_UPDATING");
                            if (!current.Blocks.Any(b => b.ChartHash == queued.ChartHash) || current.AudioHash != queued.AudioHash)
                            { UploadQueue.Reject(queued.Key); continue; }
                            score = OnlineScore.From(Json.Parse(await e.AuthorizedAsync("/api/v2/game/scores", queued.Body, queued.Key, seat: seat)));
                        }
                        finally { e.Transport.Release(); }
                        lock (sync)
                        {
                            if (!account.Scores.Any(s => s.Id == score.Id)) account.Scores.Add(score);
                            account.IndexScore(score);
                            // The other seat signed in to the same account sees it too.
                            var other = e.Seats[1 - seat];
                            if (other.IsAuthenticated && e.AccountIdOf(1 - seat) == e.AccountIdOf(seat))
                            {
                                if (!other.Scores.Any(s => s.Id == score.Id)) other.Scores.Add(score);
                                other.IndexScore(score);
                            }
                            status = e.Config.DisplayName + ": score uploaded";
                        }
                        Interlocked.Increment(ref revision);
                        UploadQueue.Remove(queued.Key);
                    }
                    catch (HttpStatusException error)
                    {
                        SetStatus(e.Config.DisplayName + ": score pending (" + error.Message + ")");
                        // 409 (the chart's files changed mid-play) and other permanent refusals are kept aside.
                        if (error.Status >= 400 && error.Status < 500 && error.Status != 401 && error.Status != 408 && error.Status != 429)
                        {
                            UploadQueue.Reject(queued.Key);
                            SetStatus(e.Config.DisplayName + ": score rejected (" + error.Message + "), saved locally");
                        }
                        else break;
                    }
                    catch (Exception error) { SetStatus(e.Config.DisplayName + ": score pending (" + error.Message + ")"); break; }
                }
            }
        }

        public int PendingCount(OnlineEndpoint e) => UploadQueue.Pending(e.AccountId).Length;

        // Each signed-in seat's queued 段位 attempts on a connected server, after its scores (2P's
        // come from a lone 2P; a seat signed in to the same account as 1P shares 1P's queue).
        async Task DrainDansAsync(OnlineEndpoint e)
        {
            if (!e.IsConnected) return;
            for (int seat = 0; seat < OnlineEndpoint.SeatCount; seat++)
            {
                if (!HasAccount(e, seat) || seat > 0 && HasAccount(e, 0) && e.AccountIdOf(seat) == e.AccountIdOf(0)) continue;
                if (!await DrainDansAsync(e, seat)) return;
            }
        }

        // False when the server should be tried again later.
        async Task<bool> DrainDansAsync(OnlineEndpoint e, int account)
        {
            foreach (var queued in DanQueue.Pending(e.AccountIdOf(account)))
            {
                try
                {
                    JObject reply;
                    await e.Transport.WaitAsync();
                    // Attempts go to dan-results. One still waiting from a version before it was queued as
                    // a count of songs, which only the deprecated dan-attempts takes.
                    string path = Json.Parse(queued.Body)["scores"] is JArray ? "/api/v2/game/dan-results" : "/api/v2/game/dan-attempts";
                    try { reply = Json.Parse(await e.AuthorizedAsync(path, queued.Body, queued.Key, seat: account)); }
                    finally { e.Transport.Release(); }
                    // The scores the server saved with the attempt: the seat's, as if each had gone up alone.
                    if (reply["scores"] is JArray saved)
                        lock (sync)
                        {
                            foreach (var item in saved)
                            {
                                if (!(item is JObject)) continue;
                                var score = OnlineScore.From(item);
                                foreach (var seat in e.Seats)
                                {
                                    if (!seat.IsAuthenticated || e.AccountIdOf(Array.IndexOf(e.Seats, seat)) != e.AccountIdOf(account)) continue;
                                    if (!seat.Scores.Any(x => x.Id == score.Id)) seat.Scores.Add(score);
                                    seat.IndexScore(score);
                                }
                            }
                        }
                    var award = new DanAward
                    {
                        Profile = reply["profile"] as JObject,
                        Error = reply["profileError"]?.Type == JTokenType.String ? (string)reply["profileError"] : "",
                    };
                    // The server kept the attempt but OurTaikoProfile did not answer. The server asks it
                    // again when the same attempt (key) comes again, so it stays queued for a while.
                    bool again = award.Profile == null && award.Error.Length > 0 && DateTime.UtcNow.Ticks - queued.Created < DanAwardRetryPeriod.Ticks;
                    lock (sync) { danAwards[queued.Key] = award; status = e.Config.DisplayName + (again ? ": 段位 result uploaded, reward pending" : ": 段位 result uploaded"); }
                    Interlocked.Increment(ref revision);
                    if (again) continue;
                    DanQueue.Remove(queued.Key);
                }
                catch (HttpStatusException error)
                {
                    SetStatus(e.Config.DisplayName + ": 段位 result pending (" + error.Message + ")");
                    if (error.Status >= 400 && error.Status < 500 && error.Status != 401 && error.Status != 408 && error.Status != 429)
                    {
                        DanQueue.Reject(queued.Key);
                        lock (sync) danAwards[queued.Key] = new DanAward { Error = error.Message };
                    }
                    else return false;
                }
                catch (Exception error) { SetStatus(e.Config.DisplayName + ": 段位 result pending (" + error.Message + ")"); return false; }
            }
            return true;
        }

        static string RandomKey()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return OnlineEndpoint.Sha256Hex(bytes);
        }

        static Task<T> CacheWork<T>(Func<T> work, CancellationToken cancel)
        {
            return Task.Run(work, cancel);
        }

        // The cached file for a content hash (the manifest only accepts 64-digit hex hashes).
        string ObjectPath(string hash) => ObjectPath(CacheRoot, hash);
        internal static string ObjectPath(string cacheRoot, string hash) => Path.Combine(cacheRoot, "objects", hash.Substring(0, 2), hash);

        // The cached file of a resource, downloaded and verified (size and SHA-256) first when the
        // cache does not hold it. `transfer` and `publish` report the steps to a progress display.
        async Task<string> EnsureObjectAsync(OnlineEndpoint e, OnlineResource resource, CancellationToken cancel,
            FileProgress transfer = null, Action publish = null)
        {
            string file = ObjectPath(resource.Hash);
            void Report(FileProgress.State status)
            {
                if (transfer == null) return;
                transfer.Status = status; publish?.Invoke();
            }
            Report(FileProgress.State.Verifying);
            if (await CacheWork(() => Matches(file, resource), cancel))
            {
                if (transfer != null) transfer.Received = transfer.Total = resource.Size;
                Report(FileProgress.State.Cached);
                return file;
            }
            if (transfer != null) { transfer.Total = resource.Size; transfer.Received = 0; }
            Report(FileProgress.State.Downloading);
            var bytes = await e.ResourceBytesAsync(resource, cancel, transfer == null ? (Action<long, long>)null
                : (received, total) => { transfer.Received = received; transfer.Total = total; publish?.Invoke(); });
            // ResourceBytesAsync has checked the size and SHA-256 (and tried the canonical root when
            // the player's root sent other bytes).
            Report(FileProgress.State.Verifying);
            await CacheWork(() => { cancel.ThrowIfCancellationRequested(); StoreObject(file, bytes, resource); return true; }, cancel);
            Report(FileProgress.State.Complete);
            return file;
        }

        static bool Matches(string path, OnlineResource resource)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != resource.Size) return false;
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == resource.Hash;
        }

        // Publishes verified bytes atomically. Two downloads of the same object can race (a preview
        // requested again before the first finished, or one file on two servers): when the other
        // one already published a matching file, or holds it open, that file is kept.
        static void StoreObject(string path, byte[] bytes, OnlineResource resource)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                catch (IOException) when (Matches(path, resource)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Dispose() => Reset();
    }
}
