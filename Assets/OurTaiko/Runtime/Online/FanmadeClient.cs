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
    // server. One bootstrap loads every category and chart when a server connects;
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

        // Every category of every connected server, in server then bootstrap order (empty ones included).
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

        // A second connected entry for the same API and account is reused, as in bootstrap().
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
        // transport. A guest (no account) has neither. A server without one of the two lists
        // answers 404 or 405: the login stands, without history or without own playlists.
        async Task SignInAsync(FanmadeEndpoint e, CancellationToken cancel)
        {
            var scores = new List<FanmadeScore>();
            JToken playlists = null;
            try
            {
                await e.LoginAsync(cancel);
                if (e.IsAuthenticated)
                {
                    try
                    {
                        var reply = Json.Parse(await e.AuthorizedAsync("/api/v1/game/scores", cancel: cancel));
                        if (!(reply["scores"] is JArray scoreList)) throw new FanmadeException("API_SCORES_INVALID");
                        foreach (var v in scoreList) scores.Add(FanmadeScore.From(v));
                    }
                    catch (HttpStatusException error) when (error.Status == 404 || error.Status == 405) { }
                    try { playlists = Json.Parse(await e.AuthorizedAsync("/api/v1/game/playlists", cancel: cancel))["playlists"]; }
                    catch (HttpStatusException error) when (error.Status == 404 || error.Status == 405) { }
                }
            }
            catch (Exception error)
            {
                SetStatus(e.Config.DisplayName + ": " + error.Message);
                // A refused account is a guest: its earlier scores and playlists are not shown any longer.
                if (!e.IsAuthenticated) SetAccountData(e, scores, null);
                throw;
            }
            SetAccountData(e, scores, playlists);
        }

        void SetAccountData(FanmadeEndpoint e, List<FanmadeScore> scores, JToken playlists)
        {
            lock (sync)
            {
                e.Scores.Clear(); e.Best.Clear();
                foreach (var score in scores) { e.Scores.Add(score); e.IndexScore(score); }
                if (playlists == null) personalPlaylists.Remove(e.Id);
                else personalPlaylists[e.Id] = playlists;
                ReadPersonalPlaylists(e);
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

        // The playlists of a bootstrap that have a chart of `catalog`, in bootstrap order. The field
        // is optional: servers without playlists omit it.
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
                if (guest) { e.BecomeGuest(); SetAccountData(e, new List<FanmadeScore>(), null); }
                else await SignInAsync(e, cancel);
                await BootstrapAsync(e, cancel);
            }
            finally { e.Transport.Release(); }
            Update();
        }

        // GET /game/catalog: the public song list, the same signed in or not. A server from before
        // it answers 404 or 405 and still has the deprecated bootstrap, whose catalog part is the same.
        static async Task<string> CatalogAsync(FanmadeEndpoint e, CancellationToken cancel)
        {
            try { return await e.RequestAsync("/api/v1/game/catalog", cancel: cancel); }
            catch (HttpStatusException error) when (error.Status == 404 || error.Status == 405)
            {
                return await e.AuthorizedAsync("/api/v1/game/bootstrap", cancel: cancel);
            }
        }

        // One catalog request, with the caller holding the endpoint's transport.
        async Task BootstrapAsync(FanmadeEndpoint e, CancellationToken cancel)
        {
            try
            {
                SetStatus(e.Config.DisplayName + ": loading catalog");
                lock (sync) catalogProgress = (0, 1);
                var snapshot = Json.Parse(await CatalogAsync(e, cancel));
                if (!SupportsProtocol(snapshot)) throw new FanmadeException("SERVER_PROTOCOL_UNSUPPORTED");
                e.Slots = SlotProtocol(snapshot);
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
                cancel.ThrowIfCancellationRequested();
                lock (sync)
                {
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

        // Blocks as slots of course × side with their ids.
        static bool SlotProtocol(JObject snapshot) =>
            snapshot["chartSlotVersion"]?.Type == JTokenType.Integer && (long)snapshot["chartSlotVersion"] == 1;

        // Difficulties as slots, or keyed by course on an older server; signed resource links and replay uploads.
        static bool SupportsProtocol(JObject snapshot) =>
            (SlotProtocol(snapshot) || (snapshot["courseKeyedDifficulties"]?.Type == JTokenType.Boolean && (bool)snapshot["courseKeyedDifficulties"]))
            && snapshot["resourceDownloadVersion"]?.Type == JTokenType.Integer && (long)snapshot["resourceDownloadVersion"] == 1
            && snapshot["scoreReplayVersion"]?.Type == JTokenType.Integer && (long)snapshot["scoreReplayVersion"] == 1;

        static bool ValidCategory(string id) => id.Length > 0 && id.Length <= 64 && id[0] >= 'a' && id[0] <= 'z'
            && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');

        // The account's best on this chart and course; null for a guest.
        public FanmadeScore Best(FanmadeChart chart, int difficulty)
        {
            if (chart == null || difficulty < 0 || difficulty >= 5 || chart.Difficulties[difficulty] == null) return null;
            lock (sync)
            {
                var e = endpoints.Find(x => x.Id == chart.Server);
                return e != null && e.Best.TryGetValue(FanmadeEndpoint.ScoreKey(chart.Id, chart.Difficulties[difficulty].Name), out var best) ? best : null;
            }
        }

        // Queues a finished play of a logged-in account, then sends it in the background. Guests
        // and unknown charts are not uploaded. The request body and its idempotency key are fixed
        // when queued, so retries never create a second score.
        public bool Submit(FanmadeChart chart, int difficulty, FanmadeScore score, PlayRecord replay = null)
        {
            if (chart == null || difficulty < 0 || difficulty >= 5 || chart.Difficulties[difficulty] == null) return false;
            var e = Endpoint(chart.Server);
            if (e == null || !e.IsConnected || !e.IsAuthenticated) return false;
            // The block that was played; an older server takes the song and the block's name.
            var block = chart.Difficulties[difficulty];
            var body = block.Id > 0 ? new JObject { ["difficultyId"] = block.Id } : new JObject { ["songId"] = chart.Id, ["difficulty"] = block.Name };
            body["good"] = score.Good; body["ok"] = score.Ok; body["bad"] = score.Bad; body["score"] = score.Score;
            body["drumroll"] = score.Drumroll; body["max_combo"] = score.MaxCombo;
            body["ClearStatus"] = score.ClearStatus;
            body["replay_data"] = replay != null ? replay.ToJson() : JValue.CreateNull();
            try
            {
                UploadQueue.Enqueue(e.AccountId, RandomKey(), body.ToString(Newtonsoft.Json.Formatting.None), chart.TjaHash, chart.AudioHash, chart.Id);
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
            {
                if (!e.IsConnected || !e.IsAuthenticated) continue;
                foreach (var queued in UploadQueue.Pending(e.AccountId))
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
                            score = FanmadeScore.From(Json.Parse(await e.AuthorizedAsync("/api/v1/game/scores", queued.Body, queued.Key)));
                        }
                        finally { e.Transport.Release(); }
                        lock (sync)
                        {
                            if (!e.Scores.Any(s => s.Id == score.Id)) e.Scores.Add(score);
                            e.IndexScore(score);
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
