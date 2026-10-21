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
    public sealed partial class FanmadeClient : IDisposable
    {
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
        public readonly string CacheRoot;
        public string Status { get { lock (sync) return status; } }
        // Bumped whenever charts or scores change, so views know to refresh.
        public long Revision => Interlocked.Read(ref revision);
        // Catalog snapshots loaded / total of the running ConnectAsync (one per server).
        public (int Done, int Total) CatalogProgress { get { lock (sync) return catalogProgress; } }
        public void ResetCatalogProgress() { lock (sync) catalogProgress = (0, 0); }
        public bool IsUploading => uploads != null && !uploads.IsCompleted;

        readonly object sync = new object();
        readonly List<FanmadeEndpoint> endpoints = new List<FanmadeEndpoint>();
        readonly Dictionary<string, List<FanmadeChart>> charts = new Dictionary<string, List<FanmadeChart>>();
        readonly Dictionary<string, List<FanmadeCategory>> categoryLists = new Dictionary<string, List<FanmadeCategory>>();
        readonly Dictionary<string, List<FanmadePlaylist>> playlistLists = new Dictionary<string, List<FanmadePlaylist>>();
        // The signed-in account's own playlists: as received at login, and read against the catalog
        // once both are there (either may arrive first).
        readonly Dictionary<string, JToken> personalPlaylists = new Dictionary<string, JToken>();
        readonly Dictionary<string, List<FanmadePlaylist>> personalLists = new Dictionary<string, List<FanmadePlaylist>>();
        string status = "";
        (int, int) catalogProgress;
        long revision;
        Task uploads;
        DateTime retryAt = DateTime.MinValue;

        public PendingScoreQueue UploadQueue { get; }
        public FanmadeClient(string cacheRoot, string databasePath = null)
        {
            CacheRoot = Path.GetFullPath(cacheRoot);
            UploadQueue = new PendingScoreQueue(databasePath ?? Path.Combine(CacheRoot, "scores.sqlite3"));
        }

        public IReadOnlyList<FanmadeEndpoint> Endpoints { get { lock (sync) return endpoints.ToArray(); } }

        // Every chart of every connected server, in server then category order, one entry per chart id.
        public IReadOnlyList<FanmadeChart> Charts
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => charts.ContainsKey(e.Id)).SelectMany(e => charts[e.Id]).ToArray();
            }
        }

        // Every category of every connected server, in server then catalog order (empty ones included).
        public IReadOnlyList<FanmadeCategory> Categories
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
        public IReadOnlyList<FanmadePlaylist> Playlists
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => playlistLists.ContainsKey(e.Id)).SelectMany(e =>
                        personalLists.TryGetValue(e.Id, out var own) ? own.Concat(playlistLists[e.Id]) : playlistLists[e.Id]).ToArray();
            }
        }

        public FanmadeChart Chart(string server, string id)
        {
            lock (sync) return charts.TryGetValue(server, out var list) ? list.Find(c => c.Id == id) : null;
        }

        public FanmadeEndpoint Endpoint(string id) { lock (sync) return endpoints.Find(e => e.Id == id); }

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
                endpoints.Clear(); charts.Clear(); categoryLists.Clear(); playlistLists.Clear();
                personalPlaylists.Clear(); personalLists.Clear();
                retryAt = DateTime.MinValue;
            }
            Interlocked.Increment(ref revision);
        }

        // A second connected entry for the same API and account is reused.
        public FanmadeEndpoint Add(ServerConfig config)
        {
            var endpoint = new FanmadeEndpoint(config);
            lock (sync)
            {
                int existing = endpoints.FindIndex(e => e.Id == endpoint.Id);
                if (existing >= 0 && endpoints[existing].IsConnected) { endpoint.Dispose(); return endpoints[existing]; }
                // A retry with another password replaces the endpoint that has not connected.
                if (existing >= 0) { endpoints[existing].Dispose(); endpoints.RemoveAt(existing); }
                endpoints.Add(endpoint);
            }
            return endpoint;
        }

        // Drops the matching servers and their catalogs (uploads finish first); the others stay connected.
        public void Remove(Predicate<FanmadeEndpoint> match)
        {
            try { uploads?.Wait(); } catch (AggregateException) { }
            uploads = null;
            lock (sync)
            {
                foreach (var e in endpoints.FindAll(match))
                {
                    charts.Remove(e.Id); categoryLists.Remove(e.Id); playlistLists.Remove(e.Id);
                    personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id);
                    e.Dispose();
                }
                endpoints.RemoveAll(match);
            }
            Interlocked.Increment(ref revision);
        }

        // Changes the account of a server without touching its catalog. The scores of the previous
        // account are dropped; the endpoint is a guest until LoginAsync succeeds.
        public void SetAccount(FanmadeEndpoint e, string username, string password)
        {
            lock (sync)
            {
                if (!e.SetAccount(username, password)) return;
                e.Scores.Clear(); e.Best.Clear();
                personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id);
            }
            Interlocked.Increment(ref revision);
        }

        // Logs in on its own and loads the account's scores and playlists; the catalog is neither needed nor
        // loaded, and is the same whether an account is signed in or not.
        public async Task LoginAsync(FanmadeEndpoint e, CancellationToken cancel = default)
        {
            if (!e.HasValidUrl()) throw new FanmadeException("SERVER_URL_INVALID");
            await e.Transport.WaitAsync(cancel);
            try { await SignInAsync(e, cancel); }
            finally { e.Transport.Release(); }
            Interlocked.Increment(ref revision);
            RetryNow();
        }

        // Login, GET /game/scores and GET /game/playlists, with the caller holding the endpoint's
        // transport. A guest (no account) has neither.
        async Task SignInAsync(FanmadeEndpoint e, CancellationToken cancel)
        {
            try { await e.LoginAsync(cancel); }
            catch (Exception error)
            {
                SetStatus(e.Config.DisplayName + ": " + error.Message);
                // A refused account is a guest: its earlier scores and playlists are not shown any longer.
                if (!e.IsAuthenticated) SetAccountData(e, 0, new List<FanmadeScore>(), null);
                throw;
            }
            await LoadAccountDataAsync(e, 0, cancel);
        }

        // GET /game/scores (and, for 1P, GET /game/playlists) of a signed-in seat, with the caller
        // holding the endpoint's transport. 2P's own playlists are not shown: the folders are 1P's.
        async Task LoadAccountDataAsync(FanmadeEndpoint e, int seat, CancellationToken cancel)
        {
            var scores = new List<FanmadeScore>();
            JToken playlists = null;
            try
            {
                if (e.Seats[seat].IsAuthenticated)
                {
                    var reply = Json.Parse(await e.AuthorizedAsync("/api/v1/game/scores", cancel: cancel, seat: seat));
                    if (!(reply["scores"] is JArray scoreList)) throw new FanmadeException("API_SCORES_INVALID");
                    foreach (var v in scoreList) scores.Add(FanmadeScore.From(v));
                    if (seat == 0) playlists = Json.Parse(await e.AuthorizedAsync("/api/v1/game/playlists", cancel: cancel))["playlists"];
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

        // Signs a seat in with a game session traded for its OurTaiko account and loads its scores
        // (and 1P's playlists). renew trades again when the session expires.
        public async Task UseAccountAsync(FanmadeEndpoint e, int seat, string token, string userId, string nickname,
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
        public void SignOut(FanmadeEndpoint e, int seat)
        {
            lock (sync)
            {
                e.Seats[seat].SignOut();
                if (seat == 0) { personalPlaylists.Remove(e.Id); personalLists.Remove(e.Id); }
            }
            Interlocked.Increment(ref revision);
        }

        void SetAccountData(FanmadeEndpoint e, int seat, List<FanmadeScore> scores, JToken playlists)
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
        void ReadPersonalPlaylists(FanmadeEndpoint e)
        {
            personalLists.Remove(e.Id);
            if (!personalPlaylists.TryGetValue(e.Id, out var received) || !charts.TryGetValue(e.Id, out var catalog)) return;
            try { personalLists[e.Id] = ReadPlaylists(received, e, new HashSet<string>(catalog.Select(c => c.Id))); }
            catch (FanmadeException error) { status = e.Config.DisplayName + ": " + error.Message; }
        }

        // Loads the complete catalog; the account and its scores are untouched. A failure keeps the
        // catalog loaded before.
        public async Task LoadCatalogAsync(FanmadeEndpoint e, CancellationToken cancel = default)
        {
            if (!e.HasValidUrl()) throw new FanmadeException("SERVER_URL_INVALID");
            await e.Transport.WaitAsync(cancel);
            try { await BootstrapAsync(e, cancel); }
            finally { e.Transport.Release(); }
            Update();
        }

        // The playlists of a catalog or of GET /game/playlists that have a chart of `catalog`, in
        // their order. The field is optional: servers without playlists omit it.
        static List<FanmadePlaylist> ReadPlaylists(JToken value, FanmadeEndpoint e, ICollection<string> catalog)
        {
            var playlists = new List<FanmadePlaylist>();
            if (value == null || value.Type == JTokenType.Null) return playlists;
            if (!(value is JArray list)) throw new FanmadeException("API_PLAYLIST_INVALID");
            var ids = new HashSet<string>();
            foreach (var item in list)
            {
                var playlist = FanmadePlaylist.From(item, e.Id, e.Config.DisplayName, catalog, e.ValidateResourceUrl);
                if (!ids.Add(playlist.Id)) throw new FanmadeException("API_PLAYLIST_INVALID");
                if (playlist.ChartIds.Count > 0) playlists.Add(playlist);
            }
            return playlists;
        }

        // Logs in with the account's scores (unless `guest`), then loads the complete catalog. A rejected login
        // throws HTTP_401/403 without touching the catalog; the caller may then retry as a guest.
        // Only servers declaring the current game protocol are accepted.
        public async Task ConnectAsync(FanmadeEndpoint e, bool guest, CancellationToken cancel = default)
        {
            if (!e.HasValidUrl()) throw new FanmadeException("SERVER_URL_INVALID");
            await e.Transport.WaitAsync(cancel);
            try
            {
                lock (sync) { e.IsConnected = false; charts.Remove(e.Id); categoryLists.Remove(e.Id); playlistLists.Remove(e.Id); personalLists.Remove(e.Id); }
                if (guest) { e.BecomeGuest(); SetAccountData(e, 0, new List<FanmadeScore>(), null); }
                else await SignInAsync(e, cancel);
                await BootstrapAsync(e, cancel);
            }
            finally { e.Transport.Release(); }
            Update();
        }

        // One GET /game/catalog (the public song list, the same signed in or not), with the caller
        // holding the endpoint's transport.
        async Task BootstrapAsync(FanmadeEndpoint e, CancellationToken cancel)
        {
            try
            {
                SetStatus(e.Config.DisplayName + ": loading catalog");
                lock (sync) catalogProgress = (0, 1);
                var snapshot = Json.Parse(await e.RequestAsync("/api/v1/game/catalog", cancel: cancel));
                if (!SupportsProtocol(snapshot)) throw new FanmadeException("SERVER_PROTOCOL_UNSUPPORTED");
                if (!(snapshot["categories"] is JArray categoryList)
                    || !(snapshot["charts"] is JArray chartList) || Json.Number(snapshot, "chartCount") != chartList.Count)
                    throw new FanmadeException("API_BOOTSTRAP_INVALID");
                var folders = new List<FanmadeCategory>();
                var byCategory = new Dictionary<string, FanmadeCategory>();
                var expectedCounts = new Dictionary<string, long>();
                var actualCounts = new Dictionary<string, long>();
                foreach (var v in categoryList)
                {
                    string id = Json.Str(v, "id");
                    if (!ValidCategory(id) || byCategory.ContainsKey(id)) throw new FanmadeException("API_CATEGORY_INVALID");
                    var folder = new FanmadeCategory { Server = e.Id, ServerName = e.Config.DisplayName,
                        Id = id, Title = Json.Str(v, "title"), Genre = Json.Str(v, "genre") };
                    folders.Add(folder);
                    byCategory.Add(id, folder);
                    expectedCounts.Add(id, Json.Number(v, "chartCount"));
                    actualCounts.Add(id, 0);
                }
                var byId = new Dictionary<string, FanmadeChart>();
                foreach (var value in chartList)
                {
                    cancel.ThrowIfCancellationRequested();
                    var chart = FanmadeChart.From(value, e.Id);
                    if (byId.ContainsKey(chart.Id)) throw new FanmadeException("API_BOOTSTRAP_INVALID");
                    byId.Add(chart.Id, chart);
                    if (!(value["categoryIds"] is JArray memberships))
                        throw new FanmadeException("API_CATEGORY_INVALID");
                    var seenCategories = new HashSet<string>();
                    foreach (var membership in memberships)
                    {
                        if (membership.Type != JTokenType.String || !byCategory.TryGetValue((string)membership, out var folder)
                            || !seenCategories.Add((string)membership)) throw new FanmadeException("API_CATEGORY_INVALID");
                        actualCounts[folder.Id]++;
                        if (chart.IsPlayable) folder.ChartIds.Add(chart.Id);
                    }
                }
                var list = new List<FanmadeChart>();
                var seen = new HashSet<string>();
                foreach (var folder in folders)
                {
                    if (actualCounts[folder.Id] != expectedCounts[folder.Id]) throw new FanmadeException("API_CATEGORY_INVALID");
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
                    charts[e.Id] = list;
                    categoryLists[e.Id] = folders;
                    playlistLists[e.Id] = playlists;
                    ReadPersonalPlaylists(e);
                    e.ChartCount = list.Count;
                    e.IsConnected = true;
                    catalogProgress = (1, 1);
                }
                Interlocked.Increment(ref revision);
                SetStatus(e.Config.DisplayName + ": " + list.Count + " songs ready" + (e.IsAuthenticated ? "" : " (guest; scores disabled)"));
            }
            catch (Exception error)
            {
                SetStatus(e.Config.DisplayName + ": " + error.Message);
                throw;
            }
        }

        // Blocks as slots of course × side with their ids (Fanmade 042), signed resource links and replay uploads.
        static bool SupportsProtocol(JObject snapshot) =>
            snapshot["chartSlotVersion"]?.Type == JTokenType.Integer && (long)snapshot["chartSlotVersion"] == 1
            && snapshot["resourceDownloadVersion"]?.Type == JTokenType.Integer && (long)snapshot["resourceDownloadVersion"] == 1
            && snapshot["scoreReplayVersion"]?.Type == JTokenType.Integer && (long)snapshot["scoreReplayVersion"] == 1;

        // The catalog's "sso": where a game signed in to an OurTaiko account trades it for a session
        // of this server. Optional; anything but an https issuer (http only on loopback, for
        // development) and a plain service name is ignored, and the server takes a password instead.
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

        // The block a seat plays on this course (ChartSide): 1P's is Difficulties[course].
        static FanmadeDifficulty SeatBlock(FanmadeChart chart, int difficulty, int seat)
            => chart == null || difficulty < 0 || difficulty >= 5 ? null
                : seat == 0 ? chart.Difficulties[difficulty] : chart.Block(difficulty, ChartSide.Seats[seat]);

        // Each seat has its own account (2P signs in through the account center only).
        static bool HasAccount(FanmadeEndpoint e, int seat) => seat >= 0 && seat < FanmadeEndpoint.SeatCount && e.Seats[seat].IsAuthenticated;

        // The seat's account's best on this chart and course; null for a guest.
        public FanmadeScore Best(FanmadeChart chart, int difficulty, int seat = 0)
        {
            var block = SeatBlock(chart, difficulty, seat);
            if (block == null) return null;
            lock (sync)
            {
                var e = endpoints.Find(x => x.Id == chart.Server);
                return e != null && HasAccount(e, seat) && e.Seats[seat].Best.TryGetValue(FanmadeEndpoint.ScoreKey(chart.Id, block.Name), out var best) ? best : null;
            }
        }

        // Queues a finished play of a logged-in account, then sends it in the background. Guests
        // and unknown charts are not uploaded. The request body and its idempotency key are fixed
        // when queued, so retries never create a second score.
        // seat picks the block played (2P reads p2, then single, then p1) and the account it belongs to.
        public bool Submit(FanmadeChart chart, int difficulty, FanmadeScore score, PlayRecord replay = null, int seat = 0)
        {
            var block = SeatBlock(chart, difficulty, seat);
            if (block == null) return false;
            var e = Endpoint(chart.Server);
            if (e == null || !e.IsConnected || !HasAccount(e, seat)) return false;
            // The block that was played names the song and course.
            var body = new JObject { ["difficultyId"] = block.Id };
            body["good"] = score.Good; body["ok"] = score.Ok; body["bad"] = score.Bad; body["score"] = score.Score;
            body["drumroll"] = score.Drumroll; body["max_combo"] = score.MaxCombo;
            body["ClearStatus"] = score.ClearStatus;
            body["replay_data"] = replay != null ? replay.ToJson() : JValue.CreateNull();
            try
            {
                UploadQueue.Enqueue(e.AccountIdOf(seat), RandomKey(), body.ToString(Newtonsoft.Json.Formatting.None), chart.TjaHash, chart.AudioHash, chart.Id);
                lock (sync) retryAt = DateTime.MinValue;
                Update();
                return true;
            }
            catch (Exception error) { SetStatus("Score queue error: " + error.Message); return false; }
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
            lock (sync) retryAt = DateTime.MinValue;
            Update();
        }

        public Task WaitForUploadsAsync() => uploads ?? Task.CompletedTask;

        async Task DrainAsync()
        {
            foreach (var e in Endpoints)
            for (int seat = 0; seat < FanmadeEndpoint.SeatCount; seat++)
            {
                if (!e.IsConnected || !e.Seats[seat].IsAuthenticated) continue;
                // Both seats may be the same account: its scores go up once.
                if (seat == 1 && e.Seats[0].IsAuthenticated && e.AccountIdOf(0) == e.AccountIdOf(1)) continue;
                var account = e.Seats[seat];
                foreach (var queued in UploadQueue.Pending(e.AccountIdOf(seat)))
                {
                    try
                    {
                        FanmadeScore score;
                        await e.Transport.WaitAsync();
                        try
                        {
                            // A score belongs to the files it was played on; if they changed since, keep it aside.
                            if (!Json.HexId(queued.TjaHash ?? "", 64) || !Json.HexId(queued.AudioHash ?? "", 64))
                            { UploadQueue.Reject(queued.Key); continue; }
                            var current = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + (queued.Song ?? Json.Str(Json.Parse(queued.Body), "songId")))), e.Id);
                            var manifest = await e.ResourcesAsync(current.Id);
                            if (manifest.Tja.Hash != current.TjaHash || manifest.Audio.Hash != current.AudioHash)
                                throw new FanmadeException("CHART_UPDATING");
                            if (current.TjaHash != queued.TjaHash || current.AudioHash != queued.AudioHash)
                            { UploadQueue.Reject(queued.Key); continue; }
                            score = FanmadeScore.From(Json.Parse(await e.AuthorizedAsync("/api/v1/game/scores", queued.Body, queued.Key, seat: seat)));
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

        public int PendingCount(FanmadeEndpoint e) => UploadQueue.Pending(e.AccountId).Length;

        static string RandomKey()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return FanmadeEndpoint.Sha256Hex(bytes);
        }

        static Task<T> CacheWork<T>(Func<T> work, CancellationToken cancel)
        {
            return Task.Run(work, cancel);
        }

        // The cached file for a content hash (the manifest only accepts 64-digit hex hashes).
        string ObjectPath(string hash) => Path.Combine(CacheRoot, "objects", hash.Substring(0, 2), hash);

        // The cached file of a resource, downloaded and verified (size and SHA-256) first when the
        // cache does not hold it. `transfer` and `publish` report the steps to a progress display.
        async Task<string> EnsureObjectAsync(FanmadeEndpoint e, FanmadeResource resource, CancellationToken cancel,
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
            Report(FileProgress.State.Verifying);
            if (bytes.LongLength != resource.Size || await CacheWork(() => FanmadeEndpoint.Sha256Hex(bytes), cancel) != resource.Hash)
                throw new FanmadeException("DOWNLOAD_INTEGRITY_FAILED");
            await CacheWork(() => { cancel.ThrowIfCancellationRequested(); StoreObject(file, bytes, resource); return true; }, cancel);
            Report(FileProgress.State.Complete);
            return file;
        }

        static bool Matches(string path, FanmadeResource resource)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != resource.Size) return false;
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == resource.Hash;
        }

        // Publishes verified bytes atomically. Two downloads of the same object can race (a preview
        // requested again before the first finished, or one file on two servers): when the other
        // one already published a matching file, or holds it open, that file is kept.
        static void StoreObject(string path, byte[] bytes, FanmadeResource resource)
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
