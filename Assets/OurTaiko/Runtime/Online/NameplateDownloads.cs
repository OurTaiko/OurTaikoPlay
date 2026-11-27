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
    // The nameplate set (NameplateSet) the game does not carry, as the site says there is:
    //   GET <site>/api/v2/nameplate/set   {"version": 1, "layout": <the plate's layouts>,
    //                                      "files": {"<folder>/<name>": "<sha256>"}}
    //   GET <site>/api/v2/title/items     {"presets": [{"name", "kind": "player"|"ai",
    //                                      "layers": {"band", "effect", "icon", "effectOverflow"}}], …}
    // and each picture under the distribution root by its hash, nameplate/items/<sha256>.otanim.
    // Read in every sign-in check, guest or not, so the first screen already draws whole plates,
    // anyone's: every file the cache lacks is downloaded into the object cache (objects/<sha256>),
    // a few at a time. The whole set is about 2.3 MB, read once; a picture's file is never
    // replaced (a new picture is a new hash), so a later check downloads only what is new, and
    // reading a file into a plate waits until a plate wears it (NameplateItems).
    // What the site last said is kept in the cache (StateFile) and used at once at launch, and in
    // place of the site's when that cannot be read: what was downloaded draws offline.
    // A title the site gives says which picture each of its parts is, by hash (TitleParts): one
    // the cache lacks, of a part added since the set was read, is fetched when a plate first wears
    // it (Wanted), and the plates are drawn again when it has come.
    public static class NameplateDownloads
    {
        public const string Folder = "nameplate/items/", SetPath = "/api/v2/nameplate/set", PartsPath = "/api/v2/title/items";
        public const string StateFile = "nameplate-set.json";
        public const int SetVersion = 1;
        // How many files are downloaded at once.
        public const int Parallel = 4;
        // The largest file a picture may be.
        const long MostBytes = 8 * 1024 * 1024;

        static readonly Regex itemId = new Regex(@"^(band|effect|icon|composition)/[a-z0-9][a-z0-9_.-]{0,63}$");
        static readonly Regex presetName = new Regex(@"^[a-z0-9][a-z0-9_.-]{0,63}$");
        // The set made from what the site said here and that text, so the same again draws nothing anew.
        static (NameplateSet set, string text) made;
        // Where pictures are kept and which site's root they come from, as last used, and the
        // hashes asked for one by one there (each once: one that cannot be had is tried again by the
        // next launch).
        static (string cacheRoot, string site) from;
        static readonly HashSet<string> asked = new HashSet<string>(StringComparer.Ordinal);

        // Pictures are kept under `cacheRoot` and fetched from the root of `site` from now on.
        static void Keep(string cacheRoot, string site)
        {
            if (from.cacheRoot != cacheRoot || from.site != site) asked.Clear();
            from = (cacheRoot, site);
            NameplateItems.UsePictures(hash => ObjectCache.PathOf(cacheRoot, hash), Wanted);
        }

        // A plate wears a part told by a hash the cache has no picture of. Main thread.
        static void Wanted(string hash)
        {
            if (!ObjectCache.ValidHash(hash) || !asked.Add(hash)) return;
            _ = FetchOneAsync(hash, from.cacheRoot, from.site);
        }

        static async Task FetchOneAsync(string hash, string cacheRoot, string site)
        {
            try
            {
                var root = await Distribution.RootAsync(site);
                var data = await Distribution.FetchAsync(root.Locate(PathOf(hash)), url => ObjectCache.FetchAsync(url, hash, MostBytes, null, default), default);
                await Task.Run(() => ObjectCache.Store(ObjectCache.PathOf(cacheRoot, hash), data));
                if (from.cacheRoot == cacheRoot) NameplateItems.Redraw();
            }
            catch (Exception error) { Log.Warn("Nameplate", $"The nameplate picture {hash} could not be downloaded: {error.Message}"); }
        }

        // Where the picture of that hash is under the distribution root.
        public static string PathOf(string hash) => Folder + hash + NameplateItems.Extension;

        // What is kept of the site's two answers, and read back: one text.
        public static string Kept(string set, string parts) => new JObject { ["set"] = JObject.Parse(set), ["parts"] = JObject.Parse(parts) }.ToString(Newtonsoft.Json.Formatting.None);

        // The site's answers as kept (Kept): the catalog plates are drawn by (the layouts, and the
        // presets as its titles and AI titles) and each picture's hash by id. Throws FormatException
        // when they are not what this game reads.
        public static (JObject catalog, Dictionary<string, string> files) Read(string text)
        {
            JObject kept;
            try { kept = JObject.Parse(text); }
            catch (Newtonsoft.Json.JsonException error) { throw new FormatException("the nameplate set is not JSON: " + error.Message); }
            if (!(kept["set"] is JObject set) || !(kept["parts"] is JObject parts)) throw new FormatException("the nameplate set is not whole");
            int version = (int?)(set["version"] as JValue) ?? 0;
            if (version != SetVersion) throw new FormatException($"the nameplate set is version {version}; this game reads {SetVersion}");
            if (!(set["layout"] is JObject layout)) throw new FormatException("the nameplate set has no layout");
            if (!(set["files"] is JObject listed)) throw new FormatException("the nameplate set lists no files");
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in listed.Properties())
            {
                string hash = (string)(file.Value as JValue);
                if (!itemId.IsMatch(file.Name) || !ObjectCache.ValidHash(hash) || !Distribution.ValidPath(PathOf(hash)))
                    throw new FormatException("the nameplate set has a file that cannot be used: " + file.Name);
                files[file.Name] = hash;
            }
            if (!(parts["presets"] is JArray presets)) throw new FormatException("the nameplate set has no presets");
            var catalog = (JObject)layout.DeepClone();
            JObject titles = new JObject(), aiTitles = new JObject();
            foreach (var preset in presets)
            {
                string name = (string)(preset["name"] as JValue), kind = (string)(preset["kind"] as JValue);
                if (name == null || !presetName.IsMatch(name) || !(preset["layers"] is JObject layers) || (kind != "player" && kind != "ai"))
                    throw new FormatException("the nameplate set has a preset that cannot be used: " + name);
                var look = new JObject();
                foreach (string slot in new[] { "band", "effect", "icon" })
                {
                    string item = (string)(layers[slot] as JValue);
                    if (item == null) continue;
                    if (!itemId.IsMatch(item) || !item.StartsWith(slot + "/", StringComparison.Ordinal))
                        throw new FormatException($"the nameplate set's preset {name} has a {slot} that is none: {item}");
                    look[slot] = item;
                }
                if (look["band"] == null) throw new FormatException($"the nameplate set's preset {name} has no band");
                if (look["effect"] != null && layers["effectOverflow"]?.Type == JTokenType.Boolean && (bool)layers["effectOverflow"]) look["effectOverflow"] = true;
                (kind == "ai" ? aiTitles : titles)[name] = look;
            }
            catalog["titles"] = titles;
            catalog["aiTitles"] = aiTitles;
            return (catalog, files);
        }

        // The set the site names, its files where the object cache keeps them.
        public static NameplateSet SetOf(JObject catalog, Dictionary<string, string> files, string cacheRoot)
        {
            var paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files) paths[file.Key] = ObjectCache.PathOf(cacheRoot, file.Value);
            return new NameplateSet(catalog, paths);
        }

        // Draws plates from what an earlier launch kept, when no set is in use yet. Never throws.
        public static void UseKept(string cacheRoot)
        {
            Keep(cacheRoot, from.site ?? ServerConfig.Site);
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

        // Asks the site what there is (at `api`, where its API answers: the site itself unless said),
        // downloads the files the cache lacks from the root `site` names and draws plates from it; a
        // file that cannot be had is left out (its slot is drawn empty) and tried again next time.
        // When the site cannot be asked, what was kept goes on being used. Never throws but for
        // cancellation.
        public static async Task RefreshAsync(string cacheRoot, string site = ServerConfig.Site, CancellationToken cancel = default, string api = null)
        {
            Keep(cacheRoot, site);
            UseKept(cacheRoot);
            try
            {
                string address = (api ?? site).TrimEnd('/');
                var asked = (set: TextAsync(address + SetPath, cancel), parts: TextAsync(address + PartsPath, cancel));
                string text = Kept(await asked.set, await asked.parts);
                var (catalog, files) = Read(text);
                var root = await Distribution.RootAsync(site, cancel);
                var wanted = await Task.Run(() =>
                {
                    // By hash: two ids of one picture are one file.
                    var missing = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var file in files)
                        if (!missing.ContainsKey(file.Value) && !ObjectCache.Matches(ObjectCache.PathOf(cacheRoot, file.Value), file.Value)) missing[file.Value] = file.Key;
                    return new List<KeyValuePair<string, string>>(missing);
                }, cancel);
                int failed = 0;
                if (wanted.Count > 0)
                {
                    // Not disposed: a cancelled check leaves the others to finish and give their slots back.
                    var slots = new SemaphoreSlim(Parallel);
                    async Task<bool> One(KeyValuePair<string, string> file)
                    {
                        await slots.WaitAsync(cancel);
                        try
                        {
                            string hash = file.Key;
                            var data = await Distribution.FetchAsync(root.Locate(PathOf(hash)),
                                url => ObjectCache.FetchAsync(url, hash, MostBytes, null, cancel), cancel);
                            await Task.Run(() => ObjectCache.Store(ObjectCache.PathOf(cacheRoot, hash), data), cancel);
                            return true;
                        }
                        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            Log.Warn("Nameplate", $"The nameplate item {file.Value} could not be downloaded: {error.Message}");
                            return false;
                        }
                        finally { slots.Release(); }
                    }
                    var tasks = new List<Task<bool>>();
                    foreach (var file in wanted) tasks.Add(One(file));
                    foreach (bool done in await Task.WhenAll(tasks))
                        if (!done) failed++;
                }
                // The catalog is checked here, before the answers are kept for the next launch. The
                // same again are drawn anew only when files they lacked have come.
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
