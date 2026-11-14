using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The game connects to its two built-in servers only: no servers.json, and no login screen
    // between Entry and song select.
    public sealed class BuiltInServersFlowTests
    {
        string folder;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            // The servers.json test loads no scene: the one left by the last tear-down stays.
            if (previous.name != "BuiltInServersTearDown")
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("BuiltInServersTearDown"));
                yield return SceneManager.UnloadSceneAsync(previous);
            }
            TestData.UseServers();
            TestSongs.Install();
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        static string[] Urls(OnlineManager online) => online.BuiltInServers.Select(server => server.baseUrl).ToArray();

        static FanmadeFixture.Chart Song() => new FanmadeFixture.Chart
        {
            Title = "First", Tja = System.Text.Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = new byte[] { 1, 2, 3, 4 }, Categories = new[] { "game" },
        };

        // The built-in servers are song sources of one site. The game asks the site for both song
        // lists at once and signs a seat in to both with one session: one sign-in on the screen,
        // where each server alone took a sign-in and four requests of its own.
        [UnityTest]
        public IEnumerator TheSourcesOfOneSiteAreLoadedAndSignedInToTogether()
        {
            using var fixture = new FanmadeFixture { SiteSources = new[] { "fanmade", "official" } };
            fixture.Charts.Add(Song());
            fixture.AddAccountScore("don", fixture.Charts[0], "Oni", 700000);
            TestData.UseServers();
            var online = OnlineManager.Instance;
            online.UseUnsavedSite(TestData.OnlineCache, fixture.BaseUrl,
                ("Fanmade", "https://fanmade.example", "fanmade"), ("Database", "https://database.example", "official"));
            online.UseAccounts(new AccountStore(null, "test"), new AccountCenter(fixture.BaseUrl));
            online.UseProfiles(new ProfileCenter(fixture.BaseUrl));
            Assert.That(Urls(online), Is.EqualTo(new[] { "https://fanmade.example", "https://database.example" }), "Each is still known by its own address.");

            var load = online.LoadSongListAsync();
            while (!load.IsCompleted) yield return null;
            load.GetAwaiter().GetResult();
            Assert.That(fixture.Requests, Is.EquivalentTo(new[] { "GET /api/v1/game/sources" }), "Both song lists in one request.");
            Assert.That(online.Client.Endpoints.Select(e => e.IsConnected), Is.All.True);
            Assert.That(online.Client.Endpoints.Select(e => e.ChartCount), Is.All.EqualTo(fixture.Charts.Count));
            Assert.That(online.SongListSummary, Does.Contain("Fanmade").And.Contain("Database"), "The summary still says what each source has.");

            var signIn = online.SignInAsync(0, "don", "katsu");
            while (!signIn.IsCompleted) yield return null;
            signIn.GetAwaiter().GetResult();
            Assert.That(online.Client.Endpoints.Select(e => e.Seats[0].IsAuthenticated), Is.All.True);
            Assert.That(fixture.Requests.Count(r => r == "POST /api/v1/game/tokens"), Is.EqualTo(1), "One session for both sources.");
            Assert.That(fixture.Requests.Count(r => r == "GET /api/v1/game/sources"), Is.EqualTo(2), "The song lists, then the seat's scores and playlists.");
            Assert.That(fixture.Requests.Where(r => r.Contains("/api/v1/sources/")), Is.Empty, "Nothing was asked of a source by itself.");
            Assert.That(online.LoginSummary, Does.Contain("DON"));
            Assert.That(online.LoginSummary.Split('•'), Has.Length.EqualTo(1), "One sign-in is reported, not one a source.");
            foreach (var e in online.Client.Endpoints)
                Assert.That(online.Client.Best(online.Client.Charts.First(c => c.Server == e.Id && c.Id == fixture.Charts[0].Id), (int)Difficulty.Oni).Score, Is.EqualTo(700000));
        }

        // A site that cannot answer for its sources fails as a whole: they are not asked one by one
        // instead, and nobody is signed in to one of them alone.
        [UnityTest]
        public IEnumerator ASiteThatCannotAnswerForItsSourcesFailsAsAWhole()
        {
            using var fixture = new FanmadeFixture();
            fixture.Charts.Add(Song());
            TestData.UseServers();
            var online = OnlineManager.Instance;
            online.UseUnsavedSite(TestData.OnlineCache, fixture.BaseUrl,
                ("Fanmade", "https://fanmade.example", "fanmade"), ("Database", "https://database.example", "official"));
            online.UseAccounts(new AccountStore(null, "test"), new AccountCenter(fixture.BaseUrl));
            online.UseProfiles(new ProfileCenter(fixture.BaseUrl));

            var load = online.LoadSongListAsync();
            while (!load.IsCompleted) yield return null;
            load.GetAwaiter().GetResult();
            Assert.That(online.Client.Endpoints.Select(e => e.IsConnected), Is.All.False);
            Assert.That(fixture.Requests, Is.EquivalentTo(new[] { "GET /api/v1/game/sources" }), "No source is asked by itself.");
            Assert.That(online.SongListSummary.Split('•'), Has.Length.EqualTo(1), "One failure, the site's.");
            Assert.That(online.SongListSummary, Does.Contain(fixture.BaseUrl));
        }

        // A manager starting in a data folder leaves no servers.json there, and one a player still
        // has from an older version is neither read nor changed.
        [UnityTest]
        public IEnumerator TheGameNeitherCreatesNorReadsServersJson()
        {
            using var fixture = new FanmadeFixture();
            folder = Path.Combine(Application.temporaryCachePath, "built-in-servers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "servers.json");
            string old = "{\"servers\":[{\"enabled\":true,\"name\":\"Mine\",\"baseUrl\":\"" + fixture.BaseUrl
                + "\",\"username\":\"don\",\"password\":\"katsu\",\"httpProxy\":\"\",\"autoLogin\":true}]}";
            string[] builtIn = { "https://fanmade.ourtaiko.org", "https://database.ourtaiko.org" };
            // GamePaths has no seam of its own: the data folder is swapped for this test only.
            var root = typeof(GamePaths).GetField("root", BindingFlags.NonPublic | BindingFlags.Static);
            string real = GamePaths.Root;
            Object.DestroyImmediate(OnlineManager.Instance.gameObject);
            root.SetValue(null, folder);
            try
            {
                var online = OnlineManager.EnsureInstance();
                Assert.That(Urls(online), Is.EqualTo(builtIn));
                Assert.That(File.Exists(file), Is.False, "No servers.json is created.");
                Object.DestroyImmediate(online.gameObject);

                File.WriteAllText(file, old);
                online = OnlineManager.EnsureInstance();
                Assert.That(Urls(online), Is.EqualTo(builtIn), "A server listed in an old servers.json is not connected to.");
                Assert.That(online.Client.Endpoints, Is.Empty);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(File.ReadAllText(file), Is.EqualTo(old), "The player's old file is left as it is.");
                Assert.That(Directory.GetFiles(folder, "servers.json*"), Has.Length.EqualTo(1));
                Assert.That(fixture.Requests, Is.Empty);
                Object.DestroyImmediate(online.gameObject);
            }
            finally
            {
                if (OnlineManager.Instance != null) Object.DestroyImmediate(OnlineManager.Instance.gameObject);
                root.SetValue(null, real);
                OnlineManager.EnsureInstance();
            }
        }

        // 演奏ゲーム leaves Entry straight for song select; on the way every folder closes and the
        // songs folder is read again, so a song added since the last visit is listed.
        [UnityTest]
        public IEnumerator EntryGoesStraightToSongSelectAndListsSongsAddedSince()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded("ServerLogin"), Is.False, "The login scene is not in the build.");
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            folder = Path.Combine(Application.temporaryCachePath, "built-in-songs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            static string Chart(string title) => $"TITLE:{title}\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1,\n#END\n";
            File.WriteAllText(Path.Combine(folder, "before.tja"), Chart("Before"));
            LocalSongLibrary.EnsureInstance().UseRoot(folder);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var manager = Object.FindAnyObjectByType<SongSelectScene>().Manager;
            Assert.That(manager.Folders[0].SongCount, Is.EqualTo(1));
            manager.OpenFolderAt(0);
            Assert.That(manager.OpenFolderKey, Is.EqualTo(SongSelectManager.LocalKey));

            Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            File.WriteAllText(Path.Combine(folder, "after.tja"), Chart("After"));
            var entry = Object.FindAnyObjectByType<EntryScene>();
            Assert.That(entry.view.boards[0].scene, Is.EqualTo(SceneSwitcher.SongSelectScene));
            Assert.That(entry.view.boards.Select(board => board.scene), Has.None.EqualTo("ServerLogin"));
            yield return Waits.Until(() => entry.Flow.CanJoin(entry.Now) && !SceneSwitcher.Instance.IsInputBlocked);
            entry.Don();
            yield return Waits.Until(() => entry.Flow.IsModeReady(entry.Now));
            Assert.That(entry.Flow.SelectedMode, Is.Zero);
            entry.Don();
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.SongSelectScene && !SceneSwitcher.Instance.IsInputBlocked);
            Assert.That(SceneSwitcher.LastScene, Is.EqualTo(SceneSwitcher.EntryScene), "No scene between Entry and song select.");
            manager = Object.FindAnyObjectByType<SongSelectScene>().Manager;
            Assert.That(manager.OpenFolder, Is.Null, "Every folder is closed again.");
            Assert.That(manager.Folders[0].SongCount, Is.EqualTo(2), "The songs folder was read again.");
        }
    }
}
