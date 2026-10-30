using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // OurTaiko accounts: the encrypted account file, signing both seats in through the account
    // center (the fixture plays it) and per-seat scores and uploads.
    public sealed class OurTaikoAccountTests
    {
        FanmadeFixture fixture;
        FanmadeClient client;
        AccountCenter center;
        string cache;
        FanmadeFixture.Chart chart;

        [SetUp]
        public void SetUp()
        {
            fixture = new FanmadeFixture();
            fixture.Users["ka"] = "don";
            chart = new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = new byte[] { 1, 2, 3 } };
            chart.Difficulties.Add(("Oni", 8, "P2"));
            fixture.Charts.Add(chart);
            cache = Path.Combine(Path.GetTempPath(), "ourtaiko-accounts-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
            center = new AccountCenter(fixture.BaseUrl);
        }

        [TearDown]
        public void TearDown()
        {
            center.Dispose();
            client.Dispose();
            fixture.Dispose();
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }

        static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();
        static void Run(Func<Task> body) => Task.Run(body).GetAwaiter().GetResult();

        // A built-in style server: the catalog first, then the seat signed in with a traded session.
        FanmadeEndpoint Catalog()
        {
            var e = client.Add(new ServerConfig { name = "Built-in", baseUrl = fixture.BaseUrl });
            Run(() => client.LoadCatalogAsync(e));
            return e;
        }

        OurTaikoAccount SignIn(FanmadeEndpoint e, int seat, string user, string password)
        {
            var account = Run(() => center.LoginAsync(user, password, "Test"));
            string session = Run(() => center.TradeAsync(account.Token, e.SsoService));
            Run(() => client.UseAccountAsync(e, seat, session, account.UserId, account.Nickname, c => center.TradeAsync(account.Token, e.SsoService, c)));
            return account;
        }

        // A score queued while a pass is running goes up with the next one.
        void Drain()
        {
            Run(() => client.WaitForUploadsAsync());
            client.RetryNow();
            Run(() => client.WaitForUploadsAsync());
        }

        [Test]
        public void TheAccountFileKeepsNoPasswordAndOnlyThisDeviceCanReadIt()
        {
            Directory.CreateDirectory(cache);
            string path = Path.Combine(cache, AccountStore.FileName);
            var store = new AccountStore(path, "device-a");
            var account = Run(() => center.LoginAsync("don", "katsu", "Test"));
            store.Seats[1] = account;
            store.Save();
            string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.That(raw, Does.Not.Contain(account.Token).And.Not.Contain("katsu").And.Not.Contain("don"));

            var again = new AccountStore(path, "device-a");
            again.Load();
            Assert.That(again.Seats[0].IsSignedIn, Is.False);
            Assert.That(again.Seats[1].Token, Is.EqualTo(account.Token));
            Assert.That(again.Seats[1].UserId, Is.EqualTo(FanmadeFixture.UserId("don")));
            Assert.That(again.Seats[1].Nickname, Is.EqualTo("DON"));

            var elsewhere = new AccountStore(path, "device-b");
            elsewhere.Load();
            Assert.That(elsewhere.Seats.All(s => !s.IsSignedIn), Is.True, "A copy on another device reads as no account.");

            var bytes = File.ReadAllBytes(path);
            bytes[20] ^= 1;
            File.WriteAllBytes(path, bytes);
            again.Load();
            Assert.That(again.Seats.All(s => !s.IsSignedIn), Is.True, "A damaged file is no account.");
        }

        [Test]
        public void TheCatalogNamesTheAccountCenterOnlyWhenItIsUsable()
        {
            var e = Catalog();
            Assert.That(e.SsoIssuer, Is.EqualTo(fixture.BaseUrl));
            Assert.That(e.SsoService, Is.EqualTo(FanmadeFixture.Service));
            foreach (var issuer in new[] { "http://example.org", "ftp://127.0.0.1", "https://user@example.org", "nonsense" })
            {
                fixture.ChangeBootstrap = reply => reply["sso"]["issuer"] = issuer;
                Run(() => client.LoadCatalogAsync(e));
                Assert.That(e.SsoService, Is.Empty, issuer);
            }
            fixture.ChangeBootstrap = null;
            fixture.AccountCenter = false;
            Run(() => client.LoadCatalogAsync(e));
            Assert.That(e.SsoIssuer, Is.Empty);
        }

        [Test]
        public void EachSeatHasItsOwnScoresAndUploadsWithItsOwnSession()
        {
            fixture.AddAccountScore("don", chart, "Oni", 900000);
            fixture.AddAccountScore("ka", chart, "Oni", 800000, player: "P2");
            var e = Catalog();
            SignIn(e, 0, "don", "katsu");
            SignIn(e, 1, "ka", "don");
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/login"), "No password goes to the game server.");
            var listed = client.Chart(e.Id, chart.Id);
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 0, "single")?.Score, Is.EqualTo(900000));
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 1, "p2")?.Score, Is.EqualTo(800000), "2P reads its own account's p2 block.");

            Assert.That(client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 1000000, Good = 4, MaxCombo = 4 }, seat: 1, side: "p2"), Is.True);
            Assert.That(client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 950000, Good = 4, MaxCombo = 4 }, seat: 0, side: "single"), Is.True);
            Drain();
            Assert.That(fixture.AcceptedUsers.ToArray(), Is.EquivalentTo(new[] { "don", "ka" }));
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 1, "p2")?.Score, Is.EqualTo(1000000));
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 0, "single")?.Score, Is.EqualTo(950000));

            client.SignOut(e, 1);
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 1, "p2"), Is.Null);
            Assert.That(client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 1 }, seat: 1, side: "p2"), Is.False, "A guest seat uploads nothing.");
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 0, "single")?.Score, Is.EqualTo(950000), "1P is untouched.");
            Assert.That(client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 1 }, seat: 0, side: "p1"), Is.False, "No block on the side played: nothing uploads.");
        }

        [Test]
        public void AnExpiredSessionIsTradedAgainAndTheScoreStillGoesUp()
        {
            var e = Catalog();
            SignIn(e, 0, "don", "katsu");
            fixture.ExpireSessions();
            var listed = client.Chart(e.Id, chart.Id);
            Assert.That(client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 1000, Good = 1, MaxCombo = 1 }), Is.True);
            Run(() => client.WaitForUploadsAsync());
            Assert.That(fixture.AcceptedUsers.ToArray(), Is.EqualTo(new[] { "don" }));
            Assert.That(fixture.Requests.Count(r => r == "POST /api/v1/game/tokens"), Is.EqualTo(2));
            Assert.That(client.PendingCount(e), Is.Zero);
        }

        [Test]
        public void BothSeatsOnOneAccountUploadItsScoresOnce()
        {
            var e = Catalog();
            SignIn(e, 0, "don", "katsu");
            SignIn(e, 1, "don", "katsu");
            Assert.That(e.AccountIdOf(0), Is.EqualTo(e.AccountIdOf(1)));
            var listed = client.Chart(e.Id, chart.Id);
            client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 10, Good = 1, MaxCombo = 1 }, seat: 0, side: "single");
            client.Submit(listed, (int)Difficulty.Oni, new FanmadeScore { Score = 20, Good = 1, MaxCombo = 1 }, seat: 1, side: "p2");
            Drain();
            Assert.That(fixture.AcceptedScores.Select(s => (long)s["score"]), Is.EquivalentTo(new[] { 10L, 20L }));
            // Each seat played its own block (2P the P2 one); both seats see both plays.
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 0, "single")?.Score, Is.EqualTo(10));
            Assert.That(client.Best(listed, (int)Difficulty.Oni, 1, "p2")?.Score, Is.EqualTo(20));
            Assert.That(e.Seats[0].ScoreCount, Is.EqualTo(2));
            Assert.That(e.Seats[1].ScoreCount, Is.EqualTo(2));
        }

        [Test]
        public void ProfileNameplatesMapToTheGamesBandsAndLanguages()
        {
            string id = FanmadeFixture.UserId("don");
            fixture.Nameplates[id] = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"title\":{\"name\":\"太鼓の達人\",\"nameTranslations\":{\"en\":\"Taiko Master\",\"zh-Hans\":\"太鼓达人\"},\"style\":\"purple\"},\"dan\":{\"index\":11,\"gold\":false}}");
            using var profiles = new ProfileCenter(fixture.BaseUrl);
            var english = Run(() => profiles.NameplateAsync(id, "en"));
            Assert.That(english.Title, Is.EqualTo("Taiko Master"));
            Assert.That(english.TitleBackground, Is.EqualTo(3));
            Assert.That(english.Dan, Is.EqualTo(11));
            Assert.That(english.Gold, Is.False);
            Assert.That(Run(() => profiles.NameplateAsync(id, "zh")).Title, Is.EqualTo("太鼓达人"));
            Assert.That(Run(() => profiles.NameplateAsync(id, "ko")).Title, Is.EqualTo("太鼓の達人"), "No translation: the title's own name.");
            var plain = Run(() => profiles.NameplateAsync(FanmadeFixture.UserId("ka"), "en"));
            Assert.That(plain.Title, Is.Empty);
            Assert.That(plain.Dan, Is.EqualTo(-1));
            Assert.That(ProfileNameplate.From(Newtonsoft.Json.Linq.JObject.Parse("{\"title\":{\"name\":\"x\",\"style\":\"rainbow\"},\"dan\":{\"index\":99}}"), "en").Rainbow, Is.True);
            Assert.That(ProfileNameplate.From(Newtonsoft.Json.Linq.JObject.Parse("{\"dan\":{\"index\":99}}"), "en").Dan, Is.EqualTo(-1));
        }

        [Test]
        public void ServerCoursesComeWithTheCatalogAndAttemptsGoUpWithTheirAward()
        {
            fixture.Dans = Newtonsoft.Json.Linq.JArray.Parse("[{\"format\":1,\"id\":\"7\",\"title\":{\"ja\":\"十段\"},\"color\":3,\"official\":true," +
                "\"songs\":[{\"chart\":{\"id\":\"" + chart.Id + "\",\"course\":\"Oni\"},\"title\":\"Fixture Song\"}]," +
                "\"exams\":[{\"type\":\"gauge\",\"range\":\"more\",\"value\":[100]}],\"rewards\":{\"dan\":20}}]");
            fixture.DanProfileReply = Newtonsoft.Json.Linq.JObject.Parse("{\"granted\":{\"dan\":{\"index\":20,\"verdict\":4,\"upgraded\":true},\"titles\":[{\"id\":\"m\",\"name\":\"太鼓の達人\",\"new\":true}]},\"nameplate\":{}}");
            var e = Catalog();
            var listed = client.Dans.Single();
            Assert.That(listed.Server, Is.EqualTo(fixture.BaseUrl));
            Assert.That((string)listed.Dans[0]["id"], Is.EqualTo("7"));
            var entries = DanCourseLoader.LoadServer(listed.Server, listed.Dans, new DanCourseResolver(Array.Empty<SongDefinition>(), (server, id) => null));
            Assert.That(entries.Single().Course.Id, Is.EqualTo("7"));
            Assert.That(entries.Single().Course.DanIndex, Is.EqualTo(20));

            Assert.That(client.SubmitDanAttempt(fixture.BaseUrl, "7", 4, 1), Is.Null, "A guest uploads nothing.");
            SignIn(e, 0, "don", "katsu");
            string key = client.SubmitDanAttempt(fixture.BaseUrl + "/", "7", 4, 1);
            Assert.That(key, Is.Not.Null);
            Drain();
            var sent = fixture.DanAttempts.Single();
            Assert.That(sent.User, Is.EqualTo("don"));
            Assert.That((string)sent.Body["courseId"], Is.EqualTo("7"));
            Assert.That((int)sent.Body["verdict"], Is.EqualTo(4));
            Assert.That((int)sent.Body["songsPlayed"], Is.EqualTo(1));
            var award = client.Award(key);
            Assert.That((int)award.Profile["granted"]["dan"]["index"], Is.EqualTo(20));
            Assert.That(client.DanQueue.Pending(e.AccountIdOf(0)), Is.Empty);

            // A server that cannot reach Profile still records the attempt; the game says it comes later
            // and sends the attempt again, so that the server asks Profile again.
            var granted = fixture.DanProfileReply;
            fixture.DanProfileReply = null;
            string later = client.SubmitDanAttempt(fixture.BaseUrl, "7", 1, 1);
            Drain();
            Assert.That(client.Award(later).Profile, Is.Null);
            Assert.That(client.Award(later).Error, Does.Contain("unreachable"));
            Assert.That(client.DanQueue.Pending(e.AccountIdOf(0)).Single().Key, Is.EqualTo(later));
            int uploads = fixture.DanAttempts.Count;
            fixture.DanProfileReply = granted;
            Drain();
            Assert.That(fixture.DanAttempts.Count, Is.GreaterThan(uploads));
            Assert.That((int)fixture.DanAttempts.Last().Body["verdict"], Is.EqualTo(1));
            Assert.That(client.Award(later).Profile, Is.Not.Null);
            Assert.That(client.DanQueue.Pending(e.AccountIdOf(0)), Is.Empty);

            // While the server is down the attempt waits in the queue.
            fixture.Fail["/api/v1/game/dan-attempts"] = 503;
            string waiting = client.SubmitDanAttempt(fixture.BaseUrl, "7", 2, 1);
            Drain();
            Assert.That(client.Award(waiting), Is.Null);
            Assert.That(client.DanQueue.Pending(e.AccountIdOf(0)).Length, Is.EqualTo(1));
            fixture.Fail.TryRemove("/api/v1/game/dan-attempts", out _);
            Drain();
            Assert.That(client.Award(waiting), Is.Not.Null);
        }

        [Test]
        public void TheAccountCenterAnswersAreChecked()
        {
            Assert.That(Assert.Catch<HttpStatusException>(() => Run(() => center.LoginAsync("don", "wrong", "Test"))).Status, Is.EqualTo(401));
            var account = Run(() => center.LoginAsync("don", "katsu", "Test"));
            Run(() => center.RefreshAsync(account));
            Assert.That(account.Username, Is.EqualTo("don"));
            Run(() => center.LogoutAsync(account.Token));
            Assert.That(Assert.Catch<HttpStatusException>(() => Run(() => center.RefreshAsync(account))).Status, Is.EqualTo(401));
            Assert.That(Assert.Catch<HttpStatusException>(() => Run(() => center.TradeAsync(account.Token, FanmadeFixture.Service))).Status, Is.EqualTo(401));
        }
    }
}
