using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    public sealed class HttpStatusException : OnlineException
    {
        public readonly int Status;
        public readonly int RetryAfterSeconds;
        public readonly string ErrorCode;
        public HttpStatusException(int status, int retryAfterSeconds = 0, string errorCode = null) : base("HTTP_" + status)
        {
            ErrorCode = errorCode;
            Status = status;
            RetryAfterSeconds = retryAfterSeconds;
        }
    }

    // One server category in catalog order, with its charts in API order. A chart listed in
    // several categories is the same OnlineChart (by id) in each.
    public sealed class OnlineCategory
    {
        public string Server = "", ServerName = "", Id = "", Title = "", Genre = "";
        // The play backdrop the server names for the folder ("scenePreset"); "" when it names none.
        public string ScenePreset = "";
        public readonly List<string> ChartIds = new List<string>();
    }

    // A playlist folder from the catalog or GET /game/playlists: the account's own ("owned") and favorite playlists and the
    // server's "system" ones, with its playable catalog charts in playlist order. Genre selects the
    // folder artwork like a category's; Cover is the optional WebP shown on the open folder
    // (songListCoverPath, songListCoverSha256 and songListCoverSize).
    public sealed class OnlinePlaylist
    {
        // The most a cover may have: what the server lets a cover upload become (8 MiB).
        public const long MaxCoverBytes = 8L * 1024 * 1024;
        public string Server = "", ServerName = "", Id = "", Title = "", Genre = "", Kind = "", Owner = "";
        // The play backdrop its owner chose for it ("scenePreset"); "" when none.
        public string ScenePreset = "";
        public OnlineResource Cover;
        public readonly List<string> ChartIds = new List<string>();

        // One playlist. Charts outside `catalog` (not listed or not playable here) and
        // repeated ones are skipped, so ChartIds may come out empty.
        public static OnlinePlaylist From(JToken v, string server, string serverName, ICollection<string> catalog)
        {
            if (!(v["chartIds"] is JArray members)) throw new OnlineException("API_PLAYLIST_INVALID");
            var playlist = new OnlinePlaylist { Server = server, ServerName = serverName, Id = Json.Str(v, "id"),
                Title = Json.Str(v, "title"), Genre = Json.Str(v, "genre"), Kind = Json.Str(v, "kind"), Owner = Json.Str(v, "owner"), ScenePreset = Json.Optional(v, "scenePreset") };
            if (playlist.Id.Length == 0) throw new OnlineException("API_PLAYLIST_INVALID");
            // A cover is always a WebP; a playlist without one leaves the path out or empty.
            if (v["songListCoverPath"]?.Type == JTokenType.String && ((string)v["songListCoverPath"]).Length > 0)
                playlist.Cover = OnlineResource.From(new JObject
                {
                    ["path"] = v["songListCoverPath"], ["sha256"] = v["songListCoverSha256"], ["size"] = v["songListCoverSize"], ["contentType"] = "image/webp",
                }, MaxCoverBytes);
            foreach (var member in members)
            {
                // Fanmade lists chart IDs as strings, OurTaiko Database as whole numbers.
                string id = member.Type == JTokenType.String ? (string)member
                    : member.Type == JTokenType.Integer && (long)member > 0 ? ((long)member).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : throw new OnlineException("API_PLAYLIST_INVALID");
                if (catalog.Contains(id) && !playlist.ChartIds.Contains(id)) playlist.ChartIds.Add(id);
            }
            return playlist;
        }
    }

    public sealed class OnlineScore
    {
        // Difficulty is the played block's name (OnlineDifficulty.Name).
        public string Id = "", Song = "", Difficulty = "";
        public long Good, Ok, Bad, Score, Drumroll, MaxCombo, ClearStatus;
        // Ad-Libs hit 良 and 可 (sent as "adlib": [良, 可]) and mines hit, apart from Good/Ok/Bad.
        public long AdLibGood, AdLibOk, Mines;
        // False once the song's files were replaced after the play.
        public bool IsLatest = true;
        // The rank the server gave the score against its chart's kiwami line (OurTaikoNet's
        // net.score_rank); null when it has none: the chart's content was replaced since, or a
        // server from before ranks were kept.
        public ScoreRank? ScoreRank;

        public static OnlineScore From(JToken v) => new OnlineScore
        {
            Id = Json.Str(v, "id"), Song = Json.Id(v, "songId"),
            Difficulty = new OnlineDifficulty { Course = Json.Str(v, "course"), Side = Json.Str(v, "side") }.Name,
            IsLatest = v["isLatest"]?.Type != JTokenType.Boolean || (bool)v["isLatest"],
            Good = Json.Number(v, "good"), Ok = Json.Number(v, "ok"), Bad = Json.Number(v, "bad"), Score = Json.Number(v, "score"),
            Drumroll = Json.Number(v, "drumroll"), MaxCombo = Json.Number(v, "max_combo"),
            ClearStatus = Json.Number(v, "ClearStatus"),
            // Servers from before these were kept send neither: 0.
            AdLibGood = v["adlib"] is JArray a && a.Count == 2 ? Count(a[0]) : 0,
            AdLibOk = v["adlib"] is JArray b && b.Count == 2 ? Count(b[1]) : 0,
            Mines = Count(v["mines"]),
            // A whole number 0–7; anything else (one too large for a long included) is none.
            ScoreRank = v["scoreRank"] is JValue { Value: long rank } && rank >= 0 && rank <= ScoreRankUtil.Count
                ? (ScoreRank)(int)rank : null,
        };

        // An optional count: 0 when missing or not a whole number of at least 0.
        static long Count(JToken v) => v?.Type == JTokenType.Integer ? Math.Max(0, (long)v) : 0;
    }

    public sealed class FileProgress
    {
        public enum State { Waiting, Downloading, Verifying, Cached, Complete }
        public State Status;
        public long Received;
        public long Total;  // 0: the server sent no length.
        public FileProgress Clone() => (FileProgress)MemberwiseClone();
    }

    public sealed class DownloadProgress
    {
        public enum Stage { Checking, Files, Preparing, Ready }
        public Stage Step;
        public FileProgress Chart = new FileProgress(), Audio = new FileProgress();
        // Only when the background movie is being fetched.
        public FileProgress Movie;
        public DownloadProgress Clone() => new DownloadProgress { Step = Step, Chart = Chart.Clone(), Audio = Audio.Clone(), Movie = Movie?.Clone() };
    }
}
