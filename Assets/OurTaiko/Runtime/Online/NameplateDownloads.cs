using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // The nameplate set (NameplateSet) the game does not carry, from the distribution root, where
    // OurTaikoNet's tools/nameplate/publish-lumen.sh puts what Tools/nameplate-samples/lumen bakes:
    //   nameplate/lumen.json                                {"version": 1, "catalog": <composition/catalog.json>,
    //                                                        "files": {"<folder>/<name>": {"path", "size", "sha256"}}}
    //   nameplate/lumen/<folder>/<name>-<hash12>.otanim     each item, a path under nameplate/
    // Read in every sign-in check, guest or not, as the PNG art before it was, so the first screen
    // already draws whole plates, anyone's: every file the cache lacks is downloaded into the object
    // cache (objects/<sha256>), a few at a time. The whole set is about 2.3 MB, read once; a
    // published file is never replaced, so a later check downloads only what was published since,
    // and reading a file into a plate waits until a plate wears it (NameplateItems).
    // The list last read is kept in the cache (StateFile) and used at once at launch, and in place
    // of the root's when that cannot be read: what was downloaded draws offline.
    public static class NameplateDownloads
    {
        public const string Folder = "nameplate/", ListFile = "lumen.json", StateFile = "nameplate-lumen.json";
        public const int ListVersion = 1;
        // How many files are downloaded at once.
        public const int Parallel = 4;
        // The largest file the list may name.
        const long MostBytes = 8 * 1024 * 1024;

        static readonly Regex itemId = new Regex(@"^(band|effect|icon|composition)/[a-z0-9][a-z0-9_.-]{0,63}$");
        // The set made from a list here and that list, so the same list read again draws nothing anew.
        static (NameplateSet set, string text) made;

        // One item of the list: where it is under nameplate/, its size and its hash.
        public readonly struct Entry
        {
            public readonly string Path, Sha256;
            public readonly long Size;
            public Entry(string path, long size, string sha256) { Path = path; Size = size; Sha256 = sha256; }
        }

        // lumen.json: the catalog and the files by id. Throws FormatException when it is not one
        // this game reads.
        public static (JObject catalog, Dictionary<string, Entry> files) Read(string text)
        {
            JObject list;
            try { list = JObject.Parse(text); }
            catch (Newtonsoft.Json.JsonException error) { throw new FormatException("the nameplate list is not JSON: " + error.Message); }
            int version = (int?)(list["version"] as JValue) ?? 0;
            if (version != ListVersion) throw new FormatException($"the nameplate list is version {version}; this game reads {ListVersion}");
            if (!(list["catalog"] is JObject catalog)) throw new FormatException("the nameplate list has no catalog");
            if (!(list["files"] is JObject listed)) throw new FormatException("the nameplate list lists no files");
            var files = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var file in listed.Properties())
            {
                var entry = file.Value as JObject;
                string path = (string)(entry?["path"] as JValue), hash = (string)(entry?["sha256"] as JValue);
                long size = (long?)(entry?["size"] as JValue) ?? 0;
                if (!itemId.IsMatch(file.Name) || path == null || !path.EndsWith(NameplateItems.Extension, StringComparison.Ordinal)
                    || !Distribution.ValidPath(Folder + path) || size <= 0 || size > MostBytes || !ObjectCache.ValidHash(hash))
                    throw new FormatException("the nameplate list has an entry that cannot be used: " + file.Name);
                files[file.Name] = new Entry(path, size, hash);
            }
            return (catalog, files);
        }

        // The set a list names, its files where the object cache keeps them.
        public static NameplateSet SetOf(JObject catalog, Dictionary<string, Entry> files, string cacheRoot)
        {
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files) paths[file.Key] = ObjectCache.PathOf(cacheRoot, file.Value.Sha256);
            return new NameplateSet(catalog, paths);
        }

        // Draws plates from the list kept by an earlier launch, when no set is in use yet. Never throws.
        public static void UseKept(string cacheRoot)
        {
            if (NameplateItems.Set != null) return;
            string state = Path.Combine(cacheRoot, StateFile);
            try
            {
                if (!File.Exists(state)) return;
                string text = File.ReadAllText(state);
                var (catalog, files) = Read(text);
                Use(SetOf(catalog, files, cacheRoot), text);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is InvalidDataException)
            {
                Log.Warn("Nameplate", "The nameplate set kept from before cannot be used: " + error.Message);
            }
        }

        // Reads the list from the root `site` names, downloads the files the cache lacks and draws
        // plates from it; a file that cannot be had is left out (its slot is drawn empty) and tried
        // again next time. When the list cannot be read, the kept one goes on being used. Never
        // throws but for cancellation.
        public static async Task RefreshAsync(string cacheRoot, string site = ServerConfig.Site, CancellationToken cancel = default)
        {
            UseKept(cacheRoot);
            try
            {
                var root = await Distribution.RootAsync(site, cancel);
                string text = await Distribution.FetchAsync(root.Locate(Folder + ListFile), url => TextAsync(url, cancel), cancel);
                var (catalog, files) = Read(text);
                var wanted = await Task.Run(() =>
                {
                    var missing = new List<KeyValuePair<string, Entry>>();
                    foreach (var file in files)
                        if (!ObjectCache.Matches(ObjectCache.PathOf(cacheRoot, file.Value.Sha256), file.Value.Size, file.Value.Sha256)) missing.Add(file);
                    return missing;
                }, cancel);
                int failed = 0;
                if (wanted.Count > 0)
                {
                    // Not disposed: a cancelled check leaves the others to finish and give their slots back.
                    var slots = new SemaphoreSlim(Parallel);
                    async Task<bool> One(KeyValuePair<string, Entry> file)
                    {
                        await slots.WaitAsync(cancel);
                        try
                        {
                            var entry = file.Value;
                            var data = await Distribution.FetchAsync(root.Locate(Folder + entry.Path),
                                url => ObjectCache.FetchAsync(url, entry.Size, entry.Sha256, null, cancel), cancel);
                            await Task.Run(() => ObjectCache.Store(ObjectCache.PathOf(cacheRoot, entry.Sha256), data), cancel);
                            return true;
                        }
                        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            Log.Warn("Nameplate", $"The nameplate item {file.Key} could not be downloaded: {error.Message}");
                            return false;
                        }
                        finally { slots.Release(); }
                    }
                    var tasks = new List<Task<bool>>();
                    foreach (var file in wanted) tasks.Add(One(file));
                    foreach (bool done in await Task.WhenAll(tasks))
                        if (!done) failed++;
                }
                // The catalog is checked here, before the list is kept for the next launch. The same
                // list again is drawn anew only when files it lacked have come.
                if (made.text != text || NameplateItems.Set != made.set) Use(SetOf(catalog, files, cacheRoot), text);
                else if (wanted.Count > failed) NameplateItems.Use(made.set);
                await Task.Run(() => ObjectCache.Store(Path.Combine(cacheRoot, StateFile), Encoding.UTF8.GetBytes(text)), cancel);
                if (wanted.Count > 0) Log.Info("Nameplate", $"Downloaded {wanted.Count - failed} of the {wanted.Count} nameplate items the cache lacked.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception error) { Log.Warn("Nameplate", "Could not fetch the nameplate set: " + error.Message); }
        }

        static void Use(NameplateSet set, string text)
        {
            NameplateItems.Use(set);
            made = (set, text);
        }

        static async Task<string> TextAsync(string url, CancellationToken cancel)
        {
            using var response = await OurTaikoHttpClient.GetAsync(url, TimeSpan.FromSeconds(30), cancel);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode + " for " + url);
            string text = await response.Content.ReadAsStringAsync();
            if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException(url + " is too large");
            return text;
        }
    }
}
