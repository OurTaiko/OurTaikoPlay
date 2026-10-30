using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        // Refreshes the chart's details (the author may have replaced its files), then makes sure
        // the TJA and audio from the resource URLs are cached with matching size and
        // SHA-256. Returns the playable TJA (built from the API metadata, kept in memory only) and
        // the audio object's path. With `movie` the background movie is cached too (Chart.MoviePath);
        // the chart still plays when that fails. `progress` runs on worker threads.
        public async Task<(string Tja, string AudioPath, FanmadeChart Chart)> PrepareAsync(FanmadeChart selected, CancellationToken cancel = default,
            Action<DownloadProgress> progress = null, bool movie = false)
        {
            var e = Endpoint(selected.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
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
                    var c = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + selected.Id + "?by=id", cancel: cancel)), e.Id);
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
                            return PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(original)), c);
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
