using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    public sealed class ScoreDatabaseTests
    {
        string root, path;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "ourtaiko-scores-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); path = Path.Combine(root, "scores.sqlite3"); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        [Test] public void LocalBestAndOutboxSurviveRestartInSeparateTables()
        {
            var store = new ScoreStore(path);
            store.Save(new PlayResult { ChartKey = "Local", Difficulty = Difficulty.Oni, Score = 123456 });
            store.Save(new PlayResult { ChartKey = "fanmade/server/song", Difficulty = Difficulty.Oni, Score = 999999 });
            var queue = new PendingScoreQueue(path);
            queue.Enqueue("server/account-a", "request-key", "{\"score\":456789}");
            var reopened = new ScoreStore(path);
            Assert.That(reopened.Get("Local", Difficulty.Oni).score, Is.EqualTo(123456));
            Assert.That(reopened.Get("fanmade/server/song", Difficulty.Oni), Is.Null);
            queue = new PendingScoreQueue(path);
            Assert.That(queue.Pending("server/account-a")[0].Body, Is.EqualTo("{\"score\":456789}"));
            Assert.That(queue.Pending("server/account-b"), Is.Empty);
            Assert.That(queue.Pending("other-server/account-a"), Is.Empty);
            Assert.That(Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 15), Is.EqualTo("SQLite format 3"));
            queue.Remove("request-key");
            Assert.That(new PendingScoreQueue(path).Pending("server/account-a"), Is.Empty);
            Assert.That(new ScoreStore(path).Get("Local", Difficulty.Oni).score, Is.EqualTo(123456));
        }

        [Test] public void LegacyScoresMigrateOnceWithoutOnlineRecords()
        {
            string legacy = Path.Combine(root, "scores.json");
            File.WriteAllText(legacy, "{\"records\":[{\"key\":\"Local/Oni\",\"score\":123,\"crown\":2},{\"key\":\"fanmade/server/song/Oni\",\"score\":999}]}");
            var store = new ScoreStore(path, legacy);
            Assert.That(store.Get("Local", Difficulty.Oni).score, Is.EqualTo(123));
            Assert.That(store.Get("fanmade/server/song", Difficulty.Oni), Is.Null);
            store.Save(new PlayResult { ChartKey = "Local", Difficulty = Difficulty.Oni, Score = 456 });
            Assert.That(new ScoreStore(path, legacy).Get("Local", Difficulty.Oni).score, Is.EqualTo(456));
            Assert.That(File.Exists(legacy), Is.True, "Original JSON remains available for recovery.");
        }
    }
}
