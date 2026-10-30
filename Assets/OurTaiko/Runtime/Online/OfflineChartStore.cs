using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SQLite;

namespace OurTaiko.Online
{
    // Every chart PrepareAsync downloaded and verified, so song select can list and play it while
    // its server cannot be reached. One row per server and chart in the scores database, beside the
    // pending uploads: the chart as the API described it, the categories the catalog listed it
    // under and the content hashes of its cached files. The files themselves stay in the shared
    // object cache (objects/<hash> under the cache root); a row whose TJA or audio is gone is
    // dropped by the next Scan.
    public sealed class OfflineChartStore
    {
        [UnityEngine.Scripting.Preserve, Table("CachedCharts")]
        public sealed class Entry
        {
            // Server + "/" + ChartId.
            [PrimaryKey] public string Key { get; set; }
            // The endpoint the chart came through (FanmadeEndpoint.Id), its API root and its name then.
            [Indexed] public string Server { get; set; }
            public string ServerUrl { get; set; }
            public string ServerName { get; set; }
            public string ChartId { get; set; }
            // GET /api/v1/charts/{id} as received; FanmadeChart.From reads it back.
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

            // The chart as the server described it when it was cached; throws FanmadeException on a damaged row.
            public FanmadeChart ToChart()
            {
                var chart = FanmadeChart.From(Json.Parse(ChartJson ?? ""), Server);
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

        public static string KeyOf(string server, string chartId) => server + "/" + chartId;

        readonly string path;
        readonly object sync = new object();

        public OfflineChartStore(string path)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var db = Open();
            db.CreateTable<Entry>();
        }

        SQLiteConnection Open() => new SQLiteConnection(path) { BusyTimeout = TimeSpan.FromSeconds(5) };

        // Records a chart just prepared; a chart cached before takes the newer description and files.
        public void Save(Entry entry)
        {
            entry.Key = KeyOf(entry.Server, entry.ChartId);
            lock (sync) { using var db = Open(); db.InsertOrReplace(entry); }
        }

        public Entry Get(string server, string chartId)
        {
            lock (sync) { using var db = Open(); return db.Find<Entry>(KeyOf(server, chartId)); }
        }

        public Entry[] All()
        {
            lock (sync) { using var db = Open(); return db.Table<Entry>().ToArray(); }
        }

        public void Remove(string server, string chartId)
        {
            lock (sync) { using var db = Open(); db.Delete<Entry>(KeyOf(server, chartId)); }
        }

        // The cached charts whose TJA and audio are still in the object cache with their recorded
        // sizes (the hashes are checked when one is played); rows whose files are gone are removed.
        // Runs on a worker thread beside the songs folder scan.
        public Entry[] Scan(string cacheRoot)
        {
            var kept = new List<Entry>();
            var gone = new List<Entry>();
            foreach (var entry in All())
                (Exists(cacheRoot, entry.TjaHash, entry.TjaSize) && Exists(cacheRoot, entry.AudioHash, entry.AudioSize) ? kept : gone).Add(entry);
            if (gone.Count > 0)
                lock (sync) { using var db = Open(); foreach (var entry in gone) db.Delete<Entry>(entry.Key); }
            return kept.OrderBy(entry => entry.Cached).ToArray();
        }

        static bool Exists(string cacheRoot, string hash, long size)
        {
            if (!Json.HexId(hash ?? "", 64)) return false;
            try
            {
                var file = new FileInfo(FanmadeClient.ObjectPath(cacheRoot, hash));
                return file.Exists && file.Length == size;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { return false; }
        }
    }
}
