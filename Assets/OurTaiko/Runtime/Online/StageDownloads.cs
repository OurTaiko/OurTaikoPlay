using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OurTaiko.Online
{
    // The play backdrops the game does not carry, from the distribution root (Tools/stage-publish
    // says how they get there):
    //   stage/bundle/<version>/catalog.json             every set and preset (StageCatalogFile), as the
    //                                                   bundles of that version are drawn
    //   stage/bundle/<version>/<platform>/index.json    this platform's files: each one's name
    //                                                   ("header/miku"), where it is, its size and SHA-256
    //   stage/bundle/<version>/<platform>/<file>        one asset bundle a backdrop file
    // The catalog and index are read once a launch (Refresh); the catalog is kept beside the
    // fetched files (StageLibrary.Downloads, Resources/Stage beside the songs), so a launch that
    // cannot reach the root still knows every preset it has the files of. A play's own files are
    // downloaded there as the play is prepared (Resolve), as <part>/<name>.bundle, and files.json
    // remembers which version of each is here. Nothing here ever keeps a play from starting:
    // whatever cannot be had is passed over for what can.
    public static class StageDownloads
    {
        // The folder under the distribution root; another one only where a test or a trial says so.
        public const string PublishedFolder = "stage";
        public static string Folder = PublishedFolder;
        public const string IndexFile = "index.json", HereFile = "files.json";
        public const int IndexVersion = 1;
        // How many files of a play's backdrop are downloaded at once.
        public const int Parallel = 4;

        [Serializable]
        public sealed class Entry
        {
            public string name, file, sha256;
            public long size;
        }

        [Serializable]
        public sealed class Index
        {
            public int version = IndexVersion;
            public Entry[] files = Array.Empty<Entry>();
        }

        static readonly object gate = new object();
        static Dictionary<string, Entry> published, here;
        static long received, expected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Forget();
            Folder = PublishedFolder;
        }

        // Forgets the index read and which files are here (read again from files.json).
        public static void Forget()
        {
            lock (gate) { published = null; here = null; }
        }

        // Whether the index was read this launch: without it nothing is downloaded.
        public static bool HasIndex { get { lock (gate) return published != null; } }
        // Bytes received and expected of the backdrop being downloaded now (0, 0 when none).
        public static long Received => Interlocked.Read(ref received);
        public static long Expected => Interlocked.Read(ref expected);

        // This platform's bundles of the version this game reads.
        static string BundleFolder => Folder + "/bundle/" + StageLibrary.BundleVersion + "/" + StageLibrary.Platform + "/";
        // The catalog that goes with this version's bundles: how their files are placed (a drumroll
        // set's spread) changes with them. stage/catalog.json is version 1's, which games out read.
        static string CatalogPath => Folder + "/bundle/" + StageLibrary.BundleVersion + "/" + StageLibrary.CatalogFile;

        // Reads the catalog and the index from the root and keeps the catalog beside the fetched
        // files, where StageLibrary reads it. Never throws but for cancellation: when the root cannot
        // be read, the catalog kept by an earlier launch goes on being used.
        public static async Task RefreshAsync(string site = ServerConfig.Site, CancellationToken cancel = default)
        {
            if (StageLibrary.Platform == null) return;
            try
            {
                var root = await Distribution.RootAsync(site, cancel);
                string catalogText = await Distribution.FetchAsync(root.Locate(CatalogPath), url => TextAsync(url, cancel), cancel);
                string indexText = await Distribution.FetchAsync(root.Locate(BundleFolder + IndexFile), url => TextAsync(url, cancel), cancel);
                // Both or neither: a catalog whose files cannot be found names sets nobody can draw.
                var entries = Read(indexText, checkPaths: true);
                var catalog = StageCatalogFile.Read(catalogText);
                if (Application.isPlaying) UnityEngine.Object.Destroy(catalog); else UnityEngine.Object.DestroyImmediate(catalog);
                string kept = StageLibrary.DownloadedCatalog;
                await Task.Run(() => ObjectCache.Store(kept, System.Text.Encoding.UTF8.GetBytes(catalogText)), cancel);
                lock (gate) published = entries;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception error) { Log.Warn("Stage", "Could not read the published backdrops from the distribution root: " + error.Message); }
        }

        static async Task<string> TextAsync(string url, CancellationToken cancel)
        {
            using var response = await OurTaikoHttpClient.GetAsync(url, TimeSpan.FromSeconds(30), cancel);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode + " for " + url);
            string text = await response.Content.ReadAsStringAsync();
            if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException(url + " is too large");
            return text;
        }

        static Dictionary<string, Entry> Read(string text, bool checkPaths)
        {
            var read = JsonUtility.FromJson<Index>(text);
            if (read?.files == null) throw new FormatException("The backdrops' index lists no files.");
            if (read.version < 1 || read.version > IndexVersion) throw new FormatException($"The backdrops' index is version {read.version}; this game reads up to {IndexVersion}.");
            var entries = new Dictionary<string, Entry>();
            foreach (var entry in read.files)
            {
                // A name is <part>/<file name>, and nothing that could leave the folder it is saved in.
                bool named = !string.IsNullOrEmpty(entry.name) && entry.name.Split('/').Length == 2 && Distribution.ValidPath(entry.name);
                if (!named || entry.size <= 0 || !ObjectCache.ValidHash(entry.sha256) || (checkPaths && !Distribution.ValidPath(BundleFolder + entry.file)))
                    throw new FormatException("The backdrops' index has an entry that cannot be used: " + entry.name);
                entries[entry.name] = entry;
            }
            return entries;
        }

        // Which version of each fetched file is here, as files.json says; empty when it cannot be read.
        static Dictionary<string, Entry> Here()
        {
            lock (gate)
            {
                if (here != null) return here;
                here = new Dictionary<string, Entry>();
                try
                {
                    string path = Path.Combine(StageLibrary.Downloads, HereFile);
                    if (File.Exists(path)) here = Read(File.ReadAllText(path), checkPaths: false);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is ArgumentException)
                {
                    Log.Warn("Stage", "The record of the fetched backdrops cannot be read, so they are fetched again: " + error.Message);
                }
                return here;
            }
        }

        static void Remember(Entry entry)
        {
            lock (gate)
            {
                var known = Here();
                known[entry.name] = entry;
                try
                {
                    var index = new Index { files = new List<Entry>(known.Values).ToArray() };
                    ObjectCache.Store(Path.Combine(StageLibrary.Downloads, HereFile), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(index, true)));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Log.Warn("Stage", "Could not record a fetched backdrop: " + error.Message); }
            }
        }

        // The set a part's files belong to: its name in the choice.
        static string SetOf(StageCatalog catalog, StageChoice choice, string folder) => folder switch
        {
            StageCatalog.HeaderFolder => choice.Header,
            StageCatalog.SceneFolder => choice.Scene,
            StageCatalog.ClearFolder => choice.Clear,
            StageCatalog.DancerFolder => choice.Troupe,
            StageCatalog.FooterFolder => choice.Footer,
            StageCatalog.CrowdFolder => choice.Crowd,
            StageCatalog.RendaFolder => choice.Renda,
            _ => catalog.Pack(choice.Preset)?.chibi?.Trim(),
        };

        // A file a set may be without: 2P's own header, a troupe's members past the first.
        static bool Optional(StageCatalog catalog, string folder, string name, string set) => name != catalog.MainFile(folder, set);

        // Every file of the choice is here to draw: the game's, the player's, or fetched (the ones
        // that were not, or are of an older version, downloaded now, a few at a time). False when
        // one the choice cannot do without cannot be had.
        static async Task<bool> EnsureAsync(StageCatalog catalog, StageChoice choice, int players, string site, CancellationToken cancel)
        {
            Dictionary<string, Entry> index;
            lock (gate) index = published;
            var known = Here();
            var wanted = new List<(string folder, string name, bool optional, Entry entry)>();
            foreach (var (folder, name) in catalog.Files(choice, players))
            {
                string set = SetOf(catalog, choice, folder);
                if (StageLibrary.Carries(folder, set)) continue;
                bool optional = Optional(catalog, folder, name, set);
                Entry entry = null;
                index?.TryGetValue(folder + "/" + name, out entry);
                string path = StageLibrary.DownloadedFile(folder, name);
                if (entry == null)
                {
                    // Not published (any more, or the root was not read): what is here is what there is.
                    if (StageLibrary.HasOutside(folder, name) || optional) continue;
                    return false;
                }
                bool current;
                lock (gate) current = known.TryGetValue(entry.name, out var have) && have.sha256 == entry.sha256;
                if (current && File.Exists(path) && new FileInfo(path).Length == entry.size) continue;
                wanted.Add((folder, name, optional, entry));
            }
            if (wanted.Count == 0) return true;
            long total = 0;
            foreach (var w in wanted) total += w.entry.size;
            var got = new long[wanted.Count];
            Interlocked.Exchange(ref received, 0);
            Interlocked.Exchange(ref expected, total);
            try
            {
                // Not disposed: a cancelled play leaves the others to finish and give their slots back.
                var slots = new SemaphoreSlim(Parallel);
                async Task<bool> One(int i)
                {
                    var (folder, name, _, entry) = wanted[i];
                    await slots.WaitAsync(cancel);
                    try
                    {
                        var address = await Distribution.LocateAsync(BundleFolder + entry.file, site, cancel);
                        var data = await Distribution.FetchAsync(address, url => ObjectCache.FetchAsync(url, entry.size, entry.sha256, count =>
                        {
                            // Counted again from nothing when the canonical root is tried.
                            Interlocked.Add(ref received, count - Interlocked.Exchange(ref got[i], count));
                        }, cancel), cancel);
                        string path = StageLibrary.DownloadedFile(folder, name);
                        await Task.Run(() => ObjectCache.Store(path, data), cancel);
                        Remember(entry);
                        return true;
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        Log.Warn("Stage", $"The backdrop file {folder}/{name} could not be downloaded: {error.Message}");
                        // An older copy that is here still draws.
                        return StageLibrary.HasOutside(folder, name);
                    }
                    finally { slots.Release(); }
                }
                var tasks = new Task<bool>[wanted.Count];
                for (int i = 0; i < tasks.Length; i++) tasks[i] = One(i);
                var done = await Task.WhenAll(tasks);
                for (int i = 0; i < done.Length; i++)
                    if (!done[i] && !wanted[i].optional) return false;
                return true;
            }
            finally
            {
                Interlocked.Exchange(ref received, 0);
                Interlocked.Exchange(ref expected, 0);
            }
        }

        // The backdrop of a play asked for `presets` (the one that counts most first), with its
        // files here to draw. A preset whose files cannot be had is passed over for the next one;
        // when what is drawn at random cannot be had either, the play gets a backdrop out of what
        // is already here. Throws only for cancellation.
        public static async Task<StageChoice> ResolveAsync(IReadOnlyList<string> presets, StageMode mode, int players, System.Random random,
            string site = ServerConfig.Site, CancellationToken cancel = default)
        {
            var catalog = StageLibrary.Catalog;
            var asked = new List<string>(presets ?? Array.Empty<string>());
            while (true)
            {
                var pack = catalog.Asked(asked, mode);
                var choice = catalog.Choose(asked, mode, random);
                if (await EnsureAsync(catalog, choice, players, site, cancel)) return choice;
                // Whose pack it was: one the play asked for is dropped and the next one tried; the
                // mode's own, or a draw, has nothing after it.
                int at = pack == null ? -1 : asked.FindIndex(name => catalog.Pack(name) == pack);
                if (at < 0 || choice.Preset == null || catalog.Pack(choice.Preset) != pack) break;
                Log.Info("Stage", $"The backdrop {pack.preset} cannot be had; trying what comes after it.");
                asked.RemoveRange(0, at + 1);
            }
            return catalog.Choose(Array.Empty<string>(), mode, random, StageLibrary.Has);
        }
    }
}
