using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using SQLite;

namespace OurTaiko.Tests
{
    public sealed class GameDatabaseTests
    {
        // scores.sqlite3 as the game wrote it before the file had a version: each store's constructor
        // ran sqlite-net's CreateTable for its own table (sqlite_master as that code made it).
        static readonly string[] Unversioned =
        {
            "CREATE TABLE \"AudioLevels\" (\n\"Hash\" varchar primary key not null ,\n\"Meter\" integer ,\n\"Peak\" float ,\n\"Loudness\" float ,\n\"Measured\" integer )",
            "CREATE TABLE \"BestScores\" (\n\"key\" varchar primary key not null ,\n\"score\" integer ,\n\"good\" integer ,\n\"ok\" integer ,\n\"bad\" integer ,\n\"maxCombo\" integer ,\n\"rolls\" integer ,\n\"crown\" integer ,\n\"adLibGood\" integer ,\n\"adLibOk\" integer ,\n\"mines\" integer )",
            "CREATE TABLE \"CachedCharts\" (\n\"Key\" varchar primary key not null ,\n\"Server\" varchar ,\n\"ServerUrl\" varchar ,\n\"ServerName\" varchar ,\n\"ServerGenre\" integer ,\n\"ChartId\" varchar ,\n\"ChartJson\" varchar ,\n\"Categories\" varchar ,\n\"TjaHash\" varchar ,\n\"TjaSize\" integer ,\n\"AudioHash\" varchar ,\n\"AudioSize\" integer ,\n\"AudioType\" varchar ,\n\"MovieHash\" varchar ,\n\"MovieSize\" integer ,\n\"PreviewHash\" varchar ,\n\"PreviewSize\" integer ,\n\"Cached\" integer )",
            "CREATE INDEX \"CachedCharts_Server\" on \"CachedCharts\"(\"Server\")",
            "CREATE TABLE \"DanAttempts\" (\n\"id\" integer primary key autoincrement not null ,\n\"courseKey\" varchar ,\n\"title\" varchar ,\n\"verdict\" integer ,\n\"score\" integer ,\n\"maxCombo\" integer ,\n\"gaugePoints\" float ,\n\"arrival\" integer ,\n\"playedAt\" integer ,\n\"songStats\" varchar ,\n\"gaugePercent\" integer )",
            "CREATE INDEX \"DanAttempts_courseKey\" on \"DanAttempts\"(\"courseKey\")",
            "CREATE TABLE \"PendingDanAttempts\" (\n\"Key\" varchar primary key not null ,\n\"Endpoint\" varchar ,\n\"Body\" varchar ,\n\"Rejected\" integer ,\n\"Created\" integer )",
            "CREATE INDEX \"PendingDanAttempts_Endpoint\" on \"PendingDanAttempts\"(\"Endpoint\")",
            "CREATE TABLE \"PendingScoreUploads\" (\n\"Key\" varchar primary key not null ,\n\"Endpoint\" varchar ,\n\"Body\" varchar ,\n\"Rejected\" integer ,\n\"ChartHash\" varchar ,\n\"AudioHash\" varchar ,\n\"Song\" varchar ,\n\"Created\" integer )",
            "CREATE INDEX \"PendingScoreUploads_Endpoint\" on \"PendingScoreUploads\"(\"Endpoint\")",
        };

        static readonly string AudioHash = new string('a', 64);

