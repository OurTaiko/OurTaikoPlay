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
    // /api/v1/replays), newest first. Ka moves, don picks, Back returns to Entry; a tap selects a row
    // and a tap on the selected one picks it. Picking downloads the recording and plays it against
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

        public IReadOnlyList<FanmadeReplay> Rows => rows;
        public int Selected { get; private set; }
        public FanmadeReplay SelectedRow => Selected >= 0 && Selected < rows.Count ? rows[Selected] : null;
        public string Status { get; private set; } = "";
        public bool IsLoading => loading != null && !loading.IsCompleted;
        public bool IsStarting { get; private set; }
        public bool HasLeft { get; private set; }

        readonly List<FanmadeReplay> rows = new List<FanmadeReplay>();
        // The next page each server lists, or none once it has no more (or failed).
        readonly Dictionary<string, int> nextPage = new Dictionary<string, int>();
        readonly List<string> errors = new List<string>();
        SceneSwitcher switcher;
        Task loading;
        CancellationTokenSource cancel;
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
            if (sfx != null) { sfx.SetAudioGroup(AudioGroup.Effects); sfx.PrepareAudioEffects(don, ka); }
            switcher.SceneChanging += OnSceneChanging;
            Show();
        }

        void Start() => loading = LoadFirstAsync();

        void OnDestroy()
        {
            cancel.Cancel(); cancel.Dispose();
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene) => cancel.Cancel();

        void Update()
        {
            if (switcher.IsInputBlocked || HasLeft || IsStarting) { Show(); return; }
            if (InputManager.GetKeyDown(InputKey.Back)) Leave();
            else if (InputManager.GetKeyDown(InputKey.LeftDon) || InputManager.GetKeyDown(InputKey.RightDon) || InputManager.GetKeyDown(InputKey.Confirm)) Pick();
            else if (InputManager.GetKeyDown(InputKey.LeftKa) || InputManager.GetKeyDown(InputKey.MenuUp) || InputManager.GetKeyDown(InputKey.MenuLeft)) Move(-1);
            else if (InputManager.GetKeyDown(InputKey.RightKa) || InputManager.GetKeyDown(InputKey.MenuDown) || InputManager.GetKeyDown(InputKey.MenuRight)) Move(1);
            Show();
        }

        public void Move(int delta)
        {
            if (rows.Count == 0 || HasLeft || IsStarting) return;
            int next = Math.Max(0, Math.Min(rows.Count - 1, Selected + delta));
            if (next != Selected) { Selected = next; Play(ka); }
            // Near the end the next pages come in.
            if (Selected >= rows.Count - 2 && !IsLoading && nextPage.Count > 0) loading = LoadMoreAsync();
        }

        public void Tap(int row)
        {
            int index = first + row;
            if (index < 0 || index >= rows.Count || HasLeft || IsStarting || switcher.IsInputBlocked) return;
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
                if (!switcher.PlayGhost(match.Song, match.Ghost)) { IsStarting = false; Status = "Could not start the battle."; return; }
                HasLeft = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                IsStarting = false;
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
            foreach (var e in online.Client.Endpoints.Where(e => e.IsConnected)) nextPage[e.Id] = 1;
            await LoadMoreAsync();
        }

        async Task LoadMoreAsync()
        {
            var client = OnlineManager.EnsureInstance().Client;
            if (client == null || nextPage.Count == 0) return;
            Status = "Loading…";
            var wanted = nextPage.ToList();
            FanmadeReplayPage[][] replies;
            try { replies = await Task.WhenAll(wanted.Select(p => client.ReplaysAsync(p.Value, "recent", cancel.Token, p.Key))); }
            catch (OperationCanceledException) { return; }
            var pages = replies.SelectMany(x => x);
            if (cancel.IsCancellationRequested) return;
            var selected = SelectedRow;
            foreach (var page in pages)
            {
                if (page.Error != null) errors.Add(page.Endpoint.Config.DisplayName + ": " + page.Error);
                if (page.HasMore) nextPage[page.Endpoint.Id] = page.Page + 1;
                else nextPage.Remove(page.Endpoint.Id);
                foreach (var item in page.Items)
                    if (!rows.Any(r => r.Server == item.Server && r.ScoreId == item.ScoreId)) rows.Add(item);
            }
            rows.Sort((a, b) => b.SubmittedAt.CompareTo(a.SubmittedAt));
            if (selected != null) Selected = Math.Max(0, rows.IndexOf(selected));
            Status = rows.Count == 0 ? (errors.Count > 0 ? string.Join(" · ", errors) : "No one has recorded a play yet.")
                : string.Join(" · ", errors);
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
            if (view.status != null) view.status.text = Status;
        }
    }
}
