using System;
using System.Linq;

namespace OurTaiko.Online
{
    // An outbox, never a source of historical best scores. Endpoint includes server AND account.
    // Its rows are PendingScoreUpload (the file's format, beside GameDatabase).
    public sealed class PendingScoreQueue
    {
        readonly GameDatabase database;
        readonly object sync = new();
        public PendingScoreQueue(string path) : this(new GameDatabase(path)) { }
        public PendingScoreQueue(GameDatabase database) => this.database = database;
        public void Enqueue(string endpoint, string key, string body, string chartHash = null, string audioHash = null, string song = null)
        {
            lock (sync) { using var db = database.Open(); db.Insert(new PendingScoreUpload { Endpoint = endpoint, Key = key, Body = body, ChartHash = chartHash, AudioHash = audioHash, Song = song, Created = DateTime.UtcNow.Ticks }); }
        }
        public PendingScoreUpload[] Pending(string endpoint)
        {
            lock (sync) { using var db = database.Open(); return db.Table<PendingScoreUpload>().Where(x => x.Endpoint == endpoint && !x.Rejected).OrderBy(x => x.Created).ToArray(); }
        }
        public int RejectedCount(string endpoint)
        {
            lock (sync) { using var db = database.Open(); return db.Table<PendingScoreUpload>().Count(x => x.Endpoint == endpoint && x.Rejected); }
        }
        public void Remove(string key) { lock (sync) { using var db = database.Open(); db.Delete<PendingScoreUpload>(key); } }
        public void Reject(string key)
        {
            lock (sync) { using var db = database.Open(); db.Execute("UPDATE PendingScoreUploads SET Rejected = 1 WHERE Key = ?", key); }
        }
    }

    // 段位 attempts waiting for their server (POST /api/v2/game/dan-results), in the same file as
    // the scores. Endpoint is the account's queue key (OnlineEndpoint.AccountIdOf). Its rows are
    // PendingDanAttempt.
    public sealed class PendingDanQueue
    {
        readonly GameDatabase database;
        readonly object sync = new();
        public PendingDanQueue(string path) : this(new GameDatabase(path)) { }
        public PendingDanQueue(GameDatabase database) => this.database = database;
        public void Enqueue(string endpoint, string key, string body)
        {
            lock (sync) { using var db = database.Open(); db.Insert(new PendingDanAttempt { Endpoint = endpoint, Key = key, Body = body, Created = DateTime.UtcNow.Ticks }); }
        }
        public PendingDanAttempt[] Pending(string endpoint)
        {
            lock (sync) { using var db = database.Open(); return db.Table<PendingDanAttempt>().Where(x => x.Endpoint == endpoint && !x.Rejected).OrderBy(x => x.Created).ToArray(); }
        }
        public void Remove(string key) { lock (sync) { using var db = database.Open(); db.Delete<PendingDanAttempt>(key); } }
        public void Reject(string key)
        {
            lock (sync) { using var db = database.Open(); db.Execute("UPDATE PendingDanAttempts SET Rejected = 1 WHERE Key = ?", key); }
        }
    }
}