        string root, path;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "ourtaiko-database-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); path = Path.Combine(root, "scores.sqlite3"); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        [Test] public void UnversionedFileKeepsItsRowsAndEndsAtTheCurrentVersion()
        {
            using (var db = new SQLiteConnection(path))
            {
                foreach (string sql in Unversioned) db.Execute(sql);
                db.Execute("INSERT INTO BestScores (key, score, good, ok, bad, maxCombo, rolls, crown, adLibGood, adLibOk, mines) VALUES ('Local/Oni', 123456, 400, 20, 3, 410, 15, 2, 0, 0, 0)");
                db.Execute("INSERT INTO DanAttempts (courseKey, title, verdict, score, maxCombo, gaugePoints, arrival, playedAt, songStats, gaugePercent) VALUES ('course', '十段', 1, 900000, 800, 9500.5, 3, 1700000000, NULL, 0)");
                db.Execute("INSERT INTO PendingScoreUploads (Key, Endpoint, Body, Rejected, ChartHash, AudioHash, Song, Created) VALUES ('upload', 'server/account', '{\"score\":456789}', 0, NULL, NULL, NULL, 1)");
                db.Execute("INSERT INTO PendingDanAttempts (Key, Endpoint, Body, Rejected, Created) VALUES ('dan', 'server/account', '{\"verdict\":1}', 0, 2)");
                db.Execute("INSERT INTO CachedCharts (Key, Server, ChartId, ChartJson, TjaHash, TjaSize, AudioHash, AudioSize, Cached) VALUES ('server/chart', 'server', 'chart', '{}', 'tja', 10, 'audio', 20, 3)");
                db.Execute("INSERT INTO AudioLevels (Hash, Meter, Peak, Loudness, Measured) VALUES (?, ?, 0.5, -14.25, 4)", AudioHash, AudioLevel.MeterVersion);
                Assert.That(GameDatabase.VersionOf(db), Is.EqualTo(0));
            }
            string before = Contents(path);

            var database = new GameDatabase(path);
            using (var db = database.Open()) Assert.That(GameDatabase.VersionOf(db), Is.EqualTo(GameDatabase.Version));
            Assert.That(Contents(path), Is.EqualTo(before), "The schema and every row stay as they were.");
            Assert.That(new ScoreStore(database).Get("Local", Difficulty.Oni).score, Is.EqualTo(123456));
            Assert.That(new DanRecordStore(database).Get("course")?.Score, Is.EqualTo(900000));
            Assert.That(new PendingScoreQueue(database).Pending("server/account").Single().Body, Is.EqualTo("{\"score\":456789}"));
            Assert.That(new PendingDanQueue(database).Pending("server/account").Single().Body, Is.EqualTo("{\"verdict\":1}"));
            Assert.That(new OfflineChartStore(database).Get("server", "chart").TjaSize, Is.EqualTo(10));
            Assert.That(new AudioLevelStore(database).TryGet(AudioHash, out var level), Is.True);
            Assert.That(level.Loudness, Is.EqualTo(-14.25));

            byte[] migrated = File.ReadAllBytes(path);
            new GameDatabase(path);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(migrated), "Opening a current file again writes nothing.");
        }

        [Test] public void NewFileHasTheUnversionedTablesAtTheCurrentVersion()
        {
            var database = new GameDatabase(Path.Combine(root, "new", "scores.sqlite3"));
            using var db = database.Open();
            Assert.That(GameDatabase.VersionOf(db), Is.EqualTo(GameDatabase.Version));
            Assert.That(db.QueryScalars<string>("SELECT sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY name"),
                Is.EqualTo(Unversioned), "Table and column names are the format of shipped versions.");
        }

        // Every table's and index's SQL, then every table's rows.
        static string Contents(string path)
        {
            using var db = new SQLiteConnection(path);
            var lines = new List<string>(db.QueryScalars<string>("SELECT type || ' ' || name || ' ' || coalesce(sql, '') FROM sqlite_master ORDER BY name"));
            foreach (string table in db.QueryScalars<string>("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name"))
            {
                string columns = string.Join(" || ',' || ", db.GetTableInfo(table).Select(c => "quote(\"" + c.Name + "\")"));
                lines.AddRange(db.QueryScalars<string>("SELECT '" + table + ": ' || " + columns + " FROM \"" + table + "\" ORDER BY rowid"));
            }
            return string.Join("\n", lines);
        }
    }
}
