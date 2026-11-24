using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OurTaiko.Online
{
    // Every chart PrepareAsync downloaded and verified, so song select can list and play it while
    // its server cannot be reached. One row per server and chart in the scores database, beside the
    // pending uploads: the chart as the API described it, the categories the catalog listed it
    // under and the content hashes of its cached files. The files themselves stay in the shared
    // object cache (objects/<hash> under the cache root); a row whose TJA or audio is gone is
    // dropped by the next Scan. Its rows are CachedChart.
    public sealed class OfflineChartStore
    {
        public static string KeyOf(string server, string chartId) => server + "/" + chartId;

        readonly GameDatabase database;
        readonly object sync = new object();

        public OfflineChartStore(string path) : this(new GameDatabase(path)) { }
        public OfflineChartStore(GameDatabase database) => this.database = database;

        // Records a chart just prepared; a chart cached before takes the newer description and files.
        public void Save(CachedChart entry)
        {
            entry.Key = KeyOf(entry.Server, entry.ChartId);
            lock (sync) { using var db = database.Open(); db.InsertOrReplace(entry); }
        }

        public CachedChart Get(string server, string chartId)
        {
            lock (sync) { using var db = database.Open(); return db.Find<CachedChart>(KeyOf(server, chartId)); }
        }

        public CachedChart[] All()
        {
            lock (sync) { using var db = database.Open(); return db.Table<CachedChart>().ToArray(); }
        }

        public void Remove(string server, string chartId)
        {
            lock (sync) { using var db = database.Open(); db.Delete<CachedChart>(KeyOf(server, chartId)); }
        }

        // The cached charts whose TJA and audio are still in the object cache with their recorded
        // sizes (the hashes are checked when one is played); rows whose files are gone are removed.
        // Runs on a worker thread beside the songs folder scan.
        public CachedChart[] Scan(string cacheRoot)
        {
            var kept = new List<CachedChart>();
            var gone = new List<CachedChart>();
            foreach (var entry in All())
                (Exists(cacheRoot, entry.TjaHash, entry.TjaSize) && Exists(cacheRoot, entry.AudioHash, entry.AudioSize) ? kept : gone).Add(entry);
            if (gone.Count > 0)
                lock (sync) { using var db = database.Open(); foreach (var entry in gone) db.Delete<CachedChart>(entry.Key); }
            return kept.OrderBy(entry => entry.Cached).ToArray();
        }

        static bool Exists(string cacheRoot, string hash, long size)
        {
            if (!Json.HexId(hash ?? "", 64)) return false;
            try
            {
                var file = new FileInfo(OnlineClient.ObjectPath(cacheRoot, hash));
                return file.Exists && file.Length == size;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { return false; }
        }
    }
}
