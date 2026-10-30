using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // A chart prepared once plays again while its server is unreachable (issue #4): the offline
    // store remembers it with its categories and the hashes of its cached files.
    public sealed class OfflineCacheTests
    {
        FanmadeFixture fixture;
        FanmadeClient client;
        string cache;
        FanmadeFixture.Chart first, second;

        [SetUp]
        public void SetUp()
        {
            fixture = new FanmadeFixture();
            first = new FanmadeFixture.Chart { Title = "First", Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = new byte[] { 1, 2, 3, 4 }, Categories = new[] { "game", "pop" } };
            second = new FanmadeFixture.Chart { Title = "Second", Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja("Hard")), Audio = new byte[] { 5, 6 }, Categories = new[] { "pop" } };
            second.Difficulties[0] = ("Hard", 5, "");
            fixture.Charts.Add(first); fixture.Charts.Add(second);
            cache = Path.Combine(Path.GetTempPath(), "ourtaiko-offline-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
        }

        [TearDown]
        public void TearDown()
        {
            client.Dispose();
            fixture.Dispose();
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }

        static void Run(Func<Task> body) => Task.Run(body).GetAwaiter().GetResult();
        static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

        FanmadeEndpoint Connect()
        {
            var endpoint = client.Add(fixture.Server("don", "katsu"));
            Run(() => client.ConnectAsync(endpoint, false));
            return endpoint;
        }

        [Test]
        public void APreparedChartIsRecordedWithEveryCategoryItIsListedUnder()
        {
            var endpoint = Connect();
            Assert.That(client.Offline.Get(endpoint.Id, first.Id), Is.Null, "Nothing is recorded before a chart is prepared.");
            Run(() => client.PrepareAsync(client.Charts.First(c => c.Id == first.Id)));
            var entry = client.Offline.Get(endpoint.Id, first.Id);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.ServerUrl, Is.EqualTo(fixture.BaseUrl.TrimEnd('/')));
            Assert.That(entry.ServerName, Is.EqualTo("Fixture"));
            Assert.That(entry.CategoryList().Select(c => c.Id), Is.EqualTo(new[] { "game", "pop" }));
            Assert.That(entry.CategoryList().Select(c => c.Title), Is.EqualTo(new[] { "Game", "Pop" }));
            Assert.That(entry.TjaHash, Is.EqualTo(FanmadeFixture.Sha(first.Tja)));
            Assert.That(entry.AudioHash, Is.EqualTo(FanmadeFixture.Sha(first.Audio)));
            var chart = entry.ToChart();
            Assert.That(chart.Title, Is.EqualTo("First"));
            Assert.That(chart.Server, Is.EqualTo(endpoint.Id));
            Assert.That(chart.Category, Is.EqualTo("Game"));
            Assert.That(chart.Genre, Is.EqualTo("GAME"));
            Assert.That(chart.AudioName, Is.EqualTo("audio.ogg"));
            Assert.That(client.Offline.Get(endpoint.Id, second.Id), Is.Null, "Only prepared charts are recorded.");
            Assert.That(new OfflineChartStore(Path.Combine(cache, "scores.sqlite3")).Scan(cache).Select(e => e.ChartId), Is.EqualTo(new[] { first.Id }),
                "The record survives a restart.");
        }

        [Test]
        public void ACachedChartPlaysWithoutItsServer()
        {
            var endpoint = Connect();
            Run(() => client.PrepareAsync(client.Charts.First(c => c.Id == first.Id)));
            int downloads = fixture.Downloads, requests = fixture.Requests.Count;
            client.Reset();
            Assert.That(client.Endpoints, Is.Empty);
            var cached = client.Offline.Get(endpoint.Id, first.Id).ToChart();
            var prepared = Run(() => client.PrepareAsync(cached));
            Assert.That(prepared.Tja, Does.Contain("TITLE:First"));
            Assert.That(prepared.Tja, Does.Contain("WAVE:audio.ogg"));
            Assert.That(prepared.Tja, Does.Contain("#START"));
            Assert.That(File.ReadAllBytes(prepared.AudioPath), Is.EqualTo(first.Audio));
            Assert.That(prepared.Chart.Id, Is.EqualTo(first.Id));
            Assert.That(fixture.Downloads, Is.EqualTo(downloads), "Nothing is downloaded offline.");
            Assert.That(fixture.Requests.Count, Is.EqualTo(requests), "The server is not contacted offline.");
            Assert.That(client.Status, Does.Contain("offline cache"));

            var never = new FanmadeChart { Server = endpoint.Id, Id = second.Id };
            var error = Assert.Throws<FanmadeException>(() => Run(() => client.PrepareAsync(never)));
            Assert.That(error.Message, Is.EqualTo("SERVER_NOT_CONNECTED"), "A chart never prepared has no cache to play from.");
        }

        [Test]
        public void ADamagedOrMissingFileRefusesThePlayAndTheScanForgetsTheChart()
        {
            var endpoint = Connect();
            Run(() => client.PrepareAsync(client.Charts.First(c => c.Id == first.Id)));
            client.Reset();
            var entry = client.Offline.Get(endpoint.Id, first.Id);
            string audio = Path.Combine(cache, "objects", entry.AudioHash.Substring(0, 2), entry.AudioHash);
            // Same size, other bytes: the hash check refuses it.
            File.WriteAllBytes(audio, new byte[] { 9, 9, 9, 9 });
            var error = Assert.Throws<FanmadeException>(() => Run(() => client.PrepareAsync(entry.ToChart())));
            Assert.That(error.Message, Is.EqualTo("CACHE_FILE_MISSING"));
            Assert.That(client.Offline.Scan(cache).Select(e => e.ChartId), Is.EqualTo(new[] { first.Id }), "A file of the right size is only checked when played.");
            File.Delete(audio);
            Assert.That(client.Offline.Scan(cache), Is.Empty, "A chart whose audio is gone is not listed.");
            Assert.That(client.Offline.Get(endpoint.Id, first.Id), Is.Null);
        }

        [Test]
        public void PreparingAgainOnlineReplacesTheRecord()
        {
            var endpoint = Connect();
            Run(() => client.PrepareAsync(client.Charts.First(c => c.Id == first.Id)));
            first.Title = "Renamed"; first.Audio = new byte[] { 7, 7, 7 };
            Run(() => client.ConnectAsync(endpoint, false));
            Run(() => client.PrepareAsync(client.Charts.First(c => c.Id == first.Id)));
            var entry = client.Offline.Get(endpoint.Id, first.Id);
            Assert.That(entry.ToChart().Title, Is.EqualTo("Renamed"));
            Assert.That(entry.AudioHash, Is.EqualTo(FanmadeFixture.Sha(first.Audio)));
            Assert.That(client.Offline.All().Length, Is.EqualTo(1));
        }
    }
}
