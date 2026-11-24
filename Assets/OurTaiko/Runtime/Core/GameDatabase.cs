using System;
using System.IO;
using SQLite;

namespace OurTaiko
{
    // The player's scores.sqlite3: one SQLite file with the local best scores and 段位 attempts and,
    // for OurTaiko.Online, the pending uploads, the charts cached for offline play and the measured
    // audio levels. This owns the file's path, how a connection is opened and the schema; each store
    // keeps only its table's model and queries. Table and column names are the format of shipped
    // versions and never change.
    //
    // Connections stay short-lived, one per operation (S: the uploads and the offline scan use the
    // stores from worker threads); SQLite's file lock and the busy timeout order the writers.
    //
    // The schema's version is PRAGMA user_version. Opening runs the steps from the file's version up
    // to Version in one transaction, so another connection sees the file before or after them, never
    // in between. A file from a newer game (a higher version) is left as it is: sqlite-net ignores
    // columns it does not know.
    public sealed partial class GameDatabase
    {
        public const string FileName = "scores.sqlite3";
        public const int Version = 1;
        public static string DefaultPath => Path.Combine(GamePaths.Root, FileName);

        public string FilePath { get; }

        public GameDatabase(string path)
        {
            FilePath = path;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            Migrate();
        }

        public SQLiteConnection Open() => new SQLiteConnection(FilePath) { BusyTimeout = TimeSpan.FromSeconds(5) };

        // Steps[v] takes a file from version v to v + 1. A shipped step never changes; a schema change
        // appends one, and each layer writes its own tables' part (Online's in GameDatabase.Online.cs).
        static readonly Action<SQLiteConnection>[] Steps = { CreateTables };

        // 0 → 1: what each store's constructor did before the file had a version. CreateTable makes a
        // missing table and adds the columns an older game did not have (their rows read 0 or null),
        // so every file an earlier version wrote comes out the same. It works from today's models: a
        // later step that renames or drops a column must first pin that table's version-1 SQL here.
        static void CreateTables(SQLiteConnection db)
        {
            db.CreateTable<ScoreStore.Record>();
            db.CreateTable<DanRecordStore.Attempt>();
            CreateOnlineTables(db);
        }

        static partial void CreateOnlineTables(SQLiteConnection db);

        public static int VersionOf(SQLiteConnection db) => db.ExecuteScalar<int>("PRAGMA user_version");

        void Migrate()
        {
            using var db = Open();
            if (VersionOf(db) >= Version) return;
            // IMMEDIATE takes the write lock before reading the version, so two games or threads opening
            // an old file one after the other run the steps once. A failed step leaves the transaction
            // open, and closing the connection rolls it back.
            db.Execute("BEGIN IMMEDIATE");
            int from = VersionOf(db);
            for (int version = from; version < Version; version++) Steps[version](db);
            if (from < Version) db.Execute("PRAGMA user_version = " + Version);
            db.Execute("COMMIT");
        }
    }
}
