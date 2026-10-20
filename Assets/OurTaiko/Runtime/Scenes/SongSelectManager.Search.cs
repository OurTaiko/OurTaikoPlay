using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;

namespace OurTaiko
{
    public sealed partial class SongSelectManager
    {
        public bool SearchDialogOpen { get; set; }
        internal void PlaySearchSound(Sound sound)
        {
            if (SearchDialogOpen && !Switcher.IsInputBlocked) Play(sound);
        }
        public bool SearchActive { get; private set; }
        public SongSearchQuery SearchQuery { get; private set; } = new SongSearchQuery();
        public string SearchStatus { get; private set; } = "";
        public int SearchCount => searchSongs.Count;
        public event Action WheelRebuilt;
        public event Action SearchRequested;
        public string SearchFolderTitle => Words("Search / Sort", "検索・並べ替え", "搜索／排序");
        public string SearchFolderDescription => Words("Keyword · Difficulty · Stars · FC / AP", "Keyword・むずかしさ・★・FC / AP", "Keyword · 难度 · 星级 · FC / AP");
        // The source folder (its key) the search covers, null for every source; set by the search
        // board that opened the dialog and kept while the results show.
        public string SearchScope { get; private set; }
        string searchScopeTitle, searchHomeKey;
        static string Words(string en, string ja, string zh)
        {
            string language = SettingManager.Instance?.Settings.general.Language ?? "en";
            return language == "ja" ? ja : language.StartsWith("zh") ? zh : en;
        }
        static WheelItem NewSearchItem() => new WheelItem { Kind = ItemKind.Search };

        // A search board names the source it searches, if it is not every source.
        public string SearchDescription(WheelItem item)
        {
            string scope = item.Folder >= 0 ? folders[item.Folder].Title : SearchActive ? searchScopeTitle : null;
            return scope == null ? SearchFolderDescription : scope + " · " + SearchFolderDescription;
        }

        // The root board searches every source, one in a source folder only that source; while results
        // show, the root board searches the same sources again.
        void RequestSearch(WheelItem item)
        {
            if (!SearchActive)
            {
                searchHomeKey = OpenFolderKey;
                SearchScope = item.Folder >= 0 ? folders[item.Folder].Key : null;
                searchScopeTitle = item.Folder >= 0 ? folders[item.Folder].Title : null;
            }
            SearchRequested?.Invoke();
        }
        readonly List<SongDefinition> searchSongs = new List<SongDefinition>();
        int searchVersion;

        public async Task SearchAsync(SongSearchQuery query, CancellationToken cancel)
        {
            int version = ++searchVersion;
            var online = OnlineManager.EnsureInstance();
            var library = LocalSongLibrary.EnsureInstance();
            string scope = SearchScope, server = scope != null && scope != LocalKey ? scope.Substring(ServerKey("").Length) : null;
            var local = scope == null || scope == LocalKey
                ? library.Songs.Concat(library.Folders.SelectMany(f => f.AllSongs())).Distinct().Where(query.MatchesLocal).ToList()
                : new List<SongDefinition>();
            var replies = scope == LocalKey ? Array.Empty<FanmadeSearchResult>() : await online.Client.SearchAsync(query, cancel, server);
            cancel.ThrowIfCancellationRequested();
            if (version != searchVersion) return;
            var found = local;
            foreach (var reply in replies)
                foreach (var chart in reply.Charts)
                    if (chart.IsPlayable && query.Matches(chart.ToSongInfo(null))) found.Add(online.SearchSong(chart));
            bool NeedsClear(SongDefinition song) => song.onlineChart != null ? online.Client.SearchNeedsClear(song.onlineChart, query)
                : query.Order != SongSearchOrder.Default && song.ReadDisplayInfo().Courses.Where(query.Matches)
                    .Any(c => (int)(SongScores.Get(song, c.Difficulty)?.crown ?? Crown.None) < (query.Order == SongSearchOrder.UnFullCombo ? 2 : 3));
            searchSongs.Clear();
            searchSongs.AddRange(found.Distinct().OrderByDescending(NeedsClear));
            var errors = replies.Where(r => r.Error != null).Select(r => r.Endpoint.Config.DisplayName + ": " + r.Error).ToList();
            string scopeUrl = server != null ? online.Client.Endpoint(server)?.Config.baseUrl.TrimEnd('/') : null;
            foreach (var config in online.EnabledServers)
            {
                string url = (config.baseUrl ?? "").TrimEnd('/');
                if (scope == LocalKey || server != null && url != scopeUrl) continue;
                if (!online.Client.Endpoints.Any(e => e.IsConnected && e.Config.baseUrl.TrimEnd('/') == url))
                    errors.Add(config.DisplayName + ": offline");
            }
            SearchStatus = string.Join(" · ", errors);
            SearchQuery = query; SearchActive = true;
            BuildSearchWheel(null);
            WheelRebuilt?.Invoke();
        }

        // The results sit in one closed folder per source, Local first, then the servers in endpoint
        // order; a single source opens at once. Coming back from a song reopens its folder on it.
        void BuildSearchWheel(SongDefinition selected)
        {
            items.Clear(); open.Clear();
            var endpoints = OnlineManager.Instance.Client.Endpoints.Select(e => e.Id).ToList();
            var sources = searchSongs.Where(s => s != null).GroupBy(s => s.onlineChart?.Server)
                .OrderBy(g => g.Key == null ? -1 : endpoints.IndexOf(g.Key) is int i && i >= 0 ? i : endpoints.Count)
                .Select(g => new SongFolder
                {
                    Key = "search:" + (g.Key ?? "local"), Server = g.Key, Songs = g.ToArray(),
                    Title = (g.Key == null ? LocalTitle : OnlineManager.Instance.Client.Endpoint(g.Key)?.Config.DisplayName ?? g.Key) + " (" + g.Count() + ")",
                }).ToArray();
            folders = sources;
            parents = sources.Select(_ => -1).ToArray();
            for (int f = 0; f < folders.Length; f++) items.Add(FolderItem(f));
            items.Add(NewSearchItem());
            items.Add(new WheelItem { Kind = ItemKind.Back });
            Focused = 0;
            int home = selected != null ? Array.FindIndex(folders, f => f.Songs.Contains(selected)) : -1;
            if (home < 0 && folders.Length == 1) home = 0;
            if (home < 0) return;
            InsertFolder(FolderItemIndex(home), null);
            int anchor = AnchorOf(home), found = items.FindIndex(i => i.Song != null && i.Song == selected);
            Focused = found >= 0 ? found : anchor + 1;
            OpenFolderKey = folders[home].Key;
        }

        public void CancelSearch() => searchVersion++;
        void ClearSearchState()
        {
            CancelSearch(); SearchActive = false; SearchDialogOpen = false;
            SearchQuery = new SongSearchQuery(); SearchStatus = ""; searchSongs.Clear();
            SearchScope = searchScopeTitle = searchHomeKey = null;
        }
        // Back to the folders open before the search, on the search board that started it.
        public void ClearSearch()
        {
            string scope = SearchScope, home = searchHomeKey;
            ClearSearchState();
            OpenFolderKey = home;
            var ready = CourseReady;
            Load(hitSoundCount); CourseReady = ready;
            int board = items.FindIndex(i => i.Kind == ItemKind.Search && (scope == null ? i.Folder < 0 : i.Folder >= 0 && folders[i.Folder].Key == scope));
            Focused = board >= 0 ? board : items.FindIndex(i => i.Kind == ItemKind.Search);
            WheelRebuilt?.Invoke();
        }
    }
}
