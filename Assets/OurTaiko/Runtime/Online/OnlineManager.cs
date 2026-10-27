using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OurTaiko.Online
{
    // A server category as a song-select folder: its songs in API order (shared SongDefinitions).
    // Global holder of the online servers, created before the first scene and kept across loads
    // (like SettingManager). It reads servers.json, signs in to the built-in servers and loads their
    // song lists (at startup from the Presented scene, later from the settings), owns the FanmadeClient, pumps its score
    // uploads every frame and turns the connected catalog into SongDefinitions and category
    // folders for song select.
    public sealed class OnlineManager : MonoBehaviour
    {
        public static OnlineManager Instance { get; private set; }

        // The additional servers of servers.json; the built-in ones are not in it.
        public ServerList Servers { get; private set; } = new ServerList();
        // null for unsaved data (tests); otherwise where Servers is read from and written to.
        public string FilePath { get; private set; }
        public FanmadeClient Client { get; private set; }
        // The connected catalog as playable songs; the same instance for a chart until Reset.
        public IReadOnlyList<SongDefinition> Songs => songs;
        // One folder per server category, in server then catalog order.
        public IReadOnlyList<SongFolder> Folders => folders;

        readonly List<SongDefinition> songs = new List<SongDefinition>();
        readonly List<SongFolder> folders = new List<SongFolder>();
        readonly Dictionary<string, (SongDefinition Song, FanmadeChart Chart)> byKey = new Dictionary<string, (SongDefinition, FanmadeChart)>();
        readonly Dictionary<SongDefinition, FanmadeChart> charts = new Dictionary<SongDefinition, FanmadeChart>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        // Decoded playlist covers by content hash.
        readonly Dictionary<string, Sprite> covers = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();

        public static OnlineManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            // Also handles entering Play mode with scene/domain reload disabled.
            var existing = FindAnyObjectByType<OnlineManager>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(OnlineManager)).AddComponent<OnlineManager>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            UseCache(Path.Combine(GamePaths.Root, "cache", "fanmade"),
                Path.Combine(GamePaths.Root, "scores.sqlite3"));
            Load(Path.Combine(GamePaths.Root, ServerList.FileName));
            UseAccounts(new AccountStore(Path.Combine(GamePaths.Root, AccountStore.FileName), DeviceSecret()), new AccountCenter());
            UseProfiles(new ProfileCenter());
        }

        // What the account file is encrypted with: something of this device and this game.
        static string DeviceSecret() => SystemInfo.deviceUniqueIdentifier + "\n" + Application.identifier;

        void Update() => Client?.Update();

        void OnDestroy()
        {
            if (Instance != this) return;
            Center?.Dispose();
            Profiles?.Dispose();
            Client?.Dispose();
            ClearSongs();
            Instance = null;
        }

        (string Name, string Url)[] builtIn = ServerList.BuiltIn;

        // The built-in servers. Players sign in to them through their OurTaiko accounts.
        public IReadOnlyList<ServerConfig> BuiltInServers => builtIn.Select(b => new ServerConfig { name = b.Name, baseUrl = b.Url }).ToArray();

        // 1P's and 2P's OurTaiko accounts on this device, and the account center they sign in at.
        public AccountStore Accounts { get; private set; }
        public AccountCenter Center { get; private set; }
        // Where the signed-in seats' nameplates (title and 段位) come from.
        public ProfileCenter Profiles { get; private set; }
        // Seats whose sign-in the account center refused (expired, signed out elsewhere, password
        // changed) since Entry last said so.
        readonly bool[] expired = new bool[FanmadeEndpoint.SeatCount];

        // What the running sign-in or song list request is doing, for the screen that started it,
        // and how the last one of each ended.
        public string Activity { get; private set; } = "";
        public string LoginSummary { get; private set; } = "";
        public string SongListSummary { get; private set; } = "";
        public bool IsBusy { get; private set; }
        // The built-in servers' connections, kept while ServerLogin reconnects the others.
        readonly List<FanmadeEndpoint> builtInEndpoints = new List<FanmadeEndpoint>();

        // Startup: load every built-in song list, then sign the accounts in (which loads their
        // scores). Cancelling stops at the current request and keeps what has arrived.
        public async Task StartUpAsync(CancellationToken cancel = default)
        {
            // One rebuild of the song list for both steps, also when the startup is cancelled midway.
            await LoadSongListAsync(cancel, refresh: false);
            if (!cancel.IsCancellationRequested) await MigrateSettingsPasswordsAsync(cancel);
            if (!cancel.IsCancellationRequested) await LoginAsync(cancel, refresh: false);
            if (this != null) RefreshSongs();
        }

        // Tests use an account file and account center of their own.
        public void UseAccounts(AccountStore accounts, AccountCenter center)
        {
            Center?.Dispose();
            Accounts = accounts;
            Center = center;
            Accounts.Load();
            Array.Clear(expired, 0, expired.Length);
        }

        public void UseProfiles(ProfileCenter profiles)
        {
            Profiles?.Dispose();
            Profiles = profiles;
        }

        // True once after the account center refused this seat's sign-in; Entry tells the player.
        public bool TakeExpired(int seat)
        {
            bool was = expired[seat];
            expired[seat] = false;
            return was;
        }

        // Versions before game accounts kept the passwords in settings.json: they sign in once
        // and are erased from the file.
        async Task MigrateSettingsPasswordsAsync(CancellationToken cancel)
        {
            var manager = SettingManager.EnsureInstance();
            var settings = manager.Settings.Clone();
            var account = settings.accounts;
            string[] usernames = { account.p1Username, account.p2Username }, passwords = { account.p1Password, account.p2Password };
            if (string.IsNullOrEmpty(passwords[0]) && string.IsNullOrEmpty(passwords[1])) return;
            for (int seat = 0; seat < usernames.Length; seat++)
            {
                if (string.IsNullOrEmpty(usernames[seat]) || string.IsNullOrEmpty(passwords[seat]) || Accounts.Seats[seat].IsSignedIn) continue;
                try { await SignInAsync(seat, usernames[seat], passwords[seat], cancel, connect: false); }
                catch (Exception error) { Debug.LogWarning($"Signing {seat + 1}P in with the saved password failed: {error.Message}"); }
            }
            account.p1Password = ""; account.p2Password = "";
            manager.Set(settings);
        }

        // Signs a seat in with a username and password at the account center and remembers the
        // account (never the password); then signs it in to the built-in servers. Throws what the
        // account center answered (HTTP 401: wrong username or password, 429: too many attempts).
        public async Task SignInAsync(int seat, string username, string password, CancellationToken cancel = default, bool connect = true)
        {
            var account = await Center.LoginAsync(username, password, DeviceName(seat), cancel);
            Accounts.Seats[seat] = account;
            SaveAccounts();
            expired[seat] = false;
            ShowAccount(seat);
            if (connect && this != null) await LoginAsync(cancel);
        }

        // Signs a seat out on this device: the account center forgets the token and the built-in
        // servers forget the seat's session. Pending scores stay queued for when it signs in again.
        public async Task SignOutAsync(int seat, CancellationToken cancel = default)
        {
            string token = Accounts.Seats[seat].Token;
            Accounts.Seats[seat] = new OurTaikoAccount();
            SaveAccounts();
            foreach (var e in builtInEndpoints) Client.SignOut(e, seat);
            ShowAccount(seat);
            if (this != null) RefreshSongs();
            if (token.Length == 0) return;
            try { await Center.LogoutAsync(token, cancel); }
            catch (Exception error) { Debug.LogWarning("Account center sign-out failed: " + error.Message); }
        }

        // Checks each remembered account at the account center and signs it in to every built-in
        // server whose catalog names that account center; no song list is requested. A seat the
        // account center refuses is signed out and marked expired; one that cannot be reached
        // stays remembered and plays offline. True when some seat is signed in somewhere.
        public async Task<bool> LoginAsync(CancellationToken cancel = default, bool refresh = true)
        {
            if (IsBusy) return false;
            IsBusy = true;
            var results = new List<string>();
            bool any = false;
            try
            {
                var endpoints = BuiltInEndpoints();
                for (int seat = 0; seat < Accounts.Seats.Length; seat++)
                {
                    var account = Accounts.Seats[seat];
                    string who = (seat + 1) + "P";
                    if (!account.IsSignedIn)
                    {
                        foreach (var e in endpoints) Client.SignOut(e, seat);
                        if (seat == 0) results.Add(who + ": guest (not signed in)");
                        continue;
                    }
                    if (cancel.IsCancellationRequested) { results.Add(who + ": cancelled"); continue; }
                    Activity = $"Checking {who}'s OurTaiko account…";
                    try
                    {
                        await Center.RefreshAsync(account, cancel);
                        SaveAccounts();
                        ShowAccount(seat, await NameplateAsync(account, cancel));
                    }
                    catch (HttpStatusException error) when (error.Status == 401)
                    {
                        Expire(seat, endpoints);
                        results.Add(who + ": sign-in expired");
                        continue;
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning($"{who}'s account check failed: {error.Message}");
                        results.Add(who + ": " + (cancel.IsCancellationRequested ? "cancelled" : "offline (" + error.Message + ")"));
                        continue;
                    }
                    foreach (var e in endpoints)
                    {
                        string name = e.Config.DisplayName;
                        if (cancel.IsCancellationRequested) { results.Add(who + " " + name + ": cancelled"); continue; }
                        if (e.SsoService.Length == 0 || e.SsoIssuer != Center.Issuer)
                        {
                            Client.SignOut(e, seat);
                            results.Add(who + " " + name + ": " + (e.IsConnected ? "no OurTaiko account sign-in" : "song list not loaded"));
                            continue;
                        }
                        Activity = $"Signing {who} in to {name}…";
                        try
                        {
                            string service = e.SsoService, accountToken = account.Token;
                            var center = Center;
                            string session = await center.TradeAsync(accountToken, service, cancel);
                            await Client.UseAccountAsync(e, seat, session, account.UserId, account.Nickname,
                                c => center.TradeAsync(accountToken, service, c), cancel);
                            any = true;
                            results.Add(who + " " + name + ": signed in as " + account.Nickname);
                        }
                        catch (HttpStatusException error) when (error.Status == 401)
                        {
                            Expire(seat, endpoints);
                            results.Add(who + ": sign-in expired");
                            break;
                        }
                        catch (Exception error)
                        {
                            Debug.LogWarning($"{who}'s sign-in to {name} failed: {error.Message}");
                            results.Add(who + " " + name + ": " + (cancel.IsCancellationRequested ? "cancelled" : Describe(error)));
                        }
                    }
                }
                // The account's own playlists are folders too.
                if (this != null && refresh) RefreshSongs();
            }
            finally { Activity = ""; LoginSummary = string.Join("   •   ", results); IsBusy = false; }
            return any;
        }

        void Expire(int seat, IEnumerable<FanmadeEndpoint> endpoints)
        {
            Accounts.Seats[seat] = new OurTaikoAccount();
            SaveAccounts();
            expired[seat] = true;
            foreach (var e in endpoints) Client.SignOut(e, seat);
            ShowAccount(seat);
        }

        void SaveAccounts()
        {
            try { Accounts.Save(); }
            catch (Exception error) { Debug.LogWarning("Could not save the OurTaiko accounts: " + error.Message); }
        }

        // What the account center lists under 登录设备 for this sign-in.
        static string DeviceName(int seat) => $"OurTaikoPlay {seat + 1}P · {SystemInfo.deviceName}";

        // Loads the built-in servers' song lists again; accounts and scores stay as they are. A
        // server that fails keeps the list it had.
        public async Task LoadSongListAsync(CancellationToken cancel = default, bool refresh = true)
        {
            if (IsBusy) return;
            IsBusy = true;
            var results = new List<string>();
            try
            {
                var endpoints = BuiltInEndpoints();
                for (int i = 0; i < endpoints.Count; i++)
                {
                    var e = endpoints[i];
                    string name = e.Config.DisplayName;
                    if (cancel.IsCancellationRequested) { results.Add(name + ": cancelled"); continue; }
                    Activity = $"Loading song list ({i + 1} / {endpoints.Count}): {name}…";
                    try
                    {
                        await Client.LoadCatalogAsync(e, cancel);
                        results.Add($"{name}: {e.ChartCount} songs");
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning($"Song list of {name} failed: {error.Message}");
                        results.Add(name + ": " + (cancel.IsCancellationRequested ? "cancelled" : Describe(error)));
                    }
                }
                if (this != null && refresh) RefreshSongs();
            }
            finally { Activity = ""; SongListSummary = string.Join("   •   ", results); IsBusy = false; }
        }

        public static string Describe(Exception error) =>
            error is HttpStatusException status && (status.Status == 401 || status.Status == 403) ? "wrong username or password"
            : error is HttpStatusException { Status: 429 } ? "too many sign-in attempts, try again later"
            : "failed (" + error.Message + ")";

        // One connection per built-in server, created signed out; the accounts are set when signing in.
        IReadOnlyList<FanmadeEndpoint> BuiltInEndpoints()
        {
            var current = Client.Endpoints;
            if (builtInEndpoints.Count != builtIn.Length || builtInEndpoints.Any(e => !current.Contains(e)))
            {
                Client.Remove(builtInEndpoints.Contains);
                builtInEndpoints.Clear();
                foreach (var (name, url) in builtIn) builtInEndpoints.Add(Client.Add(new ServerConfig { name = name, baseUrl = url }));
            }
            return builtInEndpoints.ToArray();
        }

        // The seat's nameplate from Profile, or null while Profile does not answer (the plate keeps
        // what it showed).
        async Task<ProfileNameplate> NameplateAsync(OurTaikoAccount account, CancellationToken cancel)
        {
            try { return await Profiles.NameplateAsync(account.UserId, SettingManager.EnsureInstance().Settings.general.Language, cancel); }
            catch (Exception error)
            {
                Debug.LogWarning("Nameplate unavailable: " + error.Message);
                return null;
            }
        }

        // Reads a seat's nameplate from Profile again, e.g. after a 段位 gave it something new.
        public async Task RefreshNameplateAsync(int seat, CancellationToken cancel = default)
        {
            var account = Accounts.Seats[seat];
            if (account.IsSignedIn) ShowAccount(seat, await NameplateAsync(account, cancel));
        }

        // A seat's plate: its account's nickname, with the title and 段位 Profile gives it (plate
        // null keeps the ones shown). A seat without an account wears the guest plate. 1P's goes
        // into player.json, 2P's lasts this run.
        void ShowAccount(int seat, ProfileNameplate plate = null)
        {
            if (this == null) return;
            var account = Accounts.Seats[seat];
            var player = PlayerInfoController.EnsureInstance();
            var info = !account.IsSignedIn ? (seat == 0 ? new PlayerInfo() : PlayerInfo.SecondPlayer)
                : seat == 0 ? player.Info.Clone() : player.SecondInfo.Clone();
            if (account.IsSignedIn)
            {
                if (account.Nickname.Length > 0) info.name = account.Nickname;
                if (plate != null)
                {
                    info.title = plate.Title.Length > 0 ? plate.Title : PlayerInfo.DefaultTitle;
                    info.titleBackground = plate.TitleBackground; info.rainbow = plate.Rainbow;
                    info.dan = plate.Dan; info.gold = plate.Gold; info.danCombo = plate.Combo;
                }
            }
            if (seat == 1) player.SetSecond(info);
            else if (player.Info.ToJson() != info.ToJson()) player.Set(info);
        }

        public IEnumerable<ServerConfig> EnabledServers => BuiltInServers.Concat(Servers.servers.Where(s => s.enabled));

        // A missing file is created empty so it can be found and edited.
        public void Load(string path)
        {
            FilePath = path;
            builtIn = ServerList.BuiltIn;
            ServerList list = null;
            try
            {
                list = ServerList.Read(path) ?? new ServerList();
                if (list.RemoveBuiltIn() || !File.Exists(path)) list.Write(path);
            }
            catch (Exception error) { Debug.LogWarning("Ignoring unreadable server list: " + error.Message); }
            Servers = list ?? new ServerList();
        }

        // Tests use servers (and a cache folder) that never touch the player's files, and no
        // built-in server unless they name their own.
        public void UseUnsaved(ServerList list, string cacheRoot = null, params (string Name, string Url)[] builtInServers)
        {
            FilePath = null;
            builtIn = builtInServers ?? Array.Empty<(string, string)>();
            builtInEndpoints.Clear();
            // Nothing listens there: a test that signs in names its own account center.
            UseAccounts(new AccountStore(null, "test"), new AccountCenter("http://127.0.0.1:9"));
            UseProfiles(new ProfileCenter("http://127.0.0.1:9"));
            Servers = list ?? new ServerList();
            if (cacheRoot != null) UseCache(cacheRoot);
            else Disconnect();
        }

        void UseCache(string root, string databasePath = null)
        {
            Client?.Dispose();
            ClearSongs();
            builtInEndpoints.Clear();
            Client = new FanmadeClient(root, databasePath);
        }

        // Remembers the account ServerLogin used, in the same servers.json entry.
        public void Remember(int index, string username, string password, bool autoLogin)
        {
            var server = Servers.servers[index];
            server.username = username ?? ""; server.password = password ?? ""; server.autoLogin = autoLogin;
            if (string.IsNullOrEmpty(FilePath)) return;
            try { Servers.Write(FilePath); }
            catch (Exception error) { Debug.LogWarning("Could not save the server list: " + error.Message); }
        }

        // Forgets every connection and catalog song.
        public void Disconnect()
        {
            Client.Reset();
            builtInEndpoints.Clear();
            ClearSongs();
        }

        // Forgets the servers.json connections (ServerLogin starts them over each time); the built-in
        // servers stay as they are.
        public void DisconnectAdditional() => Client.Remove(e => !builtInEndpoints.Contains(e));

        // Publishes the client's catalog as songs, reusing the instances of charts already listed.
        public void RefreshSongs()
        {
            var keep = new HashSet<string>();
            songs.Clear(); charts.Clear();
            foreach (var chart in Client.Charts.Where(c => c.IsPlayable))
            {
                string key = SongKey(chart);
                keep.Add(key);
                if (!byKey.TryGetValue(key, out var entry))
                {
                    var song = ScriptableObject.CreateInstance<SongDefinition>();
                    song.name = key;
                    song.hideFlags = HideFlags.DontSave;
                    owned.Add(song);
                    entry = (song, chart);
                }
                entry.Chart = chart;
                entry.Song.onlineChart = chart;
                // A copy downloaded earlier may be an older version; loading downloads the chart again.
                SetChartText(entry.Song, null);
                entry.Song.genre = GenreFrame(chart.Genre);
                entry.Song.course = chart.Difficulties.First(d => d != null).Course;
                byKey[key] = entry;
                songs.Add(entry.Song);
                charts[entry.Song] = chart;
            }
            foreach (string key in byKey.Keys.Where(k => !keep.Contains(k)).ToList()) byKey.Remove(key);
            folders.Clear();
            // Indexed once: looking each id up among every song took seconds with a catalog of a
            // few thousand songs.
            var byChart = new Dictionary<(string Server, string Id), List<SongDefinition>>();
            foreach (var song in songs)
            {
                var chart = charts[song];
                if (!byChart.TryGetValue((chart.Server, chart.Id), out var list)) byChart[(chart.Server, chart.Id)] = list = new List<SongDefinition>(1);
                list.Add(song);
            }
            SongDefinition[] SongsOf(string server, IEnumerable<string> ids) =>
                ids.SelectMany(id => byChart.TryGetValue((server, id), out var list) ? list : Enumerable.Empty<SongDefinition>()).ToArray();
            foreach (var category in Client.Categories)
                folders.Add(new SongFolder
                {
                    Key = category.Server + "/" + category.Id, Title = category.Title, ServerName = category.ServerName, Server = category.Server,
                    Genre = GenreFrame(category.Genre), Songs = SongsOf(category.Server, category.ChartIds),
                });
            // Playlists follow the categories as further folders, drawn in their own genre.
            foreach (var playlist in Client.Playlists)
            {
                var folder = new SongFolder
                {
                    Key = playlist.Server + "/playlist/" + playlist.Id, Title = playlist.Title, ServerName = playlist.ServerName, Server = playlist.Server,
                    Genre = GenreFrame(playlist.Genre), Songs = SongsOf(playlist.Server, playlist.ChartIds),
                };
                folders.Add(folder);
                if (playlist.Cover != null) LoadCover(folder, playlist);
            }
        }

        // Fills folder.Cover once the picture is cached and decoded; the folder shows without it
        // until then, and for good when the download or the image is bad.
        async void LoadCover(SongFolder folder, FanmadePlaylist playlist)
        {
            string hash = playlist.Cover.Hash;
            if (covers.TryGetValue(hash, out var known)) { folder.Cover = known; return; }
            var client = Client;
            try
            {
                byte[] bytes = await client.PlaylistCoverAsync(playlist);
                // The servers may have been reset while the picture was loading.
                if (this == null || client != Client) return;
                if (!covers.TryGetValue(hash, out var sprite))
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "PlaylistCover " + hash, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                    owned.Add(texture);
                    if (!texture.LoadImage(bytes, true) || texture.width > 2048 || texture.height > 2048) throw new FanmadeException("PLAYLIST_COVER_INVALID");
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                    sprite.name = texture.name; sprite.hideFlags = HideFlags.DontSave;
                    owned.Add(sprite);
                    covers[hash] = sprite;
                }
                folder.Cover = sprite;
            }
            catch (Exception error) { Debug.LogWarning("Playlist cover unavailable: " + error.Message); }
        }

        // Search can discover songs not present in any category. Register them for preview,
        // download and score lookup without rebuilding or replacing the browsing catalog.
        public SongDefinition SearchSong(FanmadeChart chart)
        {
            string key = SongKey(chart);
            if (!byKey.TryGetValue(key, out var entry))
            {
                var song = ScriptableObject.CreateInstance<SongDefinition>();
                song.name = key; song.hideFlags = HideFlags.DontSave;
                owned.Add(song); entry = (song, chart);
            }
            entry.Chart = chart;
            entry.Song.onlineChart = chart;
            entry.Song.genre = GenreFrame(chart.Genre);
            entry.Song.course = chart.Difficulties.First(d => d != null).Course;
            byKey[key] = entry; charts[entry.Song] = chart;
            return entry.Song;
        }

        public static string SongKey(FanmadeChart chart) => "fanmade/" + chart.Server + "/" + chart.Id;

        public bool IsOnline(SongDefinition song) => song != null && charts.ContainsKey(song);
        public FanmadeChart ChartOf(SongDefinition song) => song != null && charts.TryGetValue(song, out var chart) ? chart : null;

        // After a download: the song plays the verified copy (the chart may be a newer version).
        public void SetPrepared(SongDefinition song, FanmadeChart chart, string playableTja, AudioClip music)
        {
            charts[song] = chart;
            song.onlineChart = chart;
            if (byKey.ContainsKey(song.name)) byKey[song.name] = (song, chart);
            SetChartText(song, playableTja);
            if (song.music != null && song.music != music && owned.Remove(song.music)) Destroy(song.music);
            song.music = music;
            if (music != null && !owned.Contains(music)) owned.Add(music);
        }

        // null drops the chart: the song lists from its metadata until SongLoadingScene downloads it.
        void SetChartText(SongDefinition song, string text)
        {
            if (song.chart != null && owned.Remove(song.chart)) Destroy(song.chart);
            song.chart = text == null ? null : new TextAsset(text) { name = song.name, hideFlags = HideFlags.DontSave };
            if (song.chart != null) owned.Add(song.chart);
        }

        void ClearSongs()
        {
            foreach (var item in owned) if (item != null) Destroy(item);
            owned.Clear(); songs.Clear(); byKey.Clear(); charts.Clear(); folders.Clear(); covers.Clear();
        }

        // box.def GENRE names (enums.h GENRE_MAP) to the Nijiiro bar_genre frame (GENRE_TO_REF_FRAME).
        public static int GenreFrame(string genre)
        {
            switch ((genre ?? "").Trim().ToUpperInvariant())
            {
                case "J-POP": case "POP": case "ポップス": return 1;
                case "ANIME": case "アニメ": return 2;
                case "GAME": case "GAME MUSIC": case "ゲームミュージック": return 3;
                case "NAMCO": case "NAMCO ORIGINAL": case "ナムコオリジナル": return 4;
                case "CLASSICAL": case "CLASSIC": case "クラシック": return 5;
                case "VARIETY": case "バラエティー": case "バラエティ": return 6;
                case "CHILDREN": case "どうよう": return 7;
                case "VOCALOID": case "ボーカロイド": return 8;
                default: return 0;
            }
        }
    }
}
