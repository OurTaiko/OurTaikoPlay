using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Multi;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko
{
    // ゴーストバトル's screen (issue #53), reached from Entry's board: the player picks someone's
    // recorded play and plays against it (SceneSwitcher.PlayGhost) on its own song and block, which
    // the song loading scene downloads like any online song. It looks and works like マルチ演奏's
    // room (PlateUi): one view at a time, drawn in code and drawn again when what it shows changes.
    //
    //   Servers     a board for each connected built-in server, with how many plays it lists.
    //   Categories  the chosen server's categories: すべて, then its catalog's.
    //   Plays       the chosen category's recorded plays (GET /api/v2/replays?categoryId=), newest
    //               first, a few to a page. A category loads its first page the first time it is
    //               opened and further ones as the player nears their end.
    //   Result      after a battle and the two result boards: the player and the recording side by
    //               side, the winner first. Closing it returns to the page the play was picked on.
    //
    // Ka (← →) moves between the controls, don (Enter) presses the focused one, a tap or click
    // presses a control directly, Back goes one view up and from the servers to Entry. Plays of
    // replaced files are not listed, and one whose song has changed since the list came is refused
    // with a message.
    public sealed partial class GhostSelectScene : MonoBehaviour, ISettledScreen
    {
        public RectTransform stage;
        public AudioSource sfx, bgm;
        // How long the screen waits for a server to connect before saying there is none.
        public float connectSeconds = 10;
        // Plays on a page.
        public const int PageRows = 5;

        public enum View { Servers, Categories, Plays, Result }

        public sealed class Server
        {
            public string Id = "", Name = "";
            public readonly List<Category> Categories = new List<Category>();
        }

        // A server's category and the plays listed under it.
        public sealed class Category
        {
            // A catalog category's id; null for すべて.
            public string Id, Title = "", Server = "";
            public readonly List<FanmadeReplay> Rows = new List<FanmadeReplay>();
            public int Total { get; internal set; }
            public bool Started { get; internal set; }
            public bool IsLoading => Loading != null && !Loading.IsCompleted;
            // The page of PageRows now shown, from 0.
            public int Page { get; internal set; }
            public int Pages => Math.Max(1, (Rows.Count + PageRows - 1) / PageRows);
            internal int? NextPage;
            internal string Error;
            internal Task Loading;
        }

        // The battle just played, for the Result view.
        public sealed class Outcome
        {
            public string Title = "", PlayerName = "", GhostName = "";
            public PlayResult Player, Ghost;
            public bool PlayerWon => Player.Score > Ghost.Score;
            public bool IsDraw => Player.Score == Ghost.Score;
        }

        public View Shown { get; private set; }
        public IReadOnlyList<PlateUi.Control> Controls => ui.Controls;
        public PlateUi.Control Focused => ui.Focused;
        public string AllText => ui.AllText;
        public IReadOnlyList<Server> Servers => servers;
        public Server CurrentServer { get; private set; }
        public Category CurrentCategory { get; private set; }
        public IReadOnlyList<FanmadeReplay> Rows => CurrentCategory?.Rows ?? (IReadOnlyList<FanmadeReplay>)Array.Empty<FanmadeReplay>();
        public Outcome LastOutcome { get; private set; }
        // What the screen says under its list: progress, an empty list, or why a pick failed.
        public string Status { get; private set; } = "";
        public bool IsLoading => !connected || servers.Any(s => s.Categories[0].IsLoading) || (CurrentCategory?.IsLoading ?? false);
        public bool IsStarting { get; private set; }
        public bool HasLeft { get; private set; }
        // The lists have come in and no battle is starting.
        public bool IsSettled => !IsLoading && !IsStarting && !HasLeft;

        // Where the player was when a battle began, so its result returns there.
        static (string Server, string Category, int Page, string ScoreId)? resume;

        readonly List<Server> servers = new List<Server>();
        SceneSwitcher switcher;
        MultiArt art;
        GhostArt ghostArt;
        PlateUi ui;
        CancellationTokenSource cancel;
        bool connected, resumed, dirty = true;
        // A pick failed: its message stays until the player does something.
        bool failed;
        float deadline, nextServerCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => resume = null;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            art = MultiArt.Load();
            ghostArt = GhostArt.Load();
            cancel = new CancellationTokenSource();
            ui = new PlateUi(stage, art, Tap);
            switcher.SceneChanging += OnSceneChanging;
            MultiAvatars.Loaded += OnChanged;
            TakeOutcome();
            BuildBackdrop();
            Rebuild();
        }

        void Start()
        {
            if (bgm != null && art.bgm != null)
            {
                bgm.SetAudioGroup(AudioGroup.Bgm);
                bgm.clip = art.bgm; bgm.loop = true;
                bgm.PlayAudio();
            }
            if (sfx != null) { sfx.SetAudioGroup(AudioGroup.Effects); sfx.PrepareAudioEffects(art.don, art.ka); }
            Status = L10n.Get("Play", "ghostSelect.connecting");
            deadline = Time.realtimeSinceStartup + connectSeconds;
            AddServers();
            dirty = true;
        }

        void OnDestroy()
        {
            cancel.Cancel(); cancel.Dispose();
            MultiAvatars.Loaded -= OnChanged;
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            cancel.Cancel();
            if (bgm != null) bgm.StopAudio();
        }

        void OnChanged() => dirty = true;

        // Back from the result boards of a battle: the two results, for the Result view.
        void TakeOutcome()
        {
            var ghost = switcher.Ghost;
            if (switcher.FromScene != SceneSwitcher.ResultScene || ghost == null || switcher.LastResult == null || switcher.LastResult2P == null) { resume = null; return; }
            bool below = ghost.PlayerLane == 1;
            var info = PlayerInfoController.EnsureInstance();
            LastOutcome = new Outcome
            {
                Player = below ? switcher.LastResult2P : switcher.LastResult, Ghost = below ? switcher.LastResult : switcher.LastResult2P,
                Title = switcher.LastResult.Title,
                PlayerName = (switcher.Lead == Side.P1 ? info.Info : info.SecondInfo).name,
                GhostName = string.IsNullOrEmpty(ghost.Nickname) ? L10n.Get("Play", "ghost.defaultName") : ghost.Nickname,
            };
            Shown = View.Result;
        }

        void Update()
        {
            if (Time.realtimeSinceStartup >= nextServerCheck) AddServers();
            // A page that came in, or a status that changed, shows at once.
            string status = StatusNow();
            if (status != Status) { Status = status; dirty = true; }
            if (dirty) Rebuild();
            if (switcher.IsInputBlocked || HasLeft || IsStarting) return;
            if (InputManager.GetKeyDown(InputKey.Back)) Back();
            else if (InputManager.PlayerKeyDown(InputKey.LeftDon) || InputManager.PlayerKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Confirm();
            else if (InputManager.PlayerKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuLeft) || InputManager.GetKeyDown(InputKey.MenuUp)) Move(-1);
            else if (InputManager.PlayerKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuRight) || InputManager.GetKeyDown(InputKey.MenuDown)) Move(1);
        }

        // ---------------------------------------------------------------- input

        public void Move(int delta)
        {
            if (HasLeft || IsStarting || !ui.Move(delta)) return;
            failed = false;
            Play(art.ka);
        }

        public void Confirm()
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked) return;
            var control = Focused;
            if (control == null) return;
            failed = false;
            Play(art.don);
            control.Press?.Invoke();
        }

        // A tap or click: the control is focused and pressed.
        public void Tap(string key)
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked || !ui.Focus(key)) return;
            Confirm();
        }

        public void Press(string key) => Tap(key);

        // One view up; from the servers, back to Entry.
        public void Back()
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked) return;
            failed = false;
            Play(art.ka);
            switch (Shown)
            {
                case View.Result: CloseResult(); return;
                case View.Plays: Shown = View.Categories; dirty = true; return;
                case View.Categories: Shown = View.Servers; dirty = true; return;
                default: Leave(); return;
            }
        }

        public void Leave()
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked) return;
            HasLeft = true;
            resume = null;
            switcher.SwitchScene(SceneSwitcher.MenuScene);
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }

        // ---------------------------------------------------------------- what the controls do

        public void OpenServer(Server server)
        {
            CurrentServer = server;
            CurrentCategory = null;
            Shown = View.Categories;
            dirty = true;
        }

        // A category's plays; its first page is asked for the first time, and again when it failed.
        public void OpenCategory(Category category)
        {
            CurrentCategory = category;
            Shown = View.Plays;
            Open(category);
            dirty = true;
        }

        public void TurnPage(int delta)
        {
            var category = CurrentCategory;
            if (category == null) return;
            int page = Math.Max(0, Math.Min(category.Pages - 1, category.Page + delta));
            // Past the last page, a push further asks again for a page that failed.
            if (page == category.Page) { if (delta > 0) LoadNearEnd(category); return; }
            category.Page = page;
            LoadNearEnd(category);
            dirty = true;
        }

        // The result is closed: back to the page the play was picked on, once its server is here.
        public void CloseResult()
        {
            LastOutcome = null;
            Shown = View.Servers;
            Resume();
            dirty = true;
        }

        void Resume()
        {
            // Once: a category that fails to load is the player's to open again.
            if (!resume.HasValue || resumed || Shown == View.Result) return;
            var server = servers.FirstOrDefault(s => s.Id == resume.Value.Server);
            if (server == null) return;
            resumed = true;
            var category = server.Categories.FirstOrDefault(c => c.Id == resume.Value.Category) ?? server.Categories[0];
            CurrentServer = server;
            OpenCategory(category);
        }

        // The page the resumed play is on, once its rows have come.
        void ResumePage(Category category)
        {
            if (!resume.HasValue || category != CurrentCategory) return;
            int index = category.Rows.FindIndex(r => r.ScoreId == resume.Value.ScoreId);
            category.Page = index >= 0 ? index / PageRows : Math.Min(resume.Value.Page, category.Pages - 1);
            if (index >= 0) ui.Focus("play:" + resume.Value.ScoreId);
            resume = null;
            dirty = true;
        }

        public async void Pick(FanmadeReplay row)
        {
            if (row == null || HasLeft || IsStarting || switcher.IsInputBlocked) return;
            IsStarting = true;
            startingName = MultiText.Name(row.Nickname);
            dirty = true;
            try
            {
                var match = await PrepareAsync(row, cancel.Token);
                if (cancel.IsCancellationRequested) return;
                var category = CurrentCategory;
                resume = (row.Server, category?.Id, category?.Page ?? 0, row.ScoreId);
                if (!switcher.PlayGhost(match.Song, match.Ghost)) { resume = null; Fail(L10n.Get("Play", "ghostSelect.startFailed")); return; }
                HasLeft = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Fail(Describe(error));
                Log.Warn("Ghost", "Could not start a ghost battle: " + error.Message, this);
            }
        }

        void Fail(string message)
        {
            IsStarting = false;
            failed = true;
            pickMessage = message;
            dirty = true;
        }

        string pickMessage = "", startingName = "";

        // The listed play's song (registered as an online song) and its recording.
        public static async Task<(SongDefinition Song, GhostMatch Ghost)> PrepareAsync(FanmadeReplay row, CancellationToken cancel)
        {
            var online = OnlineManager.EnsureInstance();
            var client = online.Client ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            var chart = await client.ReplayChartAsync(row, cancel) ?? throw new FanmadeException("CHART_NOT_FOUND");
            if (!chart.Blocks.Any(b => b.Course == row.Course && b.Side == row.Side)) throw new FanmadeException("REPLAY_OUTDATED");
            var record = await client.ReplayRecordAsync(row, cancel);
            var song = online.SearchSong(chart) ?? throw new FanmadeException("CHART_NOT_FOUND");
            return (song, new GhostMatch
            {
                Record = record, Course = row.Course, Side = row.Side, Nickname = row.Nickname,
                ScoreId = row.ScoreId, Server = row.Server, Score = row.Score,
            });
        }

        static string Describe(Exception error) => error.Message switch
        {
            "REPLAY_OUTDATED" => L10n.Get("Play", "ghostSelect.outdated"),
            "REPLAY_NOT_FOUND" or "CHART_NOT_FOUND" => L10n.Get("Play", "ghostSelect.unavailable"),
            "REPLAY_UNREADABLE" => L10n.Get("Play", "ghostSelect.unreadable"),
            "SERVER_NOT_CONNECTED" => L10n.Get("Play", "ghostSelect.serverOffline"),
            _ => L10n.Format("Play", "ghostSelect.loadFailed", error.Message),
        };

        // ---------------------------------------------------------------- servers and pages

        // Each connected server joins the screen with すべて and its catalog categories, in catalog
        // order: after the ones already there, whenever it connects, so no board moves under the
        // player. One that disconnects stays; its categories then say so. すべて's first page is
        // asked for at once: it tells how many plays the server lists.
        void AddServers()
        {
            nextServerCheck = Time.realtimeSinceStartup + 0.25f;
            var client = OnlineManager.EnsureInstance().Client;
            if (client != null)
                foreach (var endpoint in client.Endpoints)
                {
                    if (!endpoint.IsConnected || servers.Any(s => s.Id == endpoint.Id)) continue;
                    var server = new Server { Id = endpoint.Id, Name = endpoint.Config.DisplayName };
                    server.Categories.Add(new Category { Title = L10n.Get("Play", "ghostSelect.all"), Server = endpoint.Id });
                    foreach (var category in client.Categories.Where(c => c.Server == endpoint.Id))
                        server.Categories.Add(new Category { Id = category.Id, Title = category.Title, Server = endpoint.Id });
                    servers.Add(server);
                    Open(server.Categories[0]);
                    dirty = true;
                }
            if (servers.Count > 0 && !connected) { connected = true; dirty = true; }
            Resume();
        }

        // The category's first page comes in the first time it is shown, and is asked for again when
        // it failed and the player comes back to the category.
        void Open(Category category)
        {
            if (category == null || category.Started) return;
            category.Started = true;
            category.NextPage = 1;
            category.Loading = LoadMoreAsync(category);
        }

        // Near the end of what has come, the server's next page comes in (again, if it failed before).
        void LoadNearEnd(Category category)
        {
            if (category != null && category.Started && !category.IsLoading && category.NextPage.HasValue
                && (category.Page + 2) * PageRows >= category.Rows.Count)
                category.Loading = LoadMoreAsync(category);
        }

        async Task LoadMoreAsync(Category category)
        {
            var client = OnlineManager.EnsureInstance().Client;
            if (client == null || !category.NextPage.HasValue) return;
            int page = category.NextPage.Value;
            FanmadeReplayPage[] replies;
            try { replies = await client.ReplaysAsync(page, "recent", cancel.Token, category.Server, category.Id); }
            catch (OperationCanceledException) { return; }
            if (cancel.IsCancellationRequested) return;
            var reply = replies.FirstOrDefault();
            category.Error = reply == null ? "offline" : reply.Error;
            dirty = true;
            if (category.Error != null)
            {
                // The page stays the next one, for the player's next move to ask again; a first page
                // is asked for again when the category is next opened.
                if (page == 1) category.Started = false;
                return;
            }
            category.NextPage = reply.HasMore ? page + 1 : (int?)null;
            if (page == 1) category.Total = reply.Total;
            foreach (var item in reply.Items)
                if (!category.Rows.Any(r => r.ScoreId == item.ScoreId)) category.Rows.Add(item);
            ResumePage(category);
        }

        // The line under the list.
        string StatusNow()
        {
            if (IsStarting) return L10n.Format("Play", "ghostSelect.loadingPlay", startingName);
            if (failed) return pickMessage;
            if (!connected) return Time.realtimeSinceStartup > deadline ? L10n.Get("Play", "ghostSelect.noServer") : L10n.Get("Play", "ghostSelect.connecting");
            return Shown == View.Plays ? CategoryStatus(CurrentCategory) : "";
        }

        static string CategoryStatus(Category category)
        {
            if (category == null) return "";
            if (category.IsLoading && category.Rows.Count == 0) return L10n.Get("Common", "loading");
            // "offline" is this list's own; other errors are the server's or the connection's.
            if (category.Error != null) return category.Error == "offline" ? L10n.Get("Play", "ghostSelect.offline") : category.Error;
            if (category.Rows.Count == 0) return L10n.Get("Play", category.Id == null ? "ghostSelect.noPlaysServer" : "ghostSelect.noPlaysCategory");
            return "";
        }

        // How many plays a category lists, once it is known.
        static string Count(Category category) => category.Started && !category.IsLoading && category.Error == null ? L10n.Format("Play", "ghostSelect.count", category.Total) : "";

        public static string CourseName(string course) => MultiText.Course(course);
    }
}
