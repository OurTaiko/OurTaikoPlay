using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using NUnit.Framework;
using OurTaiko.Multi;
using OurTaiko.Multi.Proto;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // マルチ演奏 (issue #38) against a room server that lives in the test: Entry's board opens the
    // room screen; a private room is made; the host starts, picks a song in song select, takes a
    // course of the song drawn; the round loads, waits for the room's start, is played with the
    // other player's score beside the lane and reports its result; the room's ranking follows the
    // player's own result screen. The server here is scripted by the test (it only answers join and
    // leave by itself), so every screen is seen in a state the test chose.
    public sealed class MultiRoomFlowTests
    {
        // The room server's end of a stream.
        sealed class FakeRoomServer : IMultiLink
        {
            public readonly List<ClientMessage> Received = new List<ClientMessage>();
            public string Token = "";
            public bool Disposed { get; private set; }
            // How a join is answered: null puts the player in Room, a code refuses with it.
            public string RefuseJoin;
            public readonly Room Room = new Room { Code = "48213", MaxMembers = 5, MinPlayers = 2, Phase = Phase.Lobby };
            public const string Me = "0123456789abcdef0123456789abcdef", Rival = "fedcba9876543210fedcba9876543210";

            readonly Queue<ServerMessage> inbox = new Queue<ServerMessage>();
            TaskCompletionSource<ServerMessage> waiting;

            public IEnumerable<T> Got<T>(Func<ClientMessage, T> part) where T : class => Received.Select(part).Where(p => p != null);

            public Task SendAsync(ClientMessage message)
            {
                Received.Add(message);
                if (message.KindCase == ClientMessage.KindOneofCase.Join)
                {
                    if (RefuseJoin != null) { Push(new ServerMessage { Closed = new Closed { Code = RefuseJoin } }); Push(null); return Task.CompletedTask; }
                    if (Room.Members.Count == 0)
                    {
                        Room.HostId = Me;
                        Room.Members.Add(new Member { UserId = Me, Nickname = "Don", Slot = 0, Connected = true });
                    }
                    Push(new ServerMessage { Welcome = new Welcome { UserId = Me, Nickname = "Don", Protocol = MultiSession.Protocol } });
                    Snapshot();
                }
                else if (message.KindCase == ClientMessage.KindOneofCase.Leave)
                {
                    Push(new ServerMessage { Closed = new Closed { Code = "LEFT" } });
                    Push(null);
                }
                return Task.CompletedTask;
            }

            // The stream is refused as a session the account center no longer knows would be.
            public bool RefuseSession;

            public Task<ServerMessage> ReceiveAsync(CancellationToken cancel)
            {
                if (RefuseSession) return Task.FromException<ServerMessage>(new MultiException("AUTH_REQUIRED"));
                if (inbox.Count > 0) return Task.FromResult(inbox.Dequeue());
                var next = waiting = new TaskCompletionSource<ServerMessage>();
                cancel.Register(() => next.TrySetCanceled());
                return next.Task;
            }

            public void Dispose() => Disposed = true;

            // null ends the stream.
            public void Push(ServerMessage message)
            {
                if (waiting != null) { var next = waiting; waiting = null; next.TrySetResult(message); }
                else inbox.Enqueue(message);
            }

            public Member Member(string id) => Room.Members.First(m => m.UserId == id);

            // The room as it now is, to the player.
            public void Snapshot(int secondsLeft = 0)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Room.ServerTimeMs = now;
                Room.DeadlineMs = secondsLeft > 0 ? now + secondsLeft * 1000L : 0;
                Push(new ServerMessage { Room = Room.Clone() });
            }
        }

        const string Tja = "TITLE:Room\nBPM:240\nOFFSET:0\nCOURSE:Normal\nLEVEL:4\n#START\n1010,\n1010,\n#END\nCOURSE:Oni\nLEVEL:8\n#START\n1111,\n1111,\n#END\n";

        FanmadeFixture fixture;
        FanmadeFixture.Chart chart;
        FakeRoomServer server;
        string avatar;
        GameSettings saved;
        PlayOptions originalFirst;
        Keyboard keyboard;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;

        [SetUp]
        public void SetUp()
        {
            fixture = new FanmadeFixture { SiteSources = new[] { "fanmade", "official" }, Votes = true };
            chart = new FanmadeFixture.Chart
            {
                Title = "Room Song", Subtitle = "Together", AudioName = "song.ogg",
                Tja = Encoding.UTF8.GetBytes(Tja), Audio = File.ReadAllBytes("Assets/OurTaiko/Audio/don.ogg"),
                Difficulties = new List<(string, int, string)> { ("Normal", 4, ""), ("Oni", 8, "") },
            };
            fixture.Charts.Add(chart);
            // The other player's account picture (WebP, as the account center serves them).
            byte[] picture = File.ReadAllBytes("Assets/OurTaiko/Tests/Fixtures/avatar.webp");
            fixture.CustomRequest = context =>
            {
                if (context.Request.Url.AbsolutePath != "/avatars/katsu.webp") return false;
                context.Response.ContentLength64 = picture.Length;
                context.Response.OutputStream.Write(picture, 0, picture.Length);
                context.Response.Close();
                return true;
            };
            avatar = fixture.BaseUrl + "/avatars/katsu.webp";
            server = new FakeRoomServer();
            MultiSession.Forget();
            saved = SettingManager.EnsureInstance().Settings.Clone();
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            originalFirst = PlayOptions.Shared;
            PlayOptions.Shared = new PlayOptions();
            var input = InputSystem.settings;
            originalBackground = input.backgroundBehavior;
            originalEditorBehavior = input.editorInputBehaviorInPlayMode;
            input.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            input.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("RoomKeyboard");
            keyboard.MakeCurrent();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var leave = MultiSession.LeaveAsync();
            while (!leave.IsCompleted) yield return null;
            if (MultiDirector.Instance != null) Object.Destroy(MultiDirector.Instance.gameObject);
            MultiTransport.Open = MultiTransport.OpenGrpc;
            MultiSession.GameSession = MultiSession.SiteSessionAsync;
            MultiSession.ResolveAddress = MultiSession.ResolveSiteAddressAsync;
            MultiSession.Forget();
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            // A test that opened no scene of the game leaves the test runner's own alone.
            var previous = SceneManager.GetActiveScene();
            if (!previous.name.StartsWith("InitTestScene") && previous.name != "RoomTearDown")
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("RoomTearDown"));
                yield return SceneManager.UnloadSceneAsync(previous);
            }
            fixture.Dispose();
            TestData.UseServers();
            SceneSwitcher.EnsureInstance().SetSeats(Side.P1);
            if (saved != null) SettingManager.EnsureInstance().UseUnsaved(saved);
            PlayOptions.Shared = originalFirst;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = originalBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
        }

        // The fixture as a site with two sources, 1P signed in, and the room server in place of gRPC.
        IEnumerator Connect()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers();
            var online = OnlineManager.Instance;
            online.UseUnsavedSite(TestData.OnlineCache, fixture.BaseUrl,
                ("Fanmade", "https://fanmade.example", "fanmade"), ("Database", "https://database.example", "official"));
            online.UseAccounts(new AccountStore(null, "test"), new AccountCenter(fixture.BaseUrl));
            online.UseProfiles(new ProfileCenter(fixture.BaseUrl));
            yield return TestData.Connect("don");
            MultiSession.ResolveAddress = _ => Task.FromResult("http://room.test");
            MultiTransport.Open = (address, token, cancel) => { server.Token = token; return server; };
        }

        static IEnumerator WaitForScene(string scene, float seconds = 30)
        {
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == scene && !SceneSwitcher.Instance.IsInputBlocked,
                () => "Still in " + SceneManager.GetActiveScene().name, seconds);
        }

        static MultiRoomScene RoomScreen => Object.FindAnyObjectByType<MultiRoomScene>();

        IEnumerator OpenRoomScreen()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            yield return new WaitForSecondsRealtime(.3f);
            entry.Don();
            yield return Waits.Until(() => entry.Flow.IsModeReady(entry.Now), seconds: 3);
            yield return new WaitForSecondsRealtime(1);
            int board = Array.FindIndex(entry.Modes, mode => mode.Scene == SceneSwitcher.MultiRoomScene);
            Assert.That(board, Is.EqualTo(4), "マルチ演奏 sits between ゴーストバトル and ゲーム設定.");
            Assert.That(entry.Modes[board].Title, Is.EqualTo(L10n.Get("Menu", "entry.multi.title")));
            Assert.That(entry.Modes[5].Scene, Is.EqualTo(SceneSwitcher.SettingScene));
            entry.Ka(board);
            yield return new WaitForSecondsRealtime(.8f);
            TestCapture.Capture("EntryMultiBoard.png");
            entry.Don();
            yield return WaitForScene(SceneSwitcher.MultiRoomScene);
        }

        // Song select, opened for a pick: folders are opened until an online song shows, which is focused.
        static IEnumerator FocusOnlineSong(SongSelectScene select)
        {
            int opened = 0;
            for (int guard = 0; guard < 6; guard++)
            {
                var items = select.Manager.Items;
                int song = First(items.Count, i => items[i].Kind == SongSelectManager.ItemKind.Song && OnlineManager.Instance.IsOnline(items[i].Song));
                if (song >= 0)
                {
                    for (int steps = 0; select.Manager.Focused != song && steps < 30; steps++)
                    {
                        if (song > select.Manager.Focused) select.Manager.Right(); else select.Manager.Left();
                        yield return new WaitForSecondsRealtime(0.25f);
                    }
                    Assert.That(select.Manager.Focused, Is.EqualTo(song));
                    yield break;
                }
                int folder = First(items.Count, i => i > opened && items[i].Kind == SongSelectManager.ItemKind.Folder);
                Assert.That(folder, Is.GreaterThan(0), "No folder left to open.");
                select.Manager.OpenFolderAt(folder);
                opened = folder;
                yield return new WaitForSecondsRealtime(0.4f);
            }
            Assert.Fail("No online song in song select.");
        }

        // The first index below count that fits, or -1.
        static int First(int count, Func<int, bool> fits)
        {
            for (int i = 0; i < count; i++) if (fits(i)) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator ARoundIsPlayedFromTheMenuToTheRanking()
        {
            yield return Connect();
            yield return OpenRoomScreen();
            var screen = RoomScreen;
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Menu));
            Assert.That(screen.Controls.Select(c => c.Key), Is.EqualTo(new[] { "quick", "create", "join", "back" }));
            Assert.That(screen.AllText, Does.Contain(MultiText.Get("menu.create.title")));
            Assert.That(SceneSwitcher.Instance.Seated, Is.EquivalentTo(new[] { Side.P1 }));
            yield return null;
            TestCapture.Capture("MultiMenu.png");
            // A tablet (4:3) and a phone (19.5:9): the screen stays whole, in the middle.
            TestCapture.Capture("MultiMenuTablet.png", 2048, 1536, TestCapture.StageIsCentred(screen.stage));
            TestCapture.Capture("MultiMenuPhone.png", 2622, 1206, TestCapture.StageIsCentred(screen.stage));

            // A private room: the stream signs in with the site's game session and joins.
            screen.Move(1);
            Assert.That(screen.Focused.Key, Is.EqualTo("create"));
            screen.Confirm();
            yield return Waits.Until(() => screen.Shown == MultiRoomScene.View.Room, () => "Not in the room: " + screen.Message, 10);
            Assert.That(fixture.SessionUser(server.Token), Is.EqualTo("don"), "The stream signs in with the player's game session.");
            Assert.That(server.Received[0].Join.TargetCase, Is.EqualTo(Join.TargetOneofCase.CreatePrivate));
            Assert.That(server.Received[0].Join.Protocol, Is.EqualTo(MultiSession.Protocol));
            Assert.That(MultiSession.IsHost, Is.True);
            Assert.That(screen.AllText, Does.Contain(MultiText.Format("room.number", "48213")).And.Contain(MultiText.Get("room.private")));
            // Alone, the host cannot start.
            Assert.That(screen.Controls.Select(c => c.Key), Does.Not.Contain("start"));
            Assert.That(screen.Controls.Select(c => c.Key), Does.Contain("open"));

            // Someone joins: they can be removed, and the round can start.
            server.Room.Members.Add(new Member { UserId = FakeRoomServer.Rival, Nickname = "Katsu", AvatarUrl = avatar, Slot = 1, Connected = true });
            server.Snapshot();
            yield return Waits.Until(() => screen.Controls.Any(c => c.Key == "start"), seconds: 5);
            Assert.That(screen.AllText, Does.Contain("Katsu").And.Contain(MultiText.Format("room.players", 2)));
            // Their card wears their account picture once it has come; a player without one keeps the letter.
            yield return Waits.Until(() => MultiAvatars.Get(avatar) != null, "The account picture did not load.", 10);
            yield return null; yield return null;
            Assert.That(Object.FindObjectsByType<UnityEngine.UI.Image>().Select(i => i.sprite), Has.Member(MultiAvatars.Get(avatar)));
            Assert.That(screen.AllText.Split('\n'), Does.Contain("D").And.Not.Contain("K"));
            Assert.That(screen.Controls.Select(c => c.Key), Does.Contain("kick:" + FakeRoomServer.Rival));
            Assert.That(screen.Focused.Key, Is.EqualTo("start"), "The host's focus starts on the start plate.");

            // A stamp from the other player shows over their card; the player sends one back.
            server.Push(new ServerMessage { Stamp = new StampSent { UserId = FakeRoomServer.Rival, StampId = 3 } });
            yield return Waits.Until(() => screen.AllText.Contains(MultiText.Stamp(3)), seconds: 5);
            yield return null;
            TestCapture.Capture("MultiLobby.png");
            screen.Press("stamps");
            yield return null; yield return null;
            Assert.That(screen.StampsOpen, Is.True);
            Assert.That(screen.Controls.Count(c => c.Key.StartsWith("stamp:")), Is.EqualTo(MultiSession.StampCount));
            TestCapture.Capture("MultiStamps.png");
            screen.Press("stamp:12");
            yield return null; yield return null;
            Assert.That(screen.StampsOpen, Is.False);
            Assert.That(server.Got(m => m.Stamp).Single().StampId, Is.EqualTo(12));

            // The host starts; the room asks everyone for a song.
            screen.Press("start");
            yield return null;
            Assert.That(server.Got(m => m.LockMembers).Count(), Is.EqualTo(1));
            server.Room.Phase = Phase.SongSelect; server.Room.Round = 1;
            server.Snapshot(90);
            yield return Waits.Until(() => screen.Controls.Any(c => c.Key == "choose"), seconds: 5);
            Assert.That(screen.Controls.Select(c => c.Key), Does.Contain("chance"));
            Assert.That(screen.AllText, Does.Contain(MultiText.Get("phase.songSelect")));
            yield return null;
            TestCapture.Capture("MultiSongSelect.png");

            // Song select picks for the room: the banner says which room and how long is left.
            screen.Press("choose");
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(SceneSwitcher.Instance.MultiPick, Is.True);
            yield return null;
            Assert.That(MultiDirector.Instance.BannerText, Does.Contain(MultiText.Format("banner.room", "48213")).And.Contain(MultiText.Get("phase.songSelect")));
            var select = Object.FindAnyObjectByType<SongSelectScene>();
            Assert.That(select.Manager.CanJoinSecond, Is.False, "A room has one player of this game.");
            yield return FocusOnlineSong(select);
            yield return new WaitForSecondsRealtime(.8f);
            var picked = select.Manager.FocusedSong;
            string source = MultiSession.SourceOf(OnlineManager.Instance.ChartOf(picked).Server);
            Assert.That(source, Is.EqualTo("fanmade").Or.EqualTo("official"));
            SceneSwitcher.Instance.LastDifficulty = (int)Difficulty.Oni;
            select.Manager.Confirm();
            yield return Waits.Until(() => select.CourseFade >= 1, seconds: 5);
            yield return null;
            select.Manager.Confirm();
            // Deciding sends the pick and returns to the room instead of playing.
            yield return WaitForScene(SceneSwitcher.MultiRoomScene);
            var pick = server.Got(m => m.PickSong).Single();
            Assert.That((pick.Source, pick.ChartId, pick.Course), Is.EqualTo((source, chart.Id, "Oni")));
            Assert.That(SceneSwitcher.Instance.MultiPick, Is.False);
            screen = RoomScreen;

            // The draw: the song, then its courses. The player takes Normal and is ready.
            var song = new Song { Source = source, ChartId = chart.Id, Title = "Room Song", Subtitle = "Together", ChartJson = ByteString.CopyFromUtf8(chart.ToJson().ToString()) };
            song.Courses.Add(new Course { Course_ = "Normal", Level = 4 });
            song.Courses.Add(new Course { Course_ = "Oni", Level = 8 });
            server.Room.Song = song; server.Room.SongPickedBy = FakeRoomServer.Me;
            foreach (var member in server.Room.Members) { member.Picked = true; member.Course = "Oni"; }
            server.Room.Phase = Phase.SongDraw;
            server.Snapshot(4);
            yield return Waits.Until(() => screen.AllText.Contains("Room Song"), seconds: 5);
            Assert.That(screen.AllText, Does.Contain(MultiText.Format("draw.pickedBy", "Don")));
            yield return null;
            TestCapture.Capture("MultiSongDraw.png");
            server.Room.Phase = Phase.Course;
            server.Member(FakeRoomServer.Rival).Ready = true;
            server.Snapshot(15);
            yield return Waits.Until(() => screen.Controls.Any(c => c.Key == "course:Normal"), seconds: 5);
            Assert.That(screen.Focused.Key, Is.EqualTo("course:Oni"), "The focus starts on the course the player asked for.");
            // Courses are the skin's icons here too: on the plates to choose from and on the members' cards.
            // (A frame on, the last drawing of the screen is gone.)
            yield return null;
            var courseIcons = MultiArt.Load().courseIcons;
            var onScreen = Object.FindObjectsByType<UnityEngine.UI.Image>().Select(i => i.sprite).ToList();
            Assert.That(onScreen.Count(sprite => sprite == courseIcons[3]), Is.EqualTo(3), "Oni: its plate and both members' cards.");
            Assert.That(onScreen.Count(sprite => sprite == courseIcons[1]), Is.EqualTo(1), "Normal: its plate.");
            Assert.That(screen.AllText, Does.Not.Contain(MultiText.Course("Oni")).And.Not.Contain(MultiText.Course("Normal")).And.Contain("★8").And.Contain("★4"));
            yield return null;
            TestCapture.Capture("MultiCourse.png");
            screen.Move(-1);
            Assert.That(screen.Focused.Key, Is.EqualTo("course:Normal"));
            screen.Confirm();
            yield return null;
            Assert.That(server.Got(m => m.PickCourse).Single().Course, Is.EqualTo("Normal"));
            Assert.That(server.Got(m => m.SetReady).Single().Ready, Is.True);

            // Loading: the room's song on the player's course, from the room screen.
            var me = server.Member(FakeRoomServer.Me);
            me.Course = "Normal"; me.Ready = true;
            foreach (var member in server.Room.Members) member.Playing = true;
            server.Room.Phase = Phase.Loading;
            server.Snapshot(90);
            yield return WaitForScene(SceneSwitcher.GameScene, 40);
            var play = Object.FindAnyObjectByType<PlayScene>();
            Assert.That(play.IsMulti, Is.True);
            Assert.That(SceneSwitcher.Instance.SelectedCourse, Is.EqualTo("Normal"));
            Assert.That(play.Session.Chart.Notes.Count, Is.EqualTo(4), "Normal's block, not the Oni the player first asked for.");
            Assert.That(play.Lane1.AutoPlay, Is.False);
            // Loaded, it waits for the room: nothing moves until every player is ready.
            yield return Waits.Until(() => play.IsWaitingForMulti, seconds: 10);
            Assert.That(server.Got(m => m.Loaded).Single().Ok, Is.True);
            Assert.That(play.MultiHud.IsWaiting, Is.True);
            Assert.That(play.MultiHud.CardCount, Is.EqualTo(2));
            Assert.That(play.MultiHud.CardOf(FakeRoomServer.Rival).Rank, Is.EqualTo("1"), "Equal scores share a place.");
            Assert.That(play.pauseButton.gameObject.activeSelf, Is.False, "A round cannot be paused.");
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(server.Got(m => m.Status), Is.Empty, "The song has not started.");
            TestCapture.Capture("MultiPlayWaiting.png");

            // The room starts the round: the play runs, with the other player's score beside it.
            me.Loaded = true; server.Member(FakeRoomServer.Rival).Loaded = true;
            // From here the room takes both for away from its screen, until their games say otherwise.
            me.Away = true; server.Member(FakeRoomServer.Rival).Away = true;
            server.Room.Phase = Phase.Playing;
            server.Snapshot();
            server.Push(new ServerMessage { StartPlay = new StartPlay { Round = 1 } });
            yield return Waits.Until(() => !play.IsWaitingForMulti, seconds: 5);
            Assert.That(play.MultiHud.IsWaiting, Is.False);
            var statuses = new PlayStatuses();
            statuses.Members.Add(new MemberStatus { UserId = FakeRoomServer.Rival, Connected = true, Status = new PlayerStatus { Score = 123450, Combo = 42, Gauge = 8600 } });
            server.Push(new ServerMessage { Statuses = statuses });
            yield return Waits.Until(() => play.MultiHud.CardOf(FakeRoomServer.Rival).Score == "123,450", seconds: 5);
            Assert.That(play.MultiHud.CardOf(FakeRoomServer.Rival), Is.EqualTo(("1", "Katsu", "123,450")));
            Assert.That(play.MultiHud.CardOf(FakeRoomServer.Me).Rank, Is.EqualTo("2"));
            Assert.That(play.MultiHud.CardOf(FakeRoomServer.Me).Name, Is.EqualTo("Don"));
            // A player's account picture lies behind their card, whole; a card without one stays plain.
            Assert.That(play.MultiHud.PhotoOf(FakeRoomServer.Rival), Is.SameAs(MultiAvatars.GetSquare(avatar)).And.Not.Null);
            Assert.That(MultiAvatars.GetSquare(avatar).rect.size, Is.EqualTo(new Vector2(MultiAvatars.SquareSize, MultiAvatars.SquareSize)));
            Assert.That(play.MultiHud.PhotoOf(FakeRoomServer.Me), Is.Null);
            // No pause, no restart.
            play.TogglePause();
            Assert.That(play.IsPaused, Is.False);
            yield return new WaitForSecondsRealtime(1.2f);
            TestCapture.Capture("MultiPlay.png");

            // The play's end goes to the room; the player's own result screen comes first.
            yield return Waits.Until(() => server.Got(m => m.Finish).Any(), seconds: 30);
            var finish = server.Got(m => m.Finish).Single();
            Assert.That(finish.Final.Bad, Is.EqualTo(4), "Nothing was struck.");
            Assert.That(finish.Cleared, Is.False);
            yield return WaitForScene(SceneSwitcher.ResultScene, 30);
            Assert.That(SceneSwitcher.Instance.ReturnScene, Is.EqualTo(SceneSwitcher.MultiRoomScene));
            var result = new Multi.Proto.PlayResult { Round = 1, TeamScore = 987650, Source = source, ChartId = chart.Id, Title = "Room Song" };
            result.Players.Add(new PlayerResult
            {
                UserId = FakeRoomServer.Rival, Nickname = "Katsu", AvatarUrl = avatar, Course = "Oni", Level = 8, Rank = 1, Award = Award.SuperStar, Slot = 1,
                Cleared = true, FullCombo = true, Status = new PlayerStatus { Score = 987650, Good = 7, Ok = 1, MaxCombo = 8, Gauge = 10000 },
            });
            result.Players.Add(new PlayerResult
            {
                UserId = FakeRoomServer.Me, Nickname = "Don", Course = "Normal", Level = 4, Rank = 2, Award = Award.NoAward, Slot = 0,
                Status = finish.Final,
            });
            server.Room.Result = result;
            server.Room.Phase = Phase.Result;
            foreach (var member in server.Room.Members) { member.Playing = member.Loaded = member.Ready = member.Picked = false; }
            server.Snapshot(10);
            yield return null;
            Assert.That(server.Got(m => m.Returned), Is.Empty, "At the player's own result screen the player is not back.");
            Assert.That(MultiDirector.Instance.BannerText, Does.Contain(MultiText.Get("phase.result")), "The result screen says what the room is doing.");

            // Leaving asks the player about the chart; "not now" goes on without a vote.
            var own = Object.FindAnyObjectByType<ResultScene>();
            yield return ChartRatingFlowTests.Leave(own);
            Assert.That(own.Rating.Focused, Is.EqualTo(ChartRatingPrompt.Good));
            own.Rating.Move(1);
            own.Rating.Press();
            Assert.That(own.IsLeaving, Is.True);
            Assert.That(fixture.ChartVotes, Is.Empty);

            // Back in the room: the game says so, and again until the room has taken it in.
            yield return WaitForScene(SceneSwitcher.MultiRoomScene);
            screen = RoomScreen;
            yield return Waits.Until(() => server.Got(m => m.Returned).Count() >= 2, "The room was not told the player is back.", 10);
            me.Away = false;
            server.Snapshot(10);
            yield return null; yield return null;
            int told = server.Got(m => m.Returned).Count();
            // The ranking, then the lobby again.
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Result));
            Assert.That(screen.AllText, Does.Contain("Katsu").And.Contain("987,650").And.Contain(MultiText.Get("result.superStar"))
                .And.Contain(MultiText.Format("result.team", "987,650")).And.Contain(MultiText.Get("result.failed")));
            // Courses and judgments are the skin's icons, not words; the picture comes in when fetched.
            var art = MultiArt.Load();
            Sprite[] Shown() => Object.FindObjectsByType<UnityEngine.UI.Image>().Where(i => i.gameObject.scene == screen.gameObject.scene).Select(i => i.sprite).ToArray();
            foreach (var icon in new[] { art.courseIcons[3], art.courseIcons[1], art.judgeGood, art.judgeOk, art.judgeBad, art.labelMaxCombo, art.labelDrumroll })
                Assert.That(Shown(), Has.Member(icon), icon.name);
            Assert.That(art.labelMaxCombo.name, Is.EqualTo("JudgeLabelMaxCombo"));
            Assert.That(screen.AllText, Does.Not.Contain(MultiText.Course("Oni")).And.Not.Contain(L10n.Get("Common", "judge.bad")).And.Not.Contain("ombo"));
            Assert.That(screen.AllText, Does.Contain("★8").And.Contain("★4"));
            yield return Waits.Until(() => MultiAvatars.Get(avatar) != null, "The account picture did not load.", 10);
            yield return null; yield return null;
            Assert.That(MultiAvatars.Get(avatar).rect.size, Is.EqualTo(new Vector2(MultiAvatars.Size, MultiAvatars.Size)));
            Assert.That(Shown(), Has.Member(MultiAvatars.Get(avatar)), "Katsu's picture is on the row.");
            Assert.That(screen.AllText.Split('\n'), Does.Contain("D").And.Not.Contain("K"), "A player without a picture keeps the letter.");
            TestCapture.Capture("MultiResult.png");
            server.Room.Phase = Phase.Lobby;
            server.Snapshot();
            screen.Press("ok");
            yield return null; yield return null;
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Room));
            // Katsu is still at their own result screen: the lobby says who. That is not being in
            // the room, so with the host alone in it no round can begin.
            Assert.That(screen.AllText, Does.Contain(MultiText.Get("member.away")).And.Contain(MultiText.Format("room.needMore", 1)));
            Assert.That(screen.Controls.Select(c => c.Key), Does.Contain("lastResult").And.Not.Contain("start"), "One player in the room is too few.");
            Assert.That(screen.Focused.Key, Is.EqualTo("stamps"), "Without the start plate the focus is not on Katsu's remove button.");
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(server.Got(m => m.Returned).Count(), Is.EqualTo(told), "Once the room knows, it is not said again.");
            // A third player comes in: two are in the room, so the host may start, and is told
            // that Katsu would be left out.
            server.Room.Members.Add(new Member { UserId = "00112233445566778899aabbccddeeff", Nickname = "Mekadon", Slot = 2, Connected = true });
            server.Snapshot();
            yield return null; yield return null;
            Assert.That(screen.Controls.Select(c => c.Key), Does.Contain("start"));
            Assert.That(screen.Focused.Key, Is.EqualTo("start"));
            Assert.That(screen.AllText, Does.Contain(MultiText.Get("member.away")).And.Contain(MultiText.Format("room.startLeaves", 1)));
            TestCapture.Capture("MultiLobbyWaiting.png");
            // Katsu comes back: everyone is in the room.
            server.Member(FakeRoomServer.Rival).Away = false;
            server.Snapshot();
            yield return null; yield return null;
            Assert.That(screen.AllText, Does.Not.Contain(MultiText.Get("member.away")).And.Not.Contain(MultiText.Format("room.startLeaves", 1)));
            Assert.That(screen.AllText, Does.Contain(MultiText.Format("room.players", 3)));
            Assert.That(screen.Controls.Select(c => c.Key), Does.Contain("lastResult").And.Contain("start"));
            screen.Press("lastResult");
            yield return null; yield return null;
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Result), "The last ranking can be looked at again.");
            screen.Back();
            yield return null; yield return null;

            // Leaving: back to the menu, and from there to Entry.
            screen.Press("leave");
            yield return Waits.Until(() => screen.Shown == MultiRoomScene.View.Menu, seconds: 5);
            Assert.That(server.Got(m => m.Leave).Count(), Is.EqualTo(1));
            Assert.That(MultiSession.InRoom, Is.False);
            Assert.That(screen.Message, Is.Empty, "Leaving is the player's own doing.");
            screen.Back();
            yield return WaitForScene(SceneSwitcher.EntryScene);
        }

        [UnityTest]
        public IEnumerator ARefusedJoinAndAClosedRoomSayWhy()
        {
            yield return Connect();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MultiRoomScene);
            yield return Waits.Until(() => RoomScreen != null && !SceneSwitcher.Instance.IsInputBlocked, seconds: 10);
            var screen = RoomScreen;

            // The number entry: on-screen keys and the keyboard's digits; five digits make a number.
            screen.Press("join");
            yield return null; yield return null;
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Code));
            foreach (int digit in new[] { 1, 2, 3 }) screen.Press("key:" + digit);
            yield return null; yield return null;
            Assert.That(screen.Code, Is.EqualTo("123"));
            screen.Press("key:delete");
            yield return null; yield return null;
            screen.TypeDigit(0); screen.TypeDigit(4); screen.TypeDigit(5); screen.TypeDigit(6);
            Assert.That(screen.Code, Is.EqualTo("12045"), "A sixth digit is not taken.");
            yield return null; yield return null;
            TestCapture.Capture("MultiCode.png");

            // No such room: the entry stays, with the reason.
            server.RefuseJoin = "ROOM_NOT_FOUND";
            screen.Press("key:ok");
            yield return Waits.Until(() => !screen.IsBusy && screen.Message.Length > 0, seconds: 10);
            Assert.That(server.Received[0].Join.RoomCode, Is.EqualTo("12045"));
            Assert.That(screen.Message, Is.EqualTo(MultiText.Describe("ROOM_NOT_FOUND")));
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Code));
            Assert.That(MultiSession.IsConnected, Is.False);

            // The room is there now. Then the host removes the player: the menu says so.
            server = new FakeRoomServer();
            server.Room.HostId = FakeRoomServer.Rival;
            server.Room.Members.Add(new Member { UserId = FakeRoomServer.Rival, Nickname = "Katsu", Slot = 0, Connected = true });
            server.Room.Members.Add(new Member { UserId = FakeRoomServer.Me, Nickname = "Don", Slot = 1, Connected = true });
            server.Room.Public = true;
            screen.Press("key:ok");
            yield return Waits.Until(() => screen.Shown == MultiRoomScene.View.Room, () => screen.Message, 10);
            Assert.That(MultiSession.IsHost, Is.False);
            Assert.That(screen.Controls.Select(c => c.Key), Is.EqualTo(new[] { "stamps", "leave" }), "A guest waits for the host.");
            Assert.That(screen.AllText, Does.Contain(MultiText.Get("room.public")).And.Contain(MultiText.Get("room.waitingHost")), "A public room is started by its host too.");
            Assert.That(MultiText.Describe("LEFT_BEHIND"), Is.EqualTo(MultiText.Get("error.LEFT_BEHIND")), "Being left out of the room has words of its own.");
            // A refused action is a notice; the room goes on.
            server.Push(new ServerMessage { Notice = new Notice { Code = "NOT_HOST" } });
            yield return Waits.Until(() => MultiDirector.Instance.ToastText == MultiText.Describe("NOT_HOST"), seconds: 5);
            Assert.That(screen.Shown, Is.EqualTo(MultiRoomScene.View.Room));
            server.Push(new ServerMessage { Closed = new Closed { Code = "KICKED" } });
            server.Push(null);
            yield return Waits.Until(() => screen.Shown == MultiRoomScene.View.Menu, seconds: 5);
            Assert.That(screen.Message, Is.EqualTo(MultiText.Describe("KICKED")));
            Assert.That(server.Disposed, Is.True);
            yield return null;
            TestCapture.Capture("MultiMenuKicked.png");
        }

        static IEnumerator Await(Task task, float seconds = 10)
        {
            yield return Waits.Until(() => task.IsCompleted, seconds: seconds);
        }

        // The stream drops without a word (a tunnel, a sleeping phone): the session joins its room
        // again by number, as the server keeps the player's place for a while.
        [UnityTest]
        public IEnumerator ADroppedStreamJoinsTheRoomAgain()
        {
            var links = new List<FakeRoomServer>();
            MultiSession.ResolveAddress = _ => Task.FromResult("http://room.test");
            MultiSession.GameSession = (renew, _) => Task.FromResult(new string('a', 64));
            MultiTransport.Open = (address, token, cancel) => { var link = new FakeRoomServer(); links.Add(link); return link; };
            var notices = new List<string>();
            var closed = new List<string>();
            Action<string> onNotice = notices.Add, onClosed = closed.Add;
            MultiSession.NoticeReceived += onNotice; MultiSession.Closed += onClosed;
            try
            {
                var join = MultiSession.QuickMatchAsync();
                yield return Await(join);
                Assert.That(join.IsCompletedSuccessfully, Is.True, join.Exception?.ToString());
                Assert.That(MultiSession.Room.Code, Is.EqualTo("48213"));
                Assert.That(links.Single().Received[0].Join.TargetCase, Is.EqualTo(Join.TargetOneofCase.QuickMatch));

                links[0].Push(null);
                yield return Waits.Until(() => notices.Contains("RECONNECTED"), seconds: 10);
                Assert.That(links, Has.Count.EqualTo(2));
                Assert.That(links[0].Disposed, Is.True);
                Assert.That(links[1].Received[0].Join.RoomCode, Is.EqualTo("48213"), "The same room, by its number.");
                Assert.That(MultiSession.IsConnected, Is.True);
                Assert.That(closed, Is.Empty);

                // A room that is gone by then ends the session, and says so once.
                MultiTransport.Open = (address, token, cancel) => { var link = new FakeRoomServer { RefuseJoin = "ROOM_NOT_FOUND" }; links.Add(link); return link; };
                links[1].Push(null);
                yield return Waits.Until(() => closed.Count > 0, seconds: 10);
                Assert.That(closed, Is.EqualTo(new[] { "CONNECTION_LOST" }));
                Assert.That(MultiSession.InRoom, Is.False);
                Assert.That(MultiSession.ClosedCode, Is.EqualTo("CONNECTION_LOST"));
            }
            finally { MultiSession.NoticeReceived -= onNotice; MultiSession.Closed -= onClosed; }
        }

        // A game session the server no longer accepts is traded for once more, then the join goes on.
        [UnityTest]
        public IEnumerator ARefusedSessionIsTradedForOnce()
        {
            var links = new List<FakeRoomServer>();
            var asked = new List<bool>();
            MultiSession.ResolveAddress = _ => Task.FromResult("http://room.test");
            MultiSession.GameSession = (renew, _) => { asked.Add(renew); return Task.FromResult(new string(renew ? 'b' : 'a', 64)); };
            MultiTransport.Open = (address, token, cancel) =>
            {
                var link = new FakeRoomServer { Token = token, RefuseSession = token[0] == 'a' };
                links.Add(link);
                return link;
            };
            var join = MultiSession.CreatePrivateAsync();
            yield return Await(join);
            Assert.That(join.IsCompletedSuccessfully, Is.True, join.Exception?.ToString());
            Assert.That(asked, Is.EqualTo(new[] { false, true }));
            Assert.That(links.Select(l => l.Token[0]), Is.EqualTo(new[] { 'a', 'b' }));
            Assert.That(MultiSession.InRoom, Is.True);

            // Refused again, it is the player's to sign in: no third try.
            var leave = MultiSession.LeaveAsync();
            yield return Await(leave);
            MultiSession.GameSession = (renew, _) => { asked.Add(renew); return Task.FromResult(new string('a', 64)); };
            var again = MultiSession.CreatePrivateAsync();
            yield return Await(again);
            Assert.That(again.IsFaulted, Is.True);
            Assert.That((again.Exception.GetBaseException() as MultiException)?.Code, Is.EqualTo("AUTH_REQUIRED"));
            Assert.That(asked, Is.EqualTo(new[] { false, true, false, true }));
            Assert.That(MultiSession.IsConnected, Is.False);
        }
    }
}
