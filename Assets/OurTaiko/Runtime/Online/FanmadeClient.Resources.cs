using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        // Refreshes the chart's details (the author may have replaced its files), then makes sure
        // the TJA and audio from the resource URLs are cached with matching size and
        // SHA-256. Returns the playable TJA (built from the API metadata, kept in memory only) and
        // the audio object's path. With `movie` the background movie is cached too (Chart.MoviePath);
        // the chart still plays when that fails. `progress` runs on worker threads.
        // A chart whose server is not connected plays from the offline cache when it was prepared
        // before (PrepareOfflineAsync); otherwise SERVER_NOT_CONNECTED.
        public async Task<(string Tja, string AudioPath, FanmadeChart Chart)> PrepareAsync(FanmadeChart selected, CancellationToken cancel = default,
            Action<DownloadProgress> progress = null, bool movie = false)
        {
            var e = Endpoint(selected.Server);
            if (e == null)
            {
                var cached = Offline.Get(selected.Server, selected.Id);
                if (cached == null) throw new FanmadeException("SERVER_NOT_CONNECTED");
                return await PrepareOfflineAsync(selected, cached, cancel, progress, movie);
            }
            var state = new DownloadProgress();
            void Publish() { cancel.ThrowIfCancellationRequested(); progress?.Invoke(state.Clone()); }
            Publish();
            await e.Transport.WaitAsync(cancel);
            try
            {
                // One shared restart budget: mismatch, 403/404 and transient failure.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancel.ThrowIfCancellationRequested();
                    string described = await e.AuthorizedAsync("/api/v1/charts/" + selected.Id + "?by=id", cancel: cancel);
                    var c = FanmadeChart.From(Json.Parse(described), e.Id);
                    if (c.Id != selected.Id) throw new FanmadeException("RESOURCE_CHART_MISMATCH");
                    c.Category = selected.Category; c.Genre = selected.Genre;
                    var manifest = await e.ResourcesAsync(c.Id, cancel);
                    // The server's current chart is prepared (the catalog entry follows it); the
                    // manifest must name that chart's audio, and the TJA is checked on download.
                    if (c.AudioHash != manifest.Audio.Hash)
                    { if (attempt == 0) continue; throw new FanmadeException("CHART_UPDATING"); }
                    if (!c.IsPlayable) throw new FanmadeException("CHART_NO_PLAYABLE_COURSE");
                    c.AudioName = manifest.Audio.ContentType == "audio/ogg" ? "audio.ogg" : "audio.mp3";
                    try
                    {
                        state.Step = DownloadProgress.Stage.Files; Publish();
                        string original = await EnsureObjectAsync(e, manifest.Tja, cancel, state.Chart, Publish);
                        string audio = await EnsureObjectAsync(e, manifest.Audio, cancel, state.Audio, Publish);
                        if (movie && manifest.Movie != null)
                        {
                            state.Movie = new FileProgress();
                            try { c.MoviePath = await EnsureObjectAsync(e, manifest.Movie, cancel, state.Movie, Publish); }
                            catch (Exception error) when (!cancel.IsCancellationRequested)
                            { UnityEngine.Debug.LogWarning("Background movie download failed: " + error.Message); }
                        }
                        state.Step = DownloadProgress.Stage.Preparing; Publish();
                        // Rebuilt on every preparation: it depends on the API metadata, not only on the files.
                        string text = await CacheWork(() =>
                        {
                            cancel.ThrowIfCancellationRequested();
                            string built = PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(original)), c);
                            // Both files are verified and the TJA reads: the chart plays offline from now on.
                            Remember(e, c, described, manifest);
                            return built;
                        }, cancel);
                        lock (sync)
                        {
                            if (charts.TryGetValue(c.Server, out var list))
                            { int index = list.FindIndex(x => x.Id == c.Id); if (index >= 0) list[index] = c; }
                        }
                        Interlocked.Increment(ref revision);
                        state.Step = DownloadProgress.Stage.Ready; Publish(); SetStatus(e.Config.DisplayName + ": ready");
                        return (text, audio, c);
                    }
                    catch (HttpStatusException error) when (attempt == 0 && (error.Status == 403 || error.Status == 404 || error.Status >= 500)) { }
                    catch (FanmadeException error) when (attempt == 0 && error.Message == "NETWORK_TIMEOUT") { }
                }
                throw new FanmadeException("RESOURCE_UNAVAILABLE");
            }
            finally { e.Transport.Release(); }
        }

        // Records a prepared chart in the offline store with the categories the catalog lists it
        // under (none for a chart found by search only). A failure here never stops the play.
        void Remember(FanmadeEndpoint e, FanmadeChart c, string described, FanmadeResources manifest)
        {
            try
            {
                var categories = new JArray();
                lock (sync)
                {
                    if (categoryLists.TryGetValue(e.Id, out var list))
                        foreach (var category in list.Where(x => x.ChartIds.Contains(c.Id)))
                            categories.Add(new JObject { ["id"] = category.Id, ["title"] = category.Title, ["genre"] = category.Genre });
                }
                Offline.Save(new OfflineChartStore.Entry
                {
                    Server = e.Id, ServerUrl = e.Config.baseUrl, ServerName = e.Config.DisplayName, ChartId = c.Id,
                    ChartJson = described, Categories = categories.ToString(Newtonsoft.Json.Formatting.None),
                    TjaHash = manifest.Tja.Hash, TjaSize = manifest.Tja.Size,
                    AudioHash = manifest.Audio.Hash, AudioSize = manifest.Audio.Size, AudioType = manifest.Audio.ContentType,
                    MovieHash = manifest.Movie?.Hash, MovieSize = manifest.Movie?.Size ?? 0,
                    PreviewHash = manifest.Preview?.Hash, PreviewSize = manifest.Preview?.Size ?? 0,
                    Cached = DateTime.UtcNow.Ticks,
                });
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("Could not record the cached chart: " + error.Message); }
        }

        // The offline counterpart of PrepareAsync: the chart as the server last described it, its
        // TJA and audio from the object cache, each checked against its recorded size and SHA-256 as
        // a download would be. The server is never contacted; a damaged or missing file refuses the play.
        async Task<(string Tja, string AudioPath, FanmadeChart Chart)> PrepareOfflineAsync(FanmadeChart selected, OfflineChartStore.Entry cached,
            CancellationToken cancel, Action<DownloadProgress> progress, bool movie)
        {
            var state = new DownloadProgress();
            void Publish() { cancel.ThrowIfCancellationRequested(); progress?.Invoke(state.Clone()); }
            Publish();
            var c = cached.ToChart();
            if (c.Id != selected.Id) throw new FanmadeException("RESOURCE_CHART_MISMATCH");
            c.Category = selected.Category; c.Genre = selected.Genre;
            if (!c.IsPlayable) throw new FanmadeException("CHART_NO_PLAYABLE_COURSE");
            state.Step = DownloadProgress.Stage.Files; Publish();
            string original = await CachedObjectAsync(cached.TjaHash, cached.TjaSize, cancel, state.Chart, Publish);
            string audio = await CachedObjectAsync(cached.AudioHash, cached.AudioSize, cancel, state.Audio, Publish);
            if (movie && cached.MovieHash != null)
            {
                state.Movie = new FileProgress();
                try { c.MoviePath = await CachedObjectAsync(cached.MovieHash, cached.MovieSize, cancel, state.Movie, Publish); }
                catch (Exception error) when (!cancel.IsCancellationRequested)
                { UnityEngine.Debug.LogWarning("Cached background movie unavailable: " + error.Message); }
            }
            state.Step = DownloadProgress.Stage.Preparing; Publish();
            string text = await CacheWork(() =>
            {
                cancel.ThrowIfCancellationRequested();
                return PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(original)), c);
            }, cancel);
            state.Step = DownloadProgress.Stage.Ready; Publish(); SetStatus(cached.ServerName + ": ready (offline cache)");
            return (text, audio, c);
        }

        // A cached object that still matches the size and hash recorded for it; CACHE_FILE_MISSING otherwise.
        async Task<string> CachedObjectAsync(string hash, long size, CancellationToken cancel, FileProgress transfer, Action publish)
        {
            if (!Json.HexId(hash ?? "", 64)) throw new FanmadeException("CACHE_FILE_MISSING");
            string file = ObjectPath(hash);
            transfer.Status = FileProgress.State.Verifying; publish();
            var resource = new FanmadeResource { Hash = hash, Size = size };
            if (!await CacheWork(() => Matches(file, resource), cancel)) throw new FanmadeException("CACHE_FILE_MISSING");
            transfer.Received = transfer.Total = size;
            transfer.Status = FileProgress.State.Cached; publish();
            return file;
        }

        // A playlist's folder cover from the object cache, downloading it first when it is not
        // cached with the announced size and SHA-256.
        public async Task<byte[]> PlaylistCoverAsync(FanmadePlaylist playlist, CancellationToken cancel = default)
        {
            var e = Endpoint(playlist.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            var resource = playlist.Cover ?? throw new FanmadeException("RESOURCE_MISSING");
            string file = await EnsureObjectAsync(e, resource, cancel);
            return await CacheWork(() => File.ReadAllBytes(file), cancel);
        }
    }
}
