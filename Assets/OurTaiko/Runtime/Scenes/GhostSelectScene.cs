using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko
{
    // ゴーストバトル's list (issue #53) as a shelf of three columns: the connected built-in servers,
    // the highlighted server's categories (すべて, then its catalog categories), and the highlighted
    // category's recorded plays (GET /api/v1/replays?categoryId=), newest first. Every column opens
    // with もどる and has another after every ten entries. The highlighted entry of a column shows
    // what the next column holds; each category loads its first page the first time it is shown and
    // the next ones near its end, and every column keeps its place. Ka (↑ ↓) moves in the focused
    // column; don (→) enters a server or category or picks a play; もどる, Back (←) return to the
    // column on the left, and from the servers to Entry. A tap selects an entry, a tap on the
    // selected one opens it. Picking downloads the recording and plays it against the player
    // (SceneSwitcher.PlayGhost) on its own song and block, which the song loading scene downloads
    // like any online song. Plays of replaced files are not listed, and one whose song has changed
    // since the list came is refused with a message.
    public sealed class GhostSelectScene : MonoBehaviour
    {
        public GhostSelectView view;
        public AudioSource sfx;
        public AudioClip don, ka;
        // How long the list waits for a server to connect before saying there is none.
        public float connectSeconds = 10;
        public const int BackEvery = 10;
        public const string BackLabel = "もどる";

        public enum Kind { Back, Server, Category, Replay }

        // One shelf entry: もどる or what it names.
        public readonly struct Entry
        {
            public readonly Kind Kind;
            public readonly Server Server;
            public readonly Category Category;
            public readonly FanmadeReplay Replay;
            public Entry(Kind kind, Server server = null, Category category = null, FanmadeReplay replay = null)
            { Kind = kind; Server = server; Category = category; Replay = replay; }
        }

        public sealed class Server
        {
            public string Id = "", Name = "";
            public readonly List<Category> Categories = new List<Category>();
            internal int Selected = 1;
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
            internal int? NextPage;
            internal string Error;
            internal Task Loading;
            internal int Selected = 1;
        }

        public IReadOnlyList<Server> Servers => servers;
        // The focused column: 0 servers, 1 categories, 2 plays.
        public int Column { get; private set; }
        // The column's entries: もどる first and after every ten.
        public IReadOnlyList<Entry> Entries(int column) => Enumerable.Range(0, EntryCount(column)).Select(i => At(column, i)).ToList();
        // How many entries the column has. Entries are counted and found by arithmetic (At), so
        // drawing a long column every frame builds no list.
        public int EntryCount(int column)
        {
            int items = column == 0 ? servers.Count : column == 1 ? CurrentServer?.Categories.Count ?? -1 : CurrentCategory?.Rows.Count ?? -1;
            return items < 0 ? 0 : items == 0 ? 1 : items + (items + BackEvery - 1) / BackEvery;
        }
        public int SelectedIn(int column) => column == 0 ? serverSelected : column == 1 ? CurrentServer?.Selected ?? 0 : CurrentCategory?.Selected ?? 0;
        public Entry Focused => At(Column, SelectedIn(Column));
        // The server and category the shelf shows, highlighted in their columns.
        public Server CurrentServer => At(0, serverSelected).Server;
        public Category CurrentCategory => CurrentServer is Server server ? At(1, server.Selected).Category : null;
        public IReadOnlyList<FanmadeReplay> Rows => CurrentCategory?.Rows ?? (IReadOnlyList<FanmadeReplay>)Array.Empty<FanmadeReplay>();
        public FanmadeReplay SelectedRow => Column == 2 ? Focused.Replay : null;
        public string Status { get; private set; } = "";
        public bool IsLoading => !connected || (CurrentCategory?.IsLoading ?? false);
        public bool IsStarting { get; private set; }
        public bool HasLeft { get; private set; }

        readonly List<Server> servers = new List<Server>();
        int serverSelected = 1;
        SceneSwitcher switcher;
        CancellationTokenSource cancel;
        bool connected;
        // A pick failed: its message stays until the player moves.
        bool failed;
        readonly int[] first = new int[3];
        float deadline, nextServerCheck;

        // The column's entry: もどる leads every group of BackEvery + 1 entries, and the group's others
        // are the column's items in order. もどる too for an index outside the column.
        Entry At(int column, int index)
        {
            if (index < 0 || index >= EntryCount(column) || index % (BackEvery + 1) == 0) return new Entry(Kind.Back);
            int item = index - index / (BackEvery + 1) - 1;
            return column == 0 ? new Entry(Kind.Server, server: servers[item])
                : column == 1 ? new Entry(Kind.Category, category: CurrentServer.Categories[item])
                : new Entry(Kind.Replay, replay: CurrentCategory.Rows[item]);
        }

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            cancel = new CancellationTokenSource();
            for (int c = 0; c < view.columns.Length; c++)
                for (int i = 0; i < view.columns[c].rows.Length; i++)
                {
                    int column = c, row = i;
                    if (view.columns[c].rows[i].relay != null) view.columns[c].rows[i].relay.Clicked = () => Tap(column, row);
                }
            if (sfx != null) { sfx.SetAudioGroup(AudioGroup.Effects); sfx.PrepareAudioEffects(don, ka); }
            switcher.SceneChanging += OnSceneChanging;
            Show();
        }

        void Start()
        {
            Status = "Connecting…";
            deadline = Time.realtimeSinceStartup + connectSeconds;
            AddServers();
        }

        void OnDestroy()
        {
            cancel.Cancel(); cancel.Dispose();
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene) => cancel.Cancel();

        void Update()
        {
            if (Time.realtimeSinceStartup >= nextServerCheck) AddServers();
            if (switcher.IsInputBlocked || HasLeft || IsStarting) { Show(); return; }
            if (InputManager.GetKeyDown(InputKey.Back) || InputManager.GetKeyDown(InputKey.MenuLeft)) Back(leave: InputManager.GetKeyDown(InputKey.Back));
            else if (InputManager.PlayerKeyDown(InputKey.LeftDon) || InputManager.PlayerKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Confirm();
            else if (InputManager.GetKeyDown(InputKey.MenuRight)) { if (Focused.Kind != Kind.Back && Focused.Kind != Kind.Replay) Confirm(); }
            else if (InputManager.PlayerKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuUp)) Move(-1);
            else if (InputManager.PlayerKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuDown)) Move(1);
            Show();
        }

        // Moves in the focused column; the columns to its right follow the new highlight.
        public void Move(int delta)
        {
            if (HasLeft || IsStarting) return;
            failed = false;
            int count = EntryCount(Column);
            int next = Math.Max(0, Math.Min(count - 1, SelectedIn(Column) + delta));
            // At the end of the plays, a push further asks again for a page that failed.
            if (next == SelectedIn(Column)) { if (Column == 2 && delta > 0) LoadNearEnd(CurrentCategory, next); return; }
            Select(Column, next);
            Play(ka);
        }

        void Select(int column, int index)
        {
            if (column == 0) serverSelected = index;
            else if (column == 1 && CurrentServer != null) CurrentServer.Selected = index;
            else if (column == 2 && CurrentCategory != null) CurrentCategory.Selected = index;
            Open(CurrentCategory);
            if (column == 2) LoadNearEnd(CurrentCategory, index);
        }

        // Near the end of a category the next page comes in (again, if it failed before).
        void LoadNearEnd(Category category, int index)
        {
            if (category != null && category.Started && index >= EntryCount(2) - 3 && !category.IsLoading && category.NextPage.HasValue)
                category.Loading = LoadMoreAsync(category);
        }

        // Don on the focused entry: もどる returns, a server or category is entered, a play is raced.
        public void Confirm()
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked || !connected) return;
            failed = false;
            var entry = Focused;
            switch (entry.Kind)
            {
                case Kind.Back: Back(leave: true); return;
                case Kind.Replay: Pick(); return;
                default:
                    Play(don);
                    Column++;
                    // Into a column, on its first entry after もどる.
                    if (SelectedIn(Column) == 0 && EntryCount(Column) > 1) Select(Column, 1);
                    // A category whose first page failed is asked for again.
                    Open(CurrentCategory);
                    return;
            }
        }

        // One column to the left; from the servers, back to Entry when `leave`.
        public void Back(bool leave = true)
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked) return;
            failed = false;
            if (Column > 0) { Column--; Play(ka); return; }
            if (leave) Leave();
        }

        // A tap selects an entry (and focuses its column); a tap on the focused entry opens it.
        public void Tap(int column, int row)
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked || column > Column + 1) return;
            int index = first[column] + row;
            if (index < 0 || index >= EntryCount(column)) return;
            if (column == Column && index == SelectedIn(column)) { Confirm(); return; }
            failed = false;
            Column = column;
            Select(column, index);
            Play(ka);
        }

        public void Leave()
        {
            if (HasLeft || IsStarting || switcher.IsInputBlocked) return;
            HasLeft = true;
            switcher.SwitchScene(SceneSwitcher.MenuScene);
        }

        public async void Pick()
        {
            var row = SelectedRow;
            if (row == null || HasLeft || IsStarting || switcher.IsInputBlocked) return;
            Play(don);
            IsStarting = true;
            Status = "Loading " + row.Nickname + "'s play…";
            try
            {
                var match = await PrepareAsync(row, cancel.Token);
                if (cancel.IsCancellationRequested) return;
                if (!switcher.PlayGhost(match.Song, match.Ghost)) { IsStarting = false; failed = true; Status = "Could not start the battle."; return; }
                HasLeft = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                IsStarting = false;
                failed = true;
                Status = Describe(error);
                Log.Warn("Ghost", "Could not start a ghost battle: " + error.Message, this);
            }
        }

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
            "REPLAY_OUTDATED" => "This song was changed after that play; pick another.",
            "REPLAY_NOT_FOUND" or "CHART_NOT_FOUND" => "That play is no longer available.",
            "REPLAY_UNREADABLE" => "That recording cannot be read by this version.",
            "SERVER_NOT_CONNECTED" => "Its server is offline.",
            _ => "Could not load that play (" + error.Message + ").",
        };

        // Each connected server joins the shelf with すべて and its catalog categories, in catalog
        // order: at the end of the shelf, whenever it connects, so no entry moves under the player.
        // One that disconnects stays; its categories then say so.
        void AddServers()
        {
            nextServerCheck = Time.realtimeSinceStartup + 0.25f;
            var client = OnlineManager.EnsureInstance().Client;
            if (client != null)
                foreach (var endpoint in client.Endpoints)
                {
                    if (!endpoint.IsConnected || servers.Any(s => s.Id == endpoint.Id)) continue;
                    var server = new Server { Id = endpoint.Id, Name = endpoint.Config.DisplayName };
                    server.Categories.Add(new Category { Title = "すべて", Server = endpoint.Id });
                    foreach (var category in client.Categories.Where(c => c.Server == endpoint.Id))
                        server.Categories.Add(new Category { Id = category.Id, Title = category.Title, Server = endpoint.Id });
                    servers.Add(server);
                }
            if (connected) return;
            if (servers.Count == 0)
            {
                if (Time.realtimeSinceStartup > deadline) Status = "No server is online, so there are no ghosts to battle.";
                return;
            }
            connected = true;
            Status = "";
            Open(CurrentCategory);
        }

        // The category's first page comes in the first time it is shown, and is asked for again when
        // it failed and the player comes back to the category.
        void Open(Category category)
        {
            if (category == null || category.Started || !connected) return;
            category.Started = true;
            category.NextPage = 1;
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
            if (category.Error != null)
            {
                // The page stays the next one, for the player's next move to ask again; a first page
                // is asked for again when the category is next shown.
                if (page == 1) category.Started = false;
                return;
            }
            category.NextPage = reply.HasMore ? page + 1 : (int?)null;
            if (page == 1) category.Total = reply.Total;
            foreach (var item in reply.Items)
                if (!category.Rows.Any(r => r.ScoreId == item.ScoreId)) category.Rows.Add(item);
        }

        string CategoryStatus(Category category)
        {
            if (category == null) return "";
            if (category.IsLoading && category.Rows.Count == 0) return "Loading…";
            if (category.Error != null) return category.Error;
            if (category.Rows.Count == 0) return category.Id == null ? "No one has recorded a play on this server yet." : "No recorded plays in this category yet.";
            return "";
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayAudioOneShot(clip);
        }

        static string CourseName(string course) => course switch
        {
            "Easy" => "かんたん", "Normal" => "ふつう", "Hard" => "むずかしい", "Oni" => "おに", "Edit" => "おに（裏）", _ => course,
        };

        public static string Detail(FanmadeReplay row)
            => CourseName(row.Course) + " ★" + row.Level + (row.Side != "single" ? " " + row.Side.ToUpperInvariant() : "")
               + "   " + (string.IsNullOrEmpty(row.Nickname) ? "?" : row.Nickname);

        static string Count(Category category) => category.Started && !category.IsLoading && category.Error == null ? category.Total + " 件" : "";

        void Show()
        {
            if (view == null) return;
            for (int column = 0; column < view.columns.Length && column < 3; column++)
            {
                var shelf = view.columns[column];
                int entries = EntryCount(column), count = shelf.rows.Length, selected = SelectedIn(column);
                // The selected entry stays a third of the way down while the column scrolls.
                first[column] = Math.Max(0, Math.Min(selected - count / 3, entries - count));
                for (int i = 0; i < count; i++)
                {
                    var v = shelf.rows[i];
                    int index = first[column] + i;
                    bool shown = index < entries;
                    v.root.gameObject.SetActive(shown);
                    if (!shown) continue;
                    var entry = At(column, index);
                    v.background.color = index != selected ? (entry.Kind == Kind.Back ? view.backColor : view.rowColor)
                        : column == Column ? view.selectedColor : view.highlightColor;
                    switch (entry.Kind)
                    {
                        case Kind.Back: v.title.text = BackLabel; v.detail.text = ""; v.score.text = ""; break;
                        case Kind.Server:
                            v.title.text = entry.Server.Name;
                            v.detail.text = Count(entry.Server.Categories[0]); v.score.text = ""; break;
                        case Kind.Category:
                            v.title.text = entry.Category.Title; v.detail.text = Count(entry.Category); v.score.text = ""; break;
                        default:
                            v.title.text = entry.Replay.Title; v.detail.text = Detail(entry.Replay);
                            v.score.text = entry.Replay.Score.ToString("N0", CultureInfo.InvariantCulture); break;
                    }
                }
            }
            if (view.status != null) view.status.text = IsStarting || failed || !connected ? Status : CategoryStatus(CurrentCategory);
        }
    }
}
