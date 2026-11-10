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
    // ゴーストバトル's list (issue #53): every recorded play the built-in servers keep (GET
    // /api/v1/replays), newest first, one category at a time. The category bar above the rows holds
    // すべて, then the servers' catalog categories merged by id: a category both servers list (pop,
    // game, …) is one entry that asks both, one only a server lists asks that server. Each category
    // keeps its own rows, paging and place. ← → switch category; on the drum, left ka from the first
    // row reaches the bar, where ka switches category and don returns to the rows. Ka (↑ ↓) moves,
    // don picks, Back returns to Entry; a tap selects a row and a tap on the selected one picks it,
    // and the bar's arrows switch category. Picking downloads the recording and plays it against
    // the player (SceneSwitcher.PlayGhost) on its own song and block, which the song loading scene
    // downloads like any online song. Plays of replaced files are not listed, and one whose song has
    // changed since the list came is refused with a message.
    public sealed class GhostSelectScene : MonoBehaviour
    {
        public GhostSelectView view;
        public AudioSource sfx;
        public AudioClip don, ka;
        // How long the list waits for a server to connect before saying there is none.
        public float connectSeconds = 10;

        // One entry of the category bar and the plays listed under it.
        public sealed class Category
        {
            // A catalog category's id; null for すべて.
            public string Id, Title = "";
            // The endpoints asked (their Ids).
            public readonly List<string> Servers = new List<string>();
            public readonly List<FanmadeReplay> Rows = new List<FanmadeReplay>();
            // The servers' totals so far.
            public int Total { get; internal set; }
            // The next page each server lists, or none once it has no more (or failed).
            internal readonly Dictionary<string, int> NextPage = new Dictionary<string, int>();
            internal readonly List<string> Errors = new List<string>();
            internal Task Loading;
            internal bool Started;
            internal int Selected;
            public bool IsLoading => Loading != null && !Loading.IsCompleted;
        }

        public IReadOnlyList<Category> Categories => categories;
        public int CategoryIndex { get; private set; }
        public Category Current => CategoryIndex < categories.Count ? categories[CategoryIndex] : null;
        public IReadOnlyList<FanmadeReplay> Rows => Current?.Rows ?? (IReadOnlyList<FanmadeReplay>)Array.Empty<FanmadeReplay>();
        // -1 while the category bar has the focus.
        public int Selected { get; private set; }
        public bool OnCategoryBar => Selected < 0;
        public FanmadeReplay SelectedRow => Selected >= 0 && Selected < Rows.Count ? Rows[Selected] : null;
        public string Status { get; private set; } = "";
        public bool IsLoading => !connected || (Current?.IsLoading ?? false);
        public bool IsStarting { get; private set; }
        public bool HasLeft { get; private set; }

        readonly List<Category> categories = new List<Category>();
        SceneSwitcher switcher;
        CancellationTokenSource cancel;
        bool connected;
        // A pick failed: its message stays until the player moves.
        bool failed;
        int first;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            cancel = new CancellationTokenSource();
            for (int i = 0; i < view.rows.Length; i++)
            {
                int index = i;
                if (view.rows[i].relay != null) view.rows[i].relay.Clicked = () => Tap(index);
            }
            if (view.categoryPrevious != null) view.categoryPrevious.Clicked = () => SwitchCategory(-1);
            if (view.categoryNext != null) view.categoryNext.Clicked = () => SwitchCategory(1);
            if (sfx != null) { sfx.SetAudioGroup(AudioGroup.Effects); sfx.PrepareAudioEffects(don, ka); }
            switcher.SceneChanging += OnSceneChanging;
            Show();
        }

        void Start() => _ = LoadFirstAsync();

        void OnDestroy()
        {
            cancel.Cancel(); cancel.Dispose();
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene) => cancel.Cancel();

        void Update()
        {
            if (switcher.IsInputBlocked || HasLeft || IsStarting) { Show(); return; }
            bool don = InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm);
            bool leftKa = InputManager.GetKeyDown(InputKey.LeftKa), rightKa = InputManager.GetKeyDown(InputKey.RightKa);
            if (InputManager.GetKeyDown(InputKey.Back)) Leave();
            else if (InputManager.GetKeyDown(InputKey.MenuLeft) || OnCategoryBar && leftKa) SwitchCategory(-1);
            else if (InputManager.GetKeyDown(InputKey.MenuRight) || OnCategoryBar && rightKa) SwitchCategory(1);
            else if (OnCategoryBar && (don || InputManager.GetKeyDown(InputKey.MenuDown))) Move(1);
            else if (don) Pick();
            else if (leftKa || InputManager.GetKeyDown(InputKey.MenuUp)) Move(-1);
            else if (rightKa || InputManager.GetKeyDown(InputKey.MenuDown)) Move(1);
            Show();
        }

        // Up from the first row is the category bar; down from the bar is the first row.
        public void Move(int delta)
        {
            if (HasLeft || IsStarting) return;
            failed = false;
            int count = Rows.Count;
            int next = Math.Max(-1, Math.Min(count - 1, Selected + delta));
            if (next != Selected) { Selected = next; Play(ka); }
            // Near the end the next pages come in.
            var category = Current;
            if (category != null && count > 0 && Selected >= count - 2 && !category.IsLoading && category.NextPage.Count > 0) category.Loading = LoadMoreAsync(category);
        }

        // The category left (-1) or right (+1) of the current one, with its own place in its rows.
        public void SwitchCategory(int delta)
        {
            if (categories.Count == 0 || HasLeft || IsStarting || switcher.IsInputBlocked) return;
            int next = Math.Max(0, Math.Min(categories.Count - 1, CategoryIndex + delta));
            failed = false;
            Play(ka);
            if (next == CategoryIndex) return;
            if (!OnCategoryBar) Current.Selected = Selected;
            CategoryIndex = next;
            if (!OnCategoryBar) Selected = Math.Min(Current.Selected, Math.Max(0, Current.Rows.Count - 1));
            Open(Current);
        }

        public void Tap(int row)
        {
            int index = first + row;
            if (index < 0 || index >= Rows.Count || HasLeft || IsStarting || switcher.IsInputBlocked) return;
            if (index == Selected) Pick();
            else Move(index - Selected);
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

        async Task LoadFirstAsync()
        {
            var online = OnlineManager.EnsureInstance();
            Status = "Connecting…";
            float deadline = Time.realtimeSinceStartup + connectSeconds;
            while (!(online.Client?.Endpoints.Any(e => e.IsConnected) ?? false))
            {
                if (Time.realtimeSinceStartup > deadline || cancel.IsCancellationRequested) { Status = "No server is online, so there are no ghosts to battle."; return; }
                await Awaitable.NextFrameAsync(cancel.Token);
            }
            BuildCategories(online.Client);
            connected = true;
            Open(Current);
            if (Current != null) await Current.Loading;
        }

        // すべて, then the connected servers' catalog categories merged by id, in catalog order.
        void BuildCategories(FanmadeClient client)
        {
            var servers = client.Endpoints.Where(e => e.IsConnected).Select(e => e.Id).ToList();
            var all = new Category { Title = "すべて" };
            all.Servers.AddRange(servers);
            categories.Add(all);
            foreach (var category in client.Categories.Where(c => servers.Contains(c.Server)))
            {
                var entry = categories.Find(c => c.Id == category.Id);
                if (entry == null) categories.Add(entry = new Category { Id = category.Id, Title = category.Title });
                if (!entry.Servers.Contains(category.Server)) entry.Servers.Add(category.Server);
            }
        }

        // The category's first pages come in the first time it is shown.
        void Open(Category category)
        {
            if (category == null || category.Started) return;
            category.Started = true;
            foreach (var server in category.Servers) category.NextPage[server] = 1;
            category.Loading = LoadMoreAsync(category);
        }

        async Task LoadMoreAsync(Category category)
        {
            var client = OnlineManager.EnsureInstance().Client;
            if (client == null || category.NextPage.Count == 0) return;
            var wanted = category.NextPage.ToList();
            FanmadeReplayPage[][] replies;
            try { replies = await Task.WhenAll(wanted.Select(p => client.ReplaysAsync(p.Value, "recent", cancel.Token, p.Key, category.Id))); }
            catch (OperationCanceledException) { return; }
            if (cancel.IsCancellationRequested) return;
            bool current = category == Current;
            var selected = current ? SelectedRow : null;
            int total = 0;
            foreach (var page in replies.SelectMany(x => x))
            {
                if (page.Error != null) category.Errors.Add(page.Endpoint.Config.DisplayName + ": " + page.Error);
                if (page.HasMore) category.NextPage[page.Endpoint.Id] = page.Page + 1;
                else category.NextPage.Remove(page.Endpoint.Id);
                total += page.Total;
                foreach (var item in page.Items)
                    if (!category.Rows.Any(r => r.Server == item.Server && r.ScoreId == item.ScoreId)) category.Rows.Add(item);
            }
            if (wanted.All(p => p.Value == 1)) category.Total = total;
            category.Rows.Sort((a, b) => b.SubmittedAt.CompareTo(a.SubmittedAt));
            if (selected != null) Selected = Math.Max(0, category.Rows.IndexOf(selected));
        }

        string CategoryStatus(Category category)
        {
            if (category == null) return Status;
            if (category.IsLoading && category.Rows.Count == 0) return "Loading…";
            if (category.Rows.Count == 0)
                return category.Errors.Count > 0 ? string.Join(" · ", category.Errors) : category.Id == null ? "No one has recorded a play yet." : "No recorded plays in this category yet.";
            return string.Join(" · ", category.Errors);
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
               + "   " + (string.IsNullOrEmpty(row.Nickname) ? "?" : row.Nickname) + "   " + row.ServerName;

        void Show()
        {
            if (view == null) return;
            var rows = Rows;
            int count = view.rows.Length;
            // The selected row stays a third of the way down while the list scrolls.
            first = Math.Max(0, Math.Min(Selected - count / 3, rows.Count - count));
            for (int i = 0; i < count; i++)
            {
                var v = view.rows[i];
                int index = first + i;
                bool shown = index < rows.Count;
                v.root.gameObject.SetActive(shown);
                if (!shown) continue;
                var row = rows[index];
                v.background.color = index == Selected ? view.selectedColor : view.rowColor;
                v.title.text = row.Title;
                v.detail.text = Detail(row);
                v.score.text = row.Score.ToString("N0", CultureInfo.InvariantCulture);
            }
            var category = Current;
            if (view.category != null)
                view.category.text = category == null ? "" : category.Title + (category.Started && !category.IsLoading ? "（" + category.Total + "）" : "")
                    + "　" + (CategoryIndex + 1) + "/" + categories.Count;
            if (view.categoryBar != null) view.categoryBar.color = OnCategoryBar ? view.selectedColor : view.rowColor;
            if (view.status != null) view.status.text = IsStarting || failed || !connected ? Status : CategoryStatus(category);
        }
    }
}
