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
            fixture.AddAccountScore("don", chart, "Oni", 1002540, 1);
            fixture.AccountScores[0]["bad"] = 10;
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
            Assert.That(select.Manager.BoardCount, Is.EqualTo(6), "3 songs, 2 folders and the root もどる.");
            Assert.That(select.Manager.KindAt(5), Is.EqualTo(SongSelectManager.ItemKind.Back));
            Assert.That(select.Manager.KindAt(3), Is.EqualTo(SongSelectManager.ItemKind.Folder));
            Assert.That(select.Manager.KindAt(4), Is.EqualTo(SongSelectManager.ItemKind.Folder));
            Assert.That(select.Manager.OpenFolder, Is.Null, "Folders start closed.");
            Assert.That(select.Manager.Folders.Select(f => f.Title), Is.EqualTo(new[] { "Game", "Pop" }));
            Assert.That(select.Manager.Folders[0].Genre, Is.EqualTo(3), "Category genre GAME uses the game-music board.");
            yield return Focus(select, 3);
            yield return new WaitForSecondsRealtime(0.8f);
            var folderView = select.wheel.GetComponentsInChildren<FolderBoardView>(false).Single(v => v.title.text == "Game");
            Assert.That(folderView.count.text, Does.StartWith("1 songs"));
            Assert.That(folderView.charaLeft.enabled, Is.True, "The open folder board shows its characters.");
            TestCapture.Capture("SongSelectFolderClosed.png");

            // Opening: the folder board becomes もどる and the song follows it, focused on もどる.
            select.Manager.Confirm();
            Assert.That(select.Manager.OpenFolder, Is.Not.Null);
            Assert.That(select.Manager.Focused, Is.EqualTo(3));
            Assert.That(select.Manager.KindAt(3), Is.EqualTo(SongSelectManager.ItemKind.Back));
            var song = select.Manager.SongAt(4);
            Assert.That(online.IsOnline(song), Is.True);
            Assert.That(song.ReadInfo().Title, Is.EqualTo("Fixture Song"));
            Assert.That(select.Manager.KindAt(5), Is.EqualTo(SongSelectManager.ItemKind.Folder), "Pop stays closed after it.");
            yield return new WaitForSecondsRealtime(0.5f);
            select.Manager.Right();
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(select.Manager.FocusedSong, Is.SameAs(song));
            var board = select.wheel.GetComponentsInChildren<SongBoardView>().Single(v => v.title.text == "Fixture Song");
            Assert.That(board.crown.enabled, Is.True, "A server clear with misses still shows a silver crown.");
            Assert.That(board.crown.sprite, Is.SameAs(select.crownClear[(int)Difficulty.Oni]));
            TestCapture.Capture("SongSelectOnline.png");

            SceneSwitcher.Instance.LastDifficulty = (int)Difficulty.Oni;
            select.Manager.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1, 5);
            Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Oni));
            yield return null;
            Assert.That(select.view.bestScore.DisplayedScore, Is.EqualTo(1002540), "Best score comes from login bootstrap.");
            Assert.That(select.view.bestScore.group.alpha, Is.EqualTo(1));
            Assert.That(select.view.cards[(int)Difficulty.Oni].crown.sprite, Is.SameAs(select.smallCrowns[1]));
            Assert.That(ScoreStore.Shared.Get(song.name, Difficulty.Oni), Is.Null);
            select.Manager.Confirm();
            yield return WaitForScene(SceneSwitcher.GameScene, 30);

            // The verified copy plays: API title, downloaded audio.
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session.Chart.Title, Is.EqualTo("Fixture Song"));
            Assert.That(song.audioPath, Is.Not.Null.And.Not.Empty);
            Assert.That(play.music.AudioLength(), Is.GreaterThan(0));
            if (AudioEngine.Instance.Available) Assert.That(song.music, Is.Null, "Online audio must bypass Unity decoding.");
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            int pendingBeforeResult = -1;
            bool uploadedBeforeResult = true;
            SceneSwitcher.Instance.SceneChanging += scene =>
            {
                if (scene != SceneSwitcher.ResultScene) return;
                pendingBeforeResult = online.Client.PendingCount(online.Client.Endpoints[0]);
                uploadedBeforeResult = !fixture.AcceptedScores.IsEmpty;
            };
            yield return WaitUntil(() => play.SongTime > 0.2, 10);
            play.Hit(isKa: false, right: false);
            yield return null;
            play.Hit(isKa: true, right: true);
            yield return WaitForScene(SceneSwitcher.ResultScene, 20);
            Assert.That(pendingBeforeResult, Is.Zero, "PlayScene must not enqueue the score before ResultScene loads.");
            Assert.That(uploadedBeforeResult, Is.False);

            yield return WaitUntil(() => !fixture.AcceptedScores.IsEmpty, 10);
            fixture.AcceptedScores.TryDequeue(out var body);
            Assert.That((string)body["songId"], Is.EqualTo(chart.Id));
            Assert.That((string)body["difficulty"], Is.EqualTo("Oni"));
            Assert.That((int)body["ClearStatus"], Is.EqualTo((int)play.Result.StoredCrown));
            Assert.That((long)body["good"] + (long)body["ok"] + (long)body["bad"], Is.EqualTo(4), "Every note is judged.");
            var inputs = (JArray)body["replay_data"]["inputs"];
            Assert.That(inputs.Select(i => (int)i[1]), Is.EqualTo(new[] { 1, 3 }), "Left don, then right ka.");
            Assert.That(online.Client.PendingCount(online.Client.Endpoints[0]), Is.Zero);
            Assert.That(ScoreStore.Shared.Get(song.name, Difficulty.Oni), Is.Null, "Online plays never enter local best scores.");

            // Reopening Result retains the display but does not submit the same play again.
            int uploads = fixture.Requests.Count(r => r == "POST /api/v1/game/scores");
            int previousBest = SceneSwitcher.Instance.LastResult.PreviousBest;
            yield return WaitUntil(() => !SceneSwitcher.Instance.IsInputBlocked, 10);
            SceneSwitcher.Instance.SwitchScene(SceneSwitcher.ResultScene);
            yield return WaitForScene(SceneSwitcher.ResultScene);
            var uploadTask = online.Client.WaitForUploadsAsync();
            yield return WaitUntil(() => uploadTask.IsCompleted, 10);
            uploadTask.GetAwaiter().GetResult();
            Assert.That(fixture.Requests.Count(r => r == "POST /api/v1/game/scores"), Is.EqualTo(uploads));
            Assert.That(fixture.AcceptedScores.IsEmpty, Is.True);
            Assert.That(SceneSwitcher.Instance.LastResult.PreviousBest, Is.EqualTo(previousBest));

            // Back from the result: the folder is open again on the song just played.
            yield return WaitUntil(() => !SceneSwitcher.Instance.IsInputBlocked, 10);
            SceneSwitcher.Instance.ReturnToMenu();
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            select = Object.FindFirstObjectByType<SongSelectScene>();
            Assert.That(select.Manager.KindAt(3), Is.EqualTo(SongSelectManager.ItemKind.Back));
            Assert.That(select.Manager.FocusedSong, Is.SameAs(song));
        }

        [UnityTest]
        public IEnumerator PracticeEntryLogsInShowsHistoryAndDownloadsWithoutUploadingPracticeScores()
        {
            fixture.AddAccountScore("don", chart, "Oni", 1002540, 1);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(new ServerList { servers = { fixture.Server("don", "katsu") } });
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            entry.Don();
            yield return new WaitForSecondsRealtime(1.5f);
            entry.Ka(1);
            yield return new WaitForSecondsRealtime(.3f);
            entry.Don();
            yield return WaitForScene(SceneSwitcher.ServerLoginScene);
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True);
            var login = Object.FindFirstObjectByType<ServerLoginScene>();
            Assert.That(login.view.message.text, Does.Contain("過去のスコア"));
            login.Activate(ServerLoginView.Item.Login);
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(fixture.Requests, Does.Contain("POST /api/v1/game/login"));
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True);

            var select = Object.FindFirstObjectByType<SongSelectScene>();
            select.Manager.OpenFolderAt(3);
            yield return Focus(select, 4);
            yield return new WaitForSecondsRealtime(.8f);
            var song = select.Manager.FocusedSong;
            Assert.That(OnlineManager.Instance.IsOnline(song), Is.True);
            SceneSwitcher.Instance.LastDifficulty = (int)Difficulty.Oni;
            select.Manager.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1, 5);
            yield return null;
            Assert.That(select.view.bestScore.DisplayedScore, Is.EqualTo(1002540));
            Assert.That(select.view.bestScore.group.alpha, Is.EqualTo(1));
            Assert.That(select.view.cards[(int)Difficulty.Oni].crown.sprite, Is.SameAs(select.smallCrowns[1]));
            select.Manager.Confirm();
            yield return WaitForScene(SceneSwitcher.PracticeScene, 30);
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.IsPractice, Is.True);
            Assert.That(play.Session.Chart.Title, Is.EqualTo("Fixture Song"));
            Assert.That(play.music.AudioLength(), Is.GreaterThan(0));
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            yield return WaitUntil(() => !SceneSwitcher.Instance.IsInputBlocked, 10);
            play.ConfirmPractice();
            yield return null;
            play.ConfirmPractice();
            Assert.That(play.IsPaused, Is.False);
            yield return WaitUntil(() => play.IsPaused, 20);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.PracticeScene));
            Assert.That(fixture.AcceptedScores.IsEmpty, Is.True);
            Assert.That(OnlineManager.Instance.Client.PendingCount(OnlineManager.Instance.Client.Endpoints[0]), Is.Zero);
            Assert.That(SongScores.Get(song, Difficulty.Oni).score, Is.EqualTo(1002540), "Practice preserves server history.");
        }

        [UnityTest]
        public IEnumerator OnlineCrownsFollowClearStatusForBoardsAndCourseCards()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            fixture.AddAccountScore("don", chart, "Oni", 500000, 0);
            TestData.UseServers(new ServerList { servers = { fixture.Server("don", "katsu") } });
            var online = OnlineManager.Instance;
            var endpoint = online.Client.Add(online.Servers.servers[0]);
            var connect = online.Client.ConnectAsync(endpoint, false);
            yield return WaitUntil(() => connect.IsCompleted, 10);
            connect.GetAwaiter().GetResult();
            online.RefreshSongs();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(.6f);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            select.Manager.OpenFolderAt(3);
            yield return Focus(select, 4);
            yield return new WaitForSecondsRealtime(.8f);
            var song = select.Manager.FocusedSong;
            var board = select.wheel.GetComponentsInChildren<SongBoardView>().Single(v => v.title.text == "Fixture Song");
            Assert.That(board.crown.enabled, Is.False, "ClearStatus 0 wins even when bad and ok are zero.");
            int score = 500000;
            foreach (int status in new[] { 1, 2, 3, 0 })
            {
                // Contradictory counters ensure the explicit status, not inferred judgments, wins.
                Assert.That(online.Client.Submit(online.ChartOf(song), (int)Difficulty.Oni,
                    new FanmadeScore { Score = score += 10000, Good = 100, Bad = 10, ClearStatus = status }), Is.True);
                var upload = online.Client.WaitForUploadsAsync();
                yield return WaitUntil(() => upload.IsCompleted, 10);
                upload.GetAwaiter().GetResult();
                yield return null;
                Assert.That(SongScores.Get(song, Difficulty.Oni).crown, Is.EqualTo((Crown)status));
                Assert.That(board.crown.enabled, Is.EqualTo(status != 0));
                if (status != 0)
                {
                    var art = status == 1 ? select.crownClear : status == 2 ? select.crownFullCombo : select.crownDonderful;
                    Assert.That(board.crown.sprite, Is.SameAs(art[(int)Difficulty.Oni]));
                }
                select.Manager.Confirm();
                yield return WaitUntil(() => select.CourseFade >= 1, 5);
                yield return null;
                Assert.That(select.view.cards[(int)Difficulty.Oni].crown.sprite, Is.SameAs(select.smallCrowns[status]));
                for (int i = 0; i < 7 && select.Manager.Cursor.Selected != Difficulty.Back; i++) select.Manager.Left();
                Assert.That(select.Manager.Cursor.Selected, Is.EqualTo(Difficulty.Back));
                select.Manager.Confirm();
                yield return new WaitForSecondsRealtime(.8f);
            }
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
            select.Manager.Confirm();
            Assert.That(select.Manager.BoardCount, Is.EqualTo(7));
            yield return Focus(select, 5);
            Assert.That(select.Manager.KindAt(5), Is.EqualTo(SongSelectManager.ItemKind.Folder));
            select.Manager.Confirm();
            Assert.That(select.Manager.OpenFolder, Does.EndWith("/pop"));
            Assert.That(select.Manager.KindAt(3), Is.EqualTo(SongSelectManager.ItemKind.Folder), "Game closed again.");
            Assert.That(select.Manager.KindAt(4), Is.EqualTo(SongSelectManager.ItemKind.Back), "The empty Pop folder holds only もどる.");
            Assert.That(select.Manager.Focused, Is.EqualTo(4));
            Assert.That(select.Manager.BoardCount, Is.EqualTo(6));
            yield return new WaitForSecondsRealtime(0.4f);
            select.Manager.Confirm();
            Assert.That(select.Manager.OpenFolder, Is.Null);
            Assert.That(select.Manager.KindAt(4), Is.EqualTo(SongSelectManager.ItemKind.Folder));
            Assert.That(select.Manager.Focused, Is.EqualTo(4), "Closing focuses the folder board.");

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
            Assert.That(select.Manager.BoardCount, Is.EqualTo(6));
            yield return Focus(select, 4);
            select.Manager.Confirm();
            // Pop holds the 1500 odd charts: もどる, then one more もどる after every ten songs.
            Assert.That(select.Manager.BoardCount, Is.EqualTo(4 + 1 + 1500 + 149 + 1));
            Assert.That(select.Manager.KindAt(4), Is.EqualTo(SongSelectManager.ItemKind.Back));
            Assert.That(select.Manager.SongAt(5).ReadInfo().Title, Is.EqualTo("Bulk 1"));
            Assert.That(select.Manager.KindAt(15), Is.EqualTo(SongSelectManager.ItemKind.Back));
            Assert.That(select.Manager.SongAt(16).ReadInfo().Title, Is.EqualTo("Bulk 21"));
            float started = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - started < 1) { frames++; yield return null; }
            Debug.Log($"SongSelect with a 1500-song folder open: {frames} frames in 1 s");
            // Step through the open folder: views are reused, not added per song.
            for (int i = 0; i < 12; i++) { select.Manager.Right(); yield return new WaitForSecondsRealtime(0.2f); }
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(select.Manager.FocusedSong.ReadInfo().Title, Is.EqualTo("Bulk 21"));
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
            Assert.That(select.Manager.BoardCount, Is.EqualTo(4), "No folders without servers, only the root もどる.");
            Assert.That(select.Manager.Focused, Is.Zero, "The first song is focused, not もどる.");
            // The wheel wraps: one step back from the first song is the root もどる, which leaves for Entry.
            yield return new WaitForSecondsRealtime(0.3f);
            select.Manager.Left();
            Assert.That(select.Manager.KindAt(select.Manager.Focused), Is.EqualTo(SongSelectManager.ItemKind.Back));
            Assert.That(select.Manager.FocusedSong, Is.Null);
            yield return new WaitForSecondsRealtime(0.8f);
            TestCapture.Capture("SongSelectRootBack.png");
            select.Manager.Confirm();
            yield return WaitForScene(SceneSwitcher.EntryScene);
        }

        // Steps the wheel with ka until the board at `index` is focused.
        static IEnumerator Focus(SongSelectScene select, int index)
        {
            for (int guard = 0; select.Manager.Focused != index && guard < 20; guard++)
            {
                if (index > select.Manager.Focused) select.Manager.Right(); else select.Manager.Left();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Assert.That(select.Manager.Focused, Is.EqualTo(index));
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
