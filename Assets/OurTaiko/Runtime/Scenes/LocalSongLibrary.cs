using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace OurTaiko
{
    // Global holder of the local songs (OurTaikoPlayer's tja_path), created before the first scene
    // and kept across loads like OnlineManager. LocalSongScanner reads the configured roots on a
    // worker thread; ServerLogin starts a rescan each time, and the first read waits for it. Each
    // chart keeps its SongDefinition across rescans, so the selected song survives a refresh.
    public sealed class LocalSongLibrary : MonoBehaviour
    {
        public static LocalSongLibrary Instance { get; private set; }
        public static string DefaultRoot => Path.Combine(GamePaths.Root, "Songs");

        public IReadOnlyList<string> Roots => roots;
        // Charts outside box.def folders, by path; then the top-level box.def folders, which hold
        // their own sub-folders.
        public IReadOnlyList<SongDefinition> Songs { get { Complete(); return songs; } }
        public IReadOnlyList<SongFolder> Folders { get { Complete(); return folders; } }

        readonly List<SongDefinition> songs = new List<SongDefinition>();
        readonly List<SongFolder> folders = new List<SongFolder>();
        readonly Dictionary<string, SongDefinition> byKey = new Dictionary<string, SongDefinition>();
        Task<LocalSongCatalog> scan;
        bool scanned;
        SettingManager settings;
        string[] configuredPaths, roots;
        string testRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();

        public static LocalSongLibrary EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<LocalSongLibrary>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(LocalSongLibrary)).AddComponent<LocalSongLibrary>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            settings = SettingManager.EnsureInstance();
            configuredPaths = (string[])settings.Settings.general.songLibraryPaths.Clone();
            roots = SongLibraryPaths.Resolve(configuredPaths, DefaultRoot);
            settings.Changed += SettingsChanged;
            // Created up front so players can find where to put their songs.
            try { Directory.CreateDirectory(DefaultRoot); }
            catch (System.Exception error) { Debug.LogWarning("Could not create the songs folder: " + error.Message); }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (settings != null) settings.Changed -= SettingsChanged;
            Clear();
            Instance = null;
        }

        void SettingsChanged(GameSettings value)
        {
            if (configuredPaths.SequenceEqual(value.general.songLibraryPaths)) return;
            configuredPaths = (string[])value.general.songLibraryPaths.Clone();
            roots = SongLibraryPaths.Resolve(configuredPaths, DefaultRoot);
            testRoot = null;
            scan = null; scanned = false;
            Clear();
            Refresh();
        }

        // Reads another songs folder from the next access on (tests use a temporary copy).
        public void UseRoot(string root)
        {
            testRoot = root;
            roots = new[] { root };
            scan = null; scanned = false;
            Clear();
        }

        // Starts a rescan in the background; Songs and Folders wait for it. The offline chart
        // cache is read again alongside, for the servers song select cannot reach.
        public void Refresh()
        {
            Online.OnlineManager.Instance?.RescanCache();
            string root = testRoot;
            string[] snapshot = (string[])roots.Clone();
            string defaultRoot = DefaultRoot;
            string language = SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "en";
            scan = Task.Run(() => root != null ? LocalSongScanner.Scan(root, language)
                : LocalSongScanner.ScanRoots(snapshot, defaultRoot, language));
        }

        void Complete()
        {
            if (scan == null && !scanned) Refresh();
            if (scan == null) return;
            var task = scan;
            scan = null;
            scanned = true;
            LocalSongCatalog catalog;
            try { catalog = task.Result; }
            catch (System.AggregateException error)
            {
                Debug.LogWarning("Could not read the songs folder: " + error.GetBaseException().Message);
                return;
            }
            Apply(catalog);
        }

        void Apply(LocalSongCatalog catalog)
        {
            var keep = new HashSet<string>();
            songs.Clear(); folders.Clear();
            foreach (var chart in catalog.Songs) songs.Add(Song(chart, null, keep));
            // OpenTaiko: a song takes its folder's genre when the box.def names one, else its own GENRE.
            SongFolder Folder(LocalFolder folder) => new SongFolder
            {
                Key = folder.Key, Title = folder.Title, Genre = Online.OnlineManager.GenreFrame(folder.Genre),
                Folders = folder.Folders.Select(Folder).ToArray(),
                Songs = folder.Charts.Select(chart => Song(chart, folder.Genre, keep)).ToArray(),
            };
            folders.AddRange(catalog.Folders.Select(Folder));
            foreach (string key in byKey.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                Release(byKey[key]);
                byKey.Remove(key);
            }
        }

        SongDefinition Song(LocalChart chart, string folderGenre, HashSet<string> keep)
        {
            if (!byKey.TryGetValue(chart.Key, out var song))
            {
                song = ScriptableObject.CreateInstance<SongDefinition>();
                song.name = chart.Key;
                song.hideFlags = HideFlags.DontSave;
                byKey[chart.Key] = song;
            }
            keep.Add(chart.Key);
            if (song.chart == null || song.chart.text != chart.Text)
            {
                if (song.chart != null) Destroy(song.chart);
                song.chart = new TextAsset(chart.Text) { name = chart.Key, hideFlags = HideFlags.DontSave };
            }
            song.audioPath = chart.AudioPath;
            song.moviePath = chart.MoviePath;
            song.localChartSha256 = chart.Sha256;
            song.genre = Online.OnlineManager.GenreFrame(string.IsNullOrEmpty(folderGenre) ? chart.Info.Genre : folderGenre);
            var oni = chart.Info.Course(Difficulty.Oni);
            song.course = (oni ?? chart.Info.Courses().Last()).Course;
            return song;
        }

        static void Release(SongDefinition song)
        {
            if (song.chart != null) Destroy(song.chart);
            Destroy(song);
        }

        void Clear()
        {
            foreach (var song in byKey.Values) Release(song);
            byKey.Clear(); songs.Clear(); folders.Clear();
        }
    }
}
