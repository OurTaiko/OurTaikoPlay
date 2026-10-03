using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // ServerLogin → SongSelect → download on the loading curtain → play → score upload, against the
    // local fixture API.
    public sealed class ServerLoginFlowTests
    {
        FanmadeFixture fixture;
        FanmadeFixture.Chart chart;

        [SetUp]
        public void SetUp()
        {
            fixture = new FanmadeFixture();
            // A one-measure chart and a short real OGG, so the song ends quickly.
            chart = new FanmadeFixture.Chart
            {
                Title = "Fixture Song", Subtitle = "Online", AudioName = "song.ogg",
                Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja("Oni", 1, 240)),
                Audio = File.ReadAllBytes("Assets/OurTaiko/Audio/don.ogg"),
            };
            fixture.Charts.Add(chart);
        }

        // Unload the scene first: resetting the servers destroys the online songs it may still draw.
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("ServerLoginTearDown"));
            yield return SceneManager.UnloadSceneAsync(previous);
            fixture.Dispose();
            TestData.UseServers(new ServerList());
        }

        [UnityTest]
        public IEnumerator LoginListsOnlineChartsThenDownloadsPlaysAndUploads()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(new ServerList { servers = { fixture.Server("don", "wrong") } });
            var online = OnlineManager.Instance;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.ServerLoginScene);
            yield return null;
            var login = Object.FindFirstObjectByType<ServerLoginScene>();
            var view = login.view;
            Assert.That(view.serverName.text, Is.EqualTo("Fixture"));
            Assert.That(view.serverUrl.text, Is.EqualTo(fixture.BaseUrl));
            Assert.That(view.username.text, Is.EqualTo("don"));
            Assert.That(login.Focus, Is.EqualTo(ServerLoginView.Item.Login), "Remembered credentials focus ログイン.");
            Assert.That(login.IsBusy, Is.False, "autoLogin is off, so nothing is sent yet.");
            TestCapture.Capture("ServerLogin.png");

            // Wrong password: an error, and the scene stays for another try.
            login.Activate(ServerLoginView.Item.Login);
            Assert.That(login.IsBusy, Is.True);
            yield return WaitUntil(() => !login.IsBusy, 10);
            Assert.That(view.message.text, Does.Contain("正しくありません"));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.ServerLoginScene));
            TestCapture.Capture("ServerLoginError.png");

            // Ka moves the focus round the fields and buttons.
            login.Move(1);
            Assert.That(login.Focus, Is.EqualTo(ServerLoginView.Item.Guest));
            login.Move(-2);
            Assert.That(login.Focus, Is.EqualTo(ServerLoginView.Item.Password));

            view.password.text = "katsu";
            login.Activate(ServerLoginView.Item.Login);
            yield return WaitUntil(() => login.Outcomes[0] == ServerLoginScene.Outcome.LoggedIn, 10);
            Assert.That(view.message.text, Does.Contain("DON"));
            Assert.That(online.Servers.servers[0].password, Is.EqualTo("katsu"));
            Assert.That(online.Servers.servers[0].autoLogin, Is.True, "A successful login is remembered for next time.");
            yield return WaitForScene(SceneSwitcher.SongSelectScene);

            // The categories follow the local songs as closed folders: Game (1 song) and Pop (empty).
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(select.BoardCount, Is.EqualTo(6), "3 songs, 2 folders and the root もどる.");
            Assert.That(select.KindAt(5), Is.EqualTo(SongSelectScene.BoardKind.Back));
            Assert.That(select.KindAt(3), Is.EqualTo(SongSelectScene.BoardKind.Folder));
            Assert.That(select.KindAt(4), Is.EqualTo(SongSelectScene.BoardKind.Folder));
            Assert.That(select.OpenFolder, Is.Null, "Folders start closed.");
            Assert.That(select.Folders.Select(f => f.Title), Is.EqualTo(new[] { "Game", "Pop" }));
            Assert.That(select.Folders[0].Genre, Is.EqualTo(3), "Category genre GAME uses the game-music board.");
            yield return Focus(select, 3);
            yield return new WaitForSecondsRealtime(0.8f);
            var folderView = select.wheel.GetComponentsInChildren<FolderBoardView>(false).Single(v => v.title.text == "Game");
            Assert.That(folderView.count.text, Does.StartWith("1 songs"));
            Assert.That(folderView.charaLeft.enabled, Is.True, "The open folder board shows its characters.");
            TestCapture.Capture("SongSelectFolderClosed.png");

            // Opening: the folder board becomes もどる and the song follows it, focused on もどる.
            select.Confirm();
            Assert.That(select.OpenFolder, Is.Not.Null);
            Assert.That(select.Focused, Is.EqualTo(3));
            Assert.That(select.KindAt(3), Is.EqualTo(SongSelectScene.BoardKind.Back));
            var song = select.SongAt(4);
            Assert.That(online.IsOnline(song), Is.True);
            Assert.That(song.ReadInfo().Title, Is.EqualTo("Fixture Song"));
            Assert.That(select.KindAt(5), Is.EqualTo(SongSelectScene.BoardKind.Folder), "Pop stays closed after it.");
            yield return new WaitForSecondsRealtime(0.5f);
            select.Right();
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(select.FocusedSong, Is.SameAs(song));
            TestCapture.Capture("SongSelectOnline.png");

            SceneSwitcher.Instance.LastDifficulty = (int)Difficulty.Oni;
            select.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1, 5);
            Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Oni));
            select.Confirm();
            yield return WaitForScene(SceneSwitcher.GameScene, 30);

            // The verified copy plays: API title, downloaded audio.
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session.Chart.Title, Is.EqualTo("Fixture Song"));
            Assert.That(song.music, Is.Not.Null);
            Assert.That(song.music.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            yield return WaitUntil(() => play.SongTime > 0.2, 10);
            play.Hit(isKa: false, right: false);
            yield return null;
            play.Hit(isKa: true, right: true);
            yield return WaitForScene(SceneSwitcher.ResultScene, 20);

            yield return WaitUntil(() => !fixture.AcceptedScores.IsEmpty, 10);
            fixture.AcceptedScores.TryDequeue(out var body);
            Assert.That((string)body["songId"], Is.EqualTo(chart.Id));
            Assert.That((string)body["difficulty"], Is.EqualTo("Oni"));
            Assert.That((long)body["good"] + (long)body["ok"] + (long)body["bad"], Is.EqualTo(4), "Every note is judged.");
            var inputs = (JArray)body["replay_data"]["inputs"];
            Assert.That(inputs.Select(i => (int)i[1]), Is.EqualTo(new[] { 1, 3 }), "Left don, then right ka.");
            Assert.That(online.Client.PendingCount(online.Client.Endpoints[0]), Is.Zero);

            // Back from the result: the folder is open again on the song just played.
            yield return WaitUntil(() => !SceneSwitcher.Instance.IsInputBlocked, 10);
            SceneSwitcher.Instance.ReturnToMenu();
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            select = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(select.KindAt(3), Is.EqualTo(SongSelectScene.BoardKind.Back));
            Assert.That(select.FocusedSong, Is.SameAs(song));
        }

        [UnityTest]
        public IEnumerator GuestSkipAndADownloadErrorReturnToTheSongList()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            var down = new ServerConfig { name = "Down", baseUrl = "http://127.0.0.1:9", enabled = true };
            var disabled = new ServerConfig { name = "Off", baseUrl = fixture.BaseUrl, enabled = false };
            TestData.UseServers(new ServerList { servers = { fixture.Server(), disabled, down } });
            var online = OnlineManager.Instance;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.ServerLoginScene);
            yield return null;
            var login = Object.FindFirstObjectByType<ServerLoginScene>();
            Assert.That(login.Focus, Is.EqualTo(ServerLoginView.Item.Username), "No account yet: the name field first.");
            Assert.That(login.view.progress.text, Is.EqualTo("1 / 2"), "Disabled servers are not shown.");
            login.Activate(ServerLoginView.Item.Login);
            Assert.That(login.IsBusy, Is.False, "ログイン without an account only asks for one.");
            login.Activate(ServerLoginView.Item.Guest);
            yield return WaitUntil(() => login.ServerIndex == 1, 10);
            Assert.That(login.Outcomes[0], Is.EqualTo(ServerLoginScene.Outcome.Guest));
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/login"));

            // The unreachable server fails as a guest too; スキップ leaves it out.
            login.Activate(ServerLoginView.Item.Guest);
            yield return WaitUntil(() => !login.IsBusy, 10);
            Assert.That(login.view.message.text, Does.Contain("NETWORK_"));
            login.Activate(ServerLoginView.Item.Skip);
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(login.Outcomes, Is.EqualTo(new[] { ServerLoginScene.Outcome.Guest, ServerLoginScene.Outcome.Skipped }));

            // Only one folder at a time: opening Pop closes Game; もどる closes Pop again.
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            yield return Focus(select, 3);
            select.Confirm();
            Assert.That(select.BoardCount, Is.EqualTo(7));
            yield return Focus(select, 5);
            Assert.That(select.KindAt(5), Is.EqualTo(SongSelectScene.BoardKind.Folder));
            select.Confirm();
            Assert.That(select.OpenFolder, Does.EndWith("/pop"));
            Assert.That(select.KindAt(3), Is.EqualTo(SongSelectScene.BoardKind.Folder), "Game closed again.");
            Assert.That(select.KindAt(4), Is.EqualTo(SongSelectScene.BoardKind.Back), "The empty Pop folder holds only もどる.");
            Assert.That(select.Focused, Is.EqualTo(4));
            Assert.That(select.BoardCount, Is.EqualTo(6));
            yield return new WaitForSecondsRealtime(0.4f);
            select.Confirm();
            Assert.That(select.OpenFolder, Is.Null);
            Assert.That(select.KindAt(4), Is.EqualTo(SongSelectScene.BoardKind.Folder));
            Assert.That(select.Focused, Is.EqualTo(4), "Closing focuses the folder board.");

            var song = online.Folders[0].Songs.Single();
            fixture.Fail["/api/v1/charts/" + chart.Id] = 500;
            SceneSwitcher.Instance.Play(song, "Oni", false);
            yield return WaitForScene(SceneSwitcher.SongLoadingScene, 10);
            var loading = Object.FindFirstObjectByType<SongLoadingScene>();
            yield return WaitUntil(() => loading.Error != null, 10);
            Assert.That(loading.Error, Is.EqualTo("HTTP_500"));
            yield return null;
            Assert.That(SceneSwitcher.Instance.Curtain.status.text, Is.EqualTo("HTTP_500"));
            yield return WaitForScene(SceneSwitcher.SongSelectScene, 15);
            Assert.That(fixture.Downloads, Is.Zero);
        }

        // ESE lists ~3000 charts: the wheel binds pooled board views only while boards are on screen.
        [UnityTest]
        public IEnumerator LargeCatalogsShareAFewPooledBoards()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            for (int i = 0; i < 3000; i++)
                fixture.Charts.Add(new FanmadeFixture.Chart { Title = "Bulk " + i, Tja = chart.Tja, Audio = chart.Audio, Categories = new[] { i % 2 == 0 ? "game" : "pop" } });
            TestData.UseServers(new ServerList { servers = { fixture.Server() } });
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.ServerLoginScene);
            yield return null;
            var login = Object.FindFirstObjectByType<ServerLoginScene>();
            login.Activate(ServerLoginView.Item.Guest);
            yield return WaitUntil(() => login.IsBusy == false, 60);
            Assert.That(login.view.message.text, Does.Contain("3001 曲"));
            yield return WaitForScene(SceneSwitcher.SongSelectScene, 30);

            var select = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(select.BoardCount, Is.EqualTo(6));
            yield return Focus(select, 4);
            select.Confirm();
            // Pop holds the 1500 odd charts: もどる, then one more もどる after every ten songs.
            Assert.That(select.BoardCount, Is.EqualTo(4 + 1 + 1500 + 149 + 1));
            Assert.That(select.KindAt(4), Is.EqualTo(SongSelectScene.BoardKind.Back));
            Assert.That(select.SongAt(5).ReadInfo().Title, Is.EqualTo("Bulk 1"));
            Assert.That(select.KindAt(15), Is.EqualTo(SongSelectScene.BoardKind.Back));
            Assert.That(select.SongAt(16).ReadInfo().Title, Is.EqualTo("Bulk 21"));
            float started = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - started < 1) { frames++; yield return null; }
            Debug.Log($"SongSelect with a 1500-song folder open: {frames} frames in 1 s");
            // Step through the open folder: views are reused, not added per song.
            for (int i = 0; i < 12; i++) { select.Right(); yield return new WaitForSecondsRealtime(0.2f); }
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(select.FocusedSong.ReadInfo().Title, Is.EqualTo("Bulk 21"));
            int views = select.wheel.GetComponentsInChildren<SongBoardView>(true).Length
                + select.wheel.GetComponentsInChildren<FolderBoardView>(true).Length;
            Assert.That(views, Is.LessThan(30), "Board views are pooled.");
            Assert.That(select.wheel.GetComponentsInChildren<SongBoardView>(false).Count(v => v.title.text == "Bulk 21"), Is.EqualTo(1));
            TestCapture.Capture("SongSelectLargeFolder.png");
        }

        [UnityTest]
        public IEnumerator WithoutServersTheSceneGoesStraightToSongSelect()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.ServerLoginScene);
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(select.BoardCount, Is.EqualTo(4), "No folders without servers, only the root もどる.");
            Assert.That(select.Focused, Is.Zero, "The first song is focused, not もどる.");
            // The wheel wraps: one step back from the first song is the root もどる, which leaves for Entry.
            yield return new WaitForSecondsRealtime(0.3f);
            select.Left();
            Assert.That(select.KindAt(select.Focused), Is.EqualTo(SongSelectScene.BoardKind.Back));
            Assert.That(select.FocusedSong, Is.Null);
            yield return new WaitForSecondsRealtime(0.8f);
            TestCapture.Capture("SongSelectRootBack.png");
            select.Confirm();
            yield return WaitForScene(SceneSwitcher.EntryScene);
        }

        // Steps the wheel with ka until the board at `index` is focused.
        static IEnumerator Focus(SongSelectScene select, int index)
        {
            for (int guard = 0; select.Focused != index && guard < 20; guard++)
            {
                if (index > select.Focused) select.Right(); else select.Left();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Assert.That(select.Focused, Is.EqualTo(index));
        }

        static IEnumerator WaitForScene(string scene, float seconds = 20)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Waiting for " + scene); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }
    }
}
