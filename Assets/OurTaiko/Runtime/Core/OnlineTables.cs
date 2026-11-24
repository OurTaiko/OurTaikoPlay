using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SQLite;

namespace OurTaiko.Online
{
    // OurTaiko.Online's tables in scores.sqlite3. Their rows are part of the file's format, which
    // GameDatabase owns, so they are declared here beside it; the queries stay with their stores in
    // OurTaiko.Online (PendingScoreQueue, PendingDanQueue, OfflineChartStore, AudioLevelStore).

    // A score waiting for its server (PendingScoreQueue).
    [UnityEngine.Scripting.Preserve, Table("PendingScoreUploads")]
    public sealed class PendingScoreUpload
    {
        [PrimaryKey] public string Key { get; set; }
        [Indexed] public string Endpoint { get; set; }
        [Column("Body")] public string Body { get; set; }
        [Column("Rejected")] public bool Rejected { get; set; }
        // The played block's content hash (rows queued before 2026-10-23 have the TJA file hash
        // in the old TjaHash column instead and are rejected).
        public string ChartHash { get; set; }
        public string AudioHash { get; set; }
        // The played song; null in rows queued before bodies named only the block.
        public string Song { get; set; }
        [Column("Created")] public long Created { get; set; }
    }

    // A 段位 attempt waiting for its server (PendingDanQueue).
    [UnityEngine.Scripting.Preserve, Table("PendingDanAttempts")]
    public sealed class PendingDanAttempt
    {
        [PrimaryKey] public string Key { get; set; }
        [Indexed] public string Endpoint { get; set; }
        public string Body { get; set; }
        public bool Rejected { get; set; }
        public long Created { get; set; }
    }

    // A chart cached for offline play (OfflineChartStore).
    [UnityEngine.Scripting.Preserve, Table("CachedCharts")]
    public sealed class CachedChart
    {
        // Server + "/" + ChartId.
        [PrimaryKey] public string Key { get; set; }
        // The endpoint the chart came through (OnlineEndpoint.Id), its API root and its name then.
        [Indexed] public string Server { get; set; }
        public string ServerUrl { get; set; }
        public string ServerName { get; set; }
        // The server's own folder board then (OnlineEndpoint.GenreFrame); 0 in entries cached
        // before servers named one.
        public int ServerGenre { get; set; }
        public string ChartId { get; set; }
        // GET /api/v2/charts/{id} as received; OnlineChart.From reads it back.
        public string ChartJson { get; set; }
        // [{"id","title","genre"}] in catalog order: the categories the chart was listed under.
        public string Categories { get; set; }
        public string TjaHash { get; set; }
        public long TjaSize { get; set; }
        public string AudioHash { get; set; }
        public long AudioSize { get; set; }
        // The manifest's audio content type (audio/ogg or audio/mpeg): the playable copy's WAVE name.
        public string AudioType { get; set; }
        // The background movie's and the preview excerpt's objects when the manifest named them;
        // null otherwise. Either plays offline only if it was cached while online.
        public string MovieHash { get; set; }
        public long MovieSize { get; set; }
        public string PreviewHash { get; set; }
        public long PreviewSize { get; set; }
        public long Cached { get; set; }

        // The chart as the server described it when it was cached; throws OnlineException on a damaged row.
        public OnlineChart ToChart()
        {
            var chart = OnlineChart.From(Json.Parse(ChartJson ?? ""), Server);
            chart.AudioName = AudioType == "audio/ogg" ? "audio.ogg" : "audio.mp3";
            var categories = CategoryList();
            if (categories.Count > 0) { chart.Category = categories[0].Title; chart.Genre = categories[0].Genre; }
            return chart;
        }

        // The categories the chart was listed under; empty for one found by search only, or a damaged row.
        public IReadOnlyList<(string Id, string Title, string Genre)> CategoryList()
        {
            var list = new List<(string, string, string)>();
            JArray values;
            try { values = string.IsNullOrEmpty(Categories) ? null : JArray.Parse(Categories); }
            catch (Newtonsoft.Json.JsonException) { return list; }
            if (values == null) return list;
            foreach (var value in values)
            {
                if (!(value is JObject category) || category["id"]?.Type != JTokenType.String) continue;
                string title = category["title"]?.Type == JTokenType.String ? (string)category["title"] : "";
                string genre = category["genre"]?.Type == JTokenType.String ? (string)category["genre"] : "";
                list.Add(((string)category["id"], title, genre));
            }
            return list;
        }
    }

    // The level a server song's full audio measured (AudioLevelStore).
    [UnityEngine.Scripting.Preserve, Table("AudioLevels")]
    public sealed class AudioLevelRecord
    {
        [PrimaryKey] public string Hash { get; set; }
        public int Meter { get; set; }
        public float Peak { get; set; }
        // Integrated LUFS; null when unmeasurable (silent or under 400 ms).
        public double? Loudness { get; set; }
        public long Measured { get; set; }
    }
}
