using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // One recorded play on a server's public replay list (GET /api/v1/replays): the play's block
    // and score, the song, and who played it.
    public sealed class FanmadeReplay
    {
        // The endpoint's Id, as FanmadeChart.Server.
        public string Server = "", ServerName = "";
        public string ScoreId = "", UserId = "", SongId = "", Course = "Oni", Side = "single";
        public string Title = "", Subtitle = "", Nickname = "";
        public long DifficultyId, Score;
        public int Level, Good, Ok, Bad, MaxCombo, ClearStatus;
        public bool IsLatest;
        public DateTime SubmittedAt;

        // null for a row this version cannot play (an unknown course or side, an unusable id): the
        // list skips it.
        public static FanmadeReplay From(JToken v, FanmadeEndpoint endpoint)
        {
            var r = new FanmadeReplay
            {
                Server = endpoint.Id, ServerName = endpoint.Config.DisplayName,
                ScoreId = Json.Str(v, "id"), UserId = Json.Str(v, "userId"), SongId = Json.Str(v, "songId"),
                Course = Json.Str(v, "course"), Side = Json.Str(v, "side"),
                Title = Json.Str(v, "title"), Subtitle = Json.Str(v, "subtitle"), Nickname = Json.Str(v, "nickname"),
                DifficultyId = Json.Number(v, "difficultyId"), Score = Json.Number(v, "score"),
                Level = (int)Json.Number(v, "level"), Good = (int)Json.Number(v, "good"), Ok = (int)Json.Number(v, "ok"),
                Bad = (int)Json.Number(v, "bad"), MaxCombo = (int)Json.Number(v, "max_combo"), ClearStatus = (int)Json.Number(v, "ClearStatus"),
                IsLatest = v?["isLatest"]?.Type == JTokenType.Boolean && (bool)v["isLatest"],
            };
            if (v?["submittedAt"] is JValue at && at.Value is DateTime time) r.SubmittedAt = time.ToUniversalTime();
            else if (DateTime.TryParse(Json.Str(v, "submittedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) r.SubmittedAt = parsed;
            if (!SafeId(r.ScoreId) || !Json.ChartId(r.SongId) || SongInfo.DifficultyOf(r.Course) == null
                || (r.Side != "single" && r.Side != "p1" && r.Side != "p2")) return null;
            return r;
        }

        // A score id goes into a request path: letters, digits, '-' and '_' only.
        static bool SafeId(string s) => s.Length > 0 && s.Length <= 64 && s.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_'));
    }

    public sealed class FanmadeReplayPage
    {
        public FanmadeEndpoint Endpoint;
        public readonly List<FanmadeReplay> Items = new List<FanmadeReplay>();
        public int Page, Total, PageSize;
        public string Error;
        public bool HasMore => Error == null && Page * PageSize < Total;
    }

    public sealed partial class FanmadeClient
    {
        // A page of every connected server's replay list, each on its own: one failure never discards
        // the other servers' rows. sort is recent or score; categoryId (a catalog category's id)
        // narrows to that category's songs.
        public Task<FanmadeReplayPage[]> ReplaysAsync(int page, string sort = "recent", CancellationToken cancel = default, string server = null, string categoryId = null)
            => Task.WhenAll(Endpoints.Where(e => e.IsConnected && (server == null || e.Id == server)).Select(e => ReplayPageAsync(e, page, sort, categoryId, cancel)));

        async Task<FanmadeReplayPage> ReplayPageAsync(FanmadeEndpoint endpoint, int page, string sort, string categoryId, CancellationToken cancel)
        {
            var result = new FanmadeReplayPage { Endpoint = endpoint, Page = page };
            await endpoint.Transport.WaitAsync(cancel);
            try
            {
                string path = "/api/v1/replays?page=" + page + "&sort=" + Uri.EscapeDataString(sort);
                if (!string.IsNullOrEmpty(categoryId)) path += "&categoryId=" + Uri.EscapeDataString(categoryId);
                var reply = Json.Parse(await endpoint.RequestAsync(path, cancel: cancel));
                if (!(reply["items"] is JArray items)) throw new FanmadeException("API_REPLAY_INVALID");
                result.Total = (int)Json.Number(reply, "total");
                result.PageSize = Math.Max(1, (int)Json.Number(reply, "pageSize"));
                int skipped = 0;
                foreach (var value in items)
                {
                    var replay = FanmadeReplay.From(value, endpoint);
                    if (replay != null) result.Items.Add(replay); else skipped++;
                }
                if (skipped > 0) Log.Warn("Online", endpoint.Config.DisplayName + ": skipped " + skipped + " replay rows this version cannot read.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (HttpStatusException error) when (error.Status == 404) { result.Error = "no replay list"; result.Items.Clear(); }
            catch (Exception error) { result.Error = error.Message; result.Items.Clear(); }
            finally { endpoint.Transport.Release(); }
            return result;
        }

        // The listed play's recording (GET /api/v1/scores/{id}/replay). Refused when it was played on
        // files the song has replaced since, or on another song.
        public async Task<PlayRecord> ReplayRecordAsync(FanmadeReplay replay, CancellationToken cancel = default)
        {
            var endpoint = ConnectedEndpoint(replay.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            JObject reply;
            await endpoint.Transport.WaitAsync(cancel);
            try { reply = Json.Parse(await endpoint.RequestAsync("/api/v1/scores/" + replay.ScoreId + "/replay", cancel: cancel)); }
            catch (HttpStatusException error) when (error.Status == 404) { throw new FanmadeException("REPLAY_NOT_FOUND"); }
            finally { endpoint.Transport.Release(); }
            var score = reply["score"];
            if (Json.Str(score, "id") != replay.ScoreId || Json.Str(score, "songId") != replay.SongId
                || Json.Str(score, "course") != replay.Course || Json.Str(score, "side") != replay.Side) throw new FanmadeException("API_REPLAY_INVALID");
            if (score?["isLatest"]?.Type != JTokenType.Boolean || !(bool)score["isLatest"]) throw new FanmadeException("REPLAY_OUTDATED");
            return PlayRecord.FromJson(reply["replay"]) ?? throw new FanmadeException("REPLAY_UNREADABLE");
        }

        // The listed play's song: from the server's song list, else described by the server (a
        // published song outside every category). null when the song has no playable block left.
        public async Task<FanmadeChart> ReplayChartAsync(FanmadeReplay replay, CancellationToken cancel = default)
        {
            var known = Chart(replay.Server, replay.SongId);
            if (known != null) return known;
            var endpoint = ConnectedEndpoint(replay.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            await endpoint.Transport.WaitAsync(cancel);
            try
            {
                var chart = FanmadeChart.From(Json.Parse(await endpoint.RequestAsync("/api/v1/charts/" + replay.SongId + "?by=id", cancel: cancel)), endpoint.Id);
                return chart.IsPlayable ? chart : null;
            }
            catch (HttpStatusException error) when (error.Status == 404) { return null; }
            finally { endpoint.Transport.Release(); }
        }
    }
}
