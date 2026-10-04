using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // FanmadeClient against the local fixture API, after OurTaikoPlayer's tests/fanmade/client.cpp.
    public sealed class FanmadeClientTests
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
            second.Difficulties[0] = ("Hard", 5, 0, true, "");
            fixture.Charts.Add(first); fixture.Charts.Add(second);
            cache = Path.Combine(Path.GetTempPath(), "ourtaiko-fanmade-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
        }

        [TearDown]
        public void TearDown()
        {
            client.Dispose();
            fixture.Dispose();
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }

        // The client's awaits must not resume on the blocked Editor main thread.
        static void Run(Func<Task> body) => Task.Run(body).GetAwaiter().GetResult();
        static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

        FanmadeEndpoint Connect(bool guest, string user = "don", string password = "katsu")
        {
            var endpoint = client.Add(fixture.Server(user, password));
            Run(() => client.ConnectAsync(endpoint, guest));
            return endpoint;
        }

        [Test]
        public void GuestLoadsEveryCategoryAsOneFlatListWithoutLoggingIn()
        {
            var endpoint = Connect(guest: true);
            Assert.That(endpoint.IsConnected, Is.True);
            Assert.That(endpoint.IsAuthenticated, Is.False);
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/login"));
            // First is in both categories but listed once, under the first category.
            Assert.That(client.Charts.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(client.Charts[0].Genre, Is.EqualTo("GAME"));
            Assert.That(client.Charts[1].Genre, Is.EqualTo("J-POP"));
            Assert.That(endpoint.ChartCount, Is.EqualTo(2));
            // Each category keeps its own list for its song-select folder; First is in both.
            Assert.That(client.Categories.Select(c => c.Title), Is.EqualTo(new[] { "Game", "Pop" }));
            Assert.That(client.Categories[0].ChartIds, Is.EqualTo(new[] { first.Id }));
            Assert.That(client.Categories[1].ChartIds, Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(client.Categories[1].ServerName, Is.EqualTo("Fixture"));
            var info = SongInfo.Read(client.Charts[0].CatalogTja());
            Assert.That(info.Title, Is.EqualTo("First"));
            Assert.That(info.Course(Difficulty.Oni).Level, Is.EqualTo(8));
        }

        [Test]
        public void LoginLoadsTheAccountsBestScores()
        {
            fixture.AddAccountScore("don", first, "Oni", 500000);
            fixture.AddAccountScore("don", first, "Oni", 700000);
            fixture.AddAccountScore("other", first, "Oni", 900000);
            var endpoint = Connect(guest: false);
            Assert.That(endpoint.IsAuthenticated, Is.True);
            Assert.That(endpoint.Nickname, Is.EqualTo("DON"));
            Assert.That(client.Best(client.Charts[0], (int)Difficulty.Oni).Score, Is.EqualTo(700000));
            Assert.That(client.Best(client.Charts[0], (int)Difficulty.Hard), Is.Null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ClearStatusSurvivesBootstrapSubmissionAndResponse(int clearStatus)
        {
            fixture.AddAccountScore("don", first, "Oni", 700000, clearStatus);
            Connect(guest: false);
            var chart = client.Charts[0];
            Assert.That(client.Best(chart, (int)Difficulty.Oni).ClearStatus, Is.EqualTo(clearStatus));
            Assert.That(client.Submit(chart, (int)Difficulty.Oni,
                new FanmadeScore { Score = 800000, Good = 100, Bad = 10, ClearStatus = clearStatus }), Is.True);
            Run(() => client.WaitForUploadsAsync());
            Assert.That(fixture.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That(body["ClearStatus"]?.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That((int)body["ClearStatus"], Is.EqualTo(clearStatus));
            Assert.That(client.Best(chart, (int)Difficulty.Oni).ClearStatus, Is.EqualTo(clearStatus));
        }

        [Test]
        public void LegacyScoresWithoutClearStatusDefaultToNoCrown()
        {
            fixture.AddAccountScore("don", first, "Oni", 700000, 3);
            fixture.AccountScores[0].Remove("ClearStatus");
            Connect(guest: false);
            Assert.That(client.Best(client.Charts[0], (int)Difficulty.Oni).ClearStatus, Is.Zero);
        }

        [Test]
        public void RejectedCredentialsThrowAndAGuestRetryStillConnects()
        {
            var endpoint = client.Add(fixture.Server("don", "wrong"));
            var error = Assert.Throws<HttpStatusException>(() => Run(() => client.ConnectAsync(endpoint, guest: false)));
            Assert.That(error.Status, Is.EqualTo(401));
            Assert.That(endpoint.IsConnected, Is.False);
            Assert.That(client.Charts, Is.Empty);
            Run(() => client.ConnectAsync(endpoint, guest: true));
            Assert.That(endpoint.IsConnected && !endpoint.IsAuthenticated, Is.True);
            Assert.That(client.Charts.Count, Is.EqualTo(2));
            // A retry with another password replaces the unconnected endpoint of the same account.
            var retry = client.Add(fixture.Server("don", "katsu"));
            Assert.That(retry, Is.SameAs(endpoint), "A connected endpoint is reused.");
        }

        [Test]
        public void AnUnreachableServerReportsANetworkCode()
        {
            var endpoint = client.Add(new ServerConfig { name = "Down", baseUrl = "http://127.0.0.1:9" });
            var error = Assert.Throws<FanmadeException>(() => Run(() => client.ConnectAsync(endpoint, guest: true)));
            Assert.That(error.Message, Does.StartWith("NETWORK_"));
            var invalid = client.Add(new ServerConfig { name = "Bad", baseUrl = "ftp://example.com" });
            Assert.That(Assert.Throws<FanmadeException>(() => Run(() => client.ConnectAsync(invalid, guest: true))).Message, Is.EqualTo("SERVER_URL_INVALID"));
        }

        [Test]
        public void PrepareDownloadsVerifiesCachesAndRepairsFiles()
        {
            Connect(guest: true);
            var chart = client.Charts[0];
            var (path, prepared) = Run(() => client.PrepareAsync(chart));
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            string dir = Path.GetDirectoryName(path);
            Assert.That(File.ReadAllBytes(Path.Combine(dir, "audio.ogg")), Is.EqualTo(first.Audio));
            string play = File.ReadAllText(path);
            Assert.That(play, Does.Contain("TITLE:First\n"));
            Assert.That(play, Does.Contain("TITLEJA:First JA\n"));
            Assert.That(play, Does.Contain("WAVE:audio.ogg\n"));
            Assert.That(play, Does.Contain("COURSE:Oni\nLEVEL:8\nSTYLE:Single\n"));
            Assert.That(play, Does.Not.Contain("Original Title"));
            Assert.That(TjaParser.Parse(play, "Oni").Title, Is.EqualTo("First"));

            // A cache hit downloads nothing; a damaged file is fetched again.
            Run(() => client.PrepareAsync(chart));
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            File.WriteAllBytes(Path.Combine(dir, "audio.ogg"), new byte[] { 9 });
            Run(() => client.PrepareAsync(chart));
            Assert.That(fixture.Downloads, Is.EqualTo(3));
            Assert.That(File.ReadAllBytes(Path.Combine(dir, "audio.ogg")), Is.EqualTo(first.Audio));
        }

        [Test]
        public void PrepareTakesTheAuthorsNewVersionAndReportsProgress()
        {
            Connect(guest: true);
            var listed = client.Charts[0];
            first.Version = new string('a', 32);
            first.Title = "First v2";
            DownloadProgress last = null;
            int updates = 0;
            var (_, prepared) = Run(() => client.PrepareAsync(listed, progress: p => { last = p; updates++; }));
            Assert.That(prepared.Version, Is.EqualTo(first.Version));
            Assert.That(prepared.Title, Is.EqualTo("First v2"));
            Assert.That(client.Charts[0].Version, Is.EqualTo(first.Version), "The catalog entry follows the new version.");
            Assert.That(last.Step, Is.EqualTo(DownloadProgress.Stage.Ready));
            Assert.That(last.Audio.Status, Is.EqualTo(FileProgress.State.Complete));
            Assert.That(updates, Is.GreaterThan(4));
        }

        [Test]
        public void AnExpiredTokenLogsInAgain()
        {
            Connect(guest: false);
            fixture.Invalidate();
            Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(fixture.Requests.Count(r => r == "POST /api/v1/game/login"), Is.EqualTo(2));
        }

        [Test]
        public void ScoresQueueRetryWithTheSameKeyAndCarryTheReplay()
        {
            var endpoint = Connect(guest: false);
            var chart = client.Charts[0];
            fixture.ScoreFailures.Enqueue(503);
            var record = new PlayRecord { AudioOffsetMs = 12, VisualOffsetMs = -3 };
            record.Inputs.Add((100.5, 1)); record.Inputs.Add((100.5, 3)); record.Inputs.Add((-20, 0));
            Assert.That(client.Submit(chart, (int)Difficulty.Oni, new FanmadeScore { Good = 3, Score = 3000, MaxCombo = 3, ClearStatus = 3 }, record), Is.True);
            Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.EqualTo(1), "A temporary failure keeps the queued request.");
            string queued = client.UploadQueue.Pending(endpoint.Id)[0].Body;

            client.RetryNow();
            Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.EqualTo(0));
            Assert.That(fixture.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That(body.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(queued), "Retries send the original body.");
            Assert.That((string)body["difficulty"], Is.EqualTo("Oni"));
            Assert.That((long)body["max_combo"], Is.EqualTo(3));
            Assert.That((int)body["ClearStatus"], Is.EqualTo(3));
            var replay = (JObject)body["replay_data"];
            Assert.That((int)replay["version"], Is.EqualTo(1));
            Assert.That((int)replay["audio_offset_ms"], Is.EqualTo(12));
            Assert.That(replay["inputs"].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[[100.5,1],[100.5,3],[-20.0,0]]"));
            Assert.That(client.Best(chart, (int)Difficulty.Oni).Score, Is.EqualTo(3000));
        }

        [Test]
        public void APermanentRefusalIsKeptAsRejected()
        {
            var endpoint = Connect(guest: false);
            fixture.ScoreFailures.Enqueue(409);
            client.Submit(client.Charts[0], (int)Difficulty.Oni, new FanmadeScore { Score = 1 });
            Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.Zero);
            Assert.That(client.UploadQueue.RejectedCount(endpoint.Id), Is.EqualTo(1));
        }

        [Test]
        public void GuestsDoubleOnlyCoursesAndOldServersFollowTheOriginalRules()
        {
            Connect(guest: true);
            Assert.That(client.Submit(client.Charts[0], (int)Difficulty.Oni, new FanmadeScore()), Is.False, "Guests never queue scores.");
            Assert.That(Directory.Exists(Path.Combine(cache, "pending")), Is.False);

            client.Reset();
            fixture.ScoreReplayVersion = 0;
            first.Difficulties[0] = ("Oni", 8, 0, false, "P1");
            first.Difficulties.Add(("Oni", 8, 1, false, "P2"));
            Connect(guest: false);
            Assert.That(client.Submit(client.Charts[0], (int)Difficulty.Oni, new FanmadeScore()), Is.False, "DOUBLE-only courses are not uploaded.");
            client.Submit(client.Charts[1], (int)Difficulty.Hard, new FanmadeScore { Score = 5 }, new PlayRecord());
            Run(() => client.WaitForUploadsAsync());
            Assert.That(fixture.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That(body.ContainsKey("replay_data"), Is.False, "Servers without scoreReplayVersion 1 get the original format.");
        }

        [Test]
        public void PlayableTjaKeepsTheApiBlocks()
        {
            var chart = FanmadeChart.From(new FanmadeFixture.Chart
            {
                Title = "Blocks", AudioName = "x.MP3",
                Difficulties = new System.Collections.Generic.List<(string, int, int, bool, string)>
                    { ("Hard", 4, 1, true, ""), ("Oni", 9, 2, false, "P1"), ("Oni", 9, 3, false, "P2") },
            }.ToJson(), "server");
            string original = "TITLE:Old\nBPM:150\nCOURSE:Easy\nLEVEL:1\n#START\n1,\n#END\nCOURSE:Hard\nLEVEL:2\nSCOREINIT:1000\n#START\n2,\n#END\n"
                + "COURSE:Oni\nSTYLE:Double\n#START P1\n3,\n#END\n#START P2\n4,\n#END\n";
            string play = PlayableTja.Build(original, chart);
            Assert.That(play, Does.StartWith("MAKER:Tester\nTITLE:Blocks\nTITLEJA:Blocks JA\nSUBTITLE:\nWAVE:audio.mp3\n"));
            Assert.That(play, Does.Not.Contain("COURSE:Easy"));
            Assert.That(play, Does.Contain("COURSE:Hard\nLEVEL:4\nSTYLE:Single\nBPM:150\nSCOREINIT:1000\n#START\n2,\n#END\n"));
            Assert.That(play, Does.Contain("COURSE:Oni\nLEVEL:9\nSTYLE:Double\nBPM:150\n#START P1\n3,\n#END\n"));
            Assert.That(play, Does.Contain("#START P2\n4,\n#END\n"));
            Assert.Throws<FanmadeException>(() => PlayableTja.Build("COURSE:Hard\n#START\n1,\n#END\n", chart), "TJA_BLOCK_MISMATCH");
        }

        [Test]
        public void ShiftJisChartsAreConverted()
        {
            var bytes = new byte[] { 0x83, 0x65, 0x83, 0x58, 0x83, 0x67 };  // テスト
            Assert.That(PlayableTja.ToUtf8(bytes, "shift-jis"), Is.EqualTo("テスト"));
            Assert.Throws<FanmadeException>(() => PlayableTja.ToUtf8(bytes, "euc-kr"));
        }

        [Test]
        public void ReplayRecordsThatAreTooLongOrInvalidAreSentAsNull()
        {
            var record = new PlayRecord();
            record.Inputs.Add((double.NaN, 1));
            Assert.That(record.ToJson().Type, Is.EqualTo(JTokenType.Null));
            Assert.That(PlayRecord.TypeOf(isKa: true, right: false), Is.EqualTo(0));
            Assert.That(PlayRecord.TypeOf(isKa: false, right: false), Is.EqualTo(1));
            Assert.That(PlayRecord.TypeOf(isKa: false, right: true), Is.EqualTo(2));
            Assert.That(PlayRecord.TypeOf(isKa: true, right: true), Is.EqualTo(3));
        }

        [Test]
        public void ServerListHasBothBuiltInServersAndGenresMapToBoardFrames()
        {
            var list = ServerList.Default();
            Assert.That(list.servers.Select(s => s.baseUrl), Is.EqualTo(new[] { "https://fanmade.ourtaiko.org", "https://ese-backend.llx.life" }));
            Assert.That(list.servers.All(s => s.enabled), Is.True);
            // An existing list keeps its entries (and edits) and only gains the missing built-in address.
            var edited = new ServerList { servers = { new ServerConfig { name = "Mine", baseUrl = "https://fanmade.ourtaiko.org/", enabled = false } } };
            Assert.That(edited.AddBuiltIn(), Is.True);
            Assert.That(edited.servers.Select(s => s.name), Is.EqualTo(new[] { "Mine", "ESE" }));
            Assert.That(edited.servers[0].enabled, Is.False);
            Assert.That(edited.AddBuiltIn(), Is.False);
            Assert.That(OnlineManager.GenreFrame("GAME"), Is.EqualTo(3));
            Assert.That(OnlineManager.GenreFrame("ボーカロイド"), Is.EqualTo(8));
            Assert.That(OnlineManager.GenreFrame("Unknown"), Is.EqualTo(0));
        }
    }
}
