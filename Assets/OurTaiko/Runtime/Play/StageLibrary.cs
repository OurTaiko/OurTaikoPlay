using System;
using System.Collections.Generic;
using System.IO;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // Where the play backdrops' files and their catalog are, in three places laid out the same way
    // (catalog.json and <part>/<name>.otanim), the first that has a file being the one drawn:
    //   the player's own, in the Stage folder beside their songs;
    //   the published ones Online.StageDownloads fetched, in Resources/Stage beside their songs
    //     (each an asset bundle built for this platform, <part>/<name>.bundle, so its picture is
    //     compressed as the game's own are; a player's file is decoded as it is);
    //   the few sets the game carries, under its own Resources/Stage.
    // The catalog is the published one once it was fetched (it lists the game's sets too), else the
    // game's; the player's catalog.json adds its presets and sets to it, one of a name it already
    // has taking that one's place. A file or catalog outside the game that cannot be read is said
    // in the log and the next place's is used: nothing here keeps a play from starting.
    public static class StageLibrary
    {
        public const string FolderName = "Stage", CatalogFile = "catalog.json", ReadMeFile = "README.txt";
        // Names the bundle format in the published files' addresses (Editor.StageBundles builds
        // them). A game reads only its own version's bundles: raise it whenever bundles built from
        // now on could not be read by a game already out (another Unity version's format, a change
        // to OtanimAsset), and keep the old ones published.
        public const string BundleVersion = "1";
        // The game's own catalog, a TextAsset: Resources/Stage/catalog.json.
        public const string BuiltInCatalog = StageCatalog.Root + "catalog";

        static string folder, downloads;
        static StageCatalog catalog;
        static HashSet<string> carried;
        static DateTime catalogStamp, publishedStamp;
        static bool prepared;
        // The files from outside the game that are loaded, by path, with the time each was written.
        static readonly Dictionary<string, (OtanimAsset asset, DateTime stamp)> loaded = new Dictionary<string, (OtanimAsset, DateTime)>();
        // The fetched bundles that are open, by path.
        static readonly Dictionary<string, (AssetBundle bundle, OtanimAsset asset, DateTime stamp)> open = new Dictionary<string, (AssetBundle, OtanimAsset, DateTime)>();

        // The platform's name in the bundles' addresses; null where the game has none.
        public static string Platform
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: return "windows";
                    case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return "macos";
                    case RuntimePlatform.LinuxPlayer: case RuntimePlatform.LinuxEditor: return "linux";
                    case RuntimePlatform.Android: return "android";
                    case RuntimePlatform.IPhonePlayer: return "ios";
                    default: return null;
                }
            }
        }

        // The Editor keeps statics between Play sessions while the textures made in one are
        // destroyed when it stops; each starts from nothing, as a player's does.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Forget();

        // Drops what was read; the next use reads the folder again.
        public static void Forget()
        {
            foreach (var entry in loaded.Values) Release(entry.asset);
            loaded.Clear();
            foreach (var entry in open.Values)
                if (entry.bundle != null) entry.bundle.Unload(true);
            open.Clear();
            if (catalog != null) Destroy(catalog);
            catalog = null;
            carried = null;
            folder = null;
            downloads = null;
            prepared = false;
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        static void Release(OtanimAsset asset)
        {
            if (asset == null) return;
            Destroy(asset.texture);
            Destroy(asset);
        }

        // The player's folder.
        public static string Folder => folder ??= Path.Combine(GamePaths.Root, FolderName);

        // Where the published backdrops are fetched to: Resources/Stage beside the songs.
        public static string Downloads => downloads ??= Path.Combine(GamePaths.Root, "Resources", FolderName);

        // Other folders as the player's and the fetched ones' (tests).
        public static void UseFolder(string path, string fetched = null)
        {
            Forget();
            folder = path;
            downloads = fetched ?? Path.Combine(path, "..", Path.GetFileName(path) + "-fetched");
        }

        static string PlayerFile(string part, string name) => Path.Combine(Folder, part, name + ".otanim");
        public static string DownloadedFile(string part, string name) => Path.Combine(Downloads, part, name + ".bundle");
        public static string DownloadedCatalog => Path.Combine(Downloads, CatalogFile);

        // The file is outside the game: the player's, or a fetched one.
        public static bool HasOutside(string part, string name) => File.Exists(PlayerFile(part, name)) || File.Exists(DownloadedFile(part, name));

        // The game itself carries this set.
        public static bool Carries(string part, string set)
        {
            _ = Catalog;
            return carried.Contains(part + "/" + set);
        }

        // The catalog in use: the game's, with the player's added. Read again when the player's
        // file has changed since, so an edit shows in the next play.
        public static StageCatalog Catalog
        {
            get
            {
                Prepare();
                string path = Path.Combine(Folder, CatalogFile), published = DownloadedCatalog;
                DateTime stamp = Stamp(path), fetched = Stamp(published);
                if (catalog != null && stamp == catalogStamp && fetched == publishedStamp) return catalog;
                if (catalog != null) Destroy(catalog);
                catalogStamp = stamp;
                publishedStamp = fetched;
                catalog = ReadBuiltIn();
                carried = new HashSet<string>();
                foreach (var (part, set) in catalog.Sets()) carried.Add(part + "/" + set);
                // The published catalog lists everything, the game's sets among them.
                if (fetched != default && Outside(published, "published") is StageCatalog all)
                {
                    Destroy(catalog);
                    catalog = all;
                }
                if (stamp != default && Outside(path, "player's") is StageCatalog theirs)
                {
                    StageCatalogFile.Merge(catalog, theirs);
                    Log.Info("Stage", $"The player's catalog adds {theirs.packs.Length} presets from {path}");
                    Destroy(theirs);
                }
                return catalog;
            }
        }

        // A catalog outside the game; null, said in the log, when it cannot be read.
        static StageCatalog Outside(string path, string whose)
        {
            try { return StageCatalogFile.Read(File.ReadAllText(path), warning => Log.Warn("Stage", $"{path}: {warning}.")); }
            catch (Exception error) when (error is FormatException || error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warn("Stage", $"The {whose} catalog {path} is not used: {error.Message}.");
                return null;
            }
        }

        static StageCatalog ReadBuiltIn()
        {
            var text = Resources.Load<TextAsset>(BuiltInCatalog);
            if (text == null) throw new InvalidOperationException("The game has no stage catalog at Resources/" + BuiltInCatalog + ".json");
            try { return StageCatalogFile.Read(text.text); }
            catch (FormatException error) { throw new InvalidOperationException("The game's stage catalog cannot be read: " + error.Message); }
        }

        static DateTime Stamp(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return default; }
        }

        // Made once so players find where their backdrops go, with a note on how.
        static void Prepare()
        {
            if (prepared) return;
            prepared = true;
            try
            {
                Directory.CreateDirectory(Folder);
                string note = Path.Combine(Folder, ReadMeFile);
                if (!File.Exists(note)) File.WriteAllText(note, ReadMe);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warn("Stage", "Could not prepare the stage folder: " + error.Message);
            }
        }

        public const string ReadMe =
            "Play backdrops of your own.\n\n" +
            "Files go in a folder per part, as <part>/<name>.otanim:\n" +
            "  header   the band above the lane (<name>_2p is 2P's)\n" +
            "  scene    the scene below the lane\n" +
            "  clear    the scene past the clear line\n" +
            "  dancers  <name>_0 to <name>_4, or one file <name> for a whole troupe\n" +
            "  footer   the stand in front of the dancers\n" +
            "  crowd    the crowd of a full gauge\n" +
            "  renda    what a drumroll hit throws\n" +
            "  chibi    the characters hits bring out\n\n" +
            "A file with the name of one the game has is drawn in its place.\n\n" +
            "To add sets and presets of your own, write a catalog.json here. The game adds it to its own;\n" +
            "an entry with a name the game already has replaces the game's. Example:\n\n" +
            "{\n" +
            "  \"version\": 1,\n" +
            "  \"presets\": {\n" +
            "    \"MYSTAGE\": { \"header\": \"mine\", \"scene\": \"mine\", \"clear\": \"-\", \"dancers\": \"-\" }\n" +
            "  },\n" +
            "  \"sets\": {\n" +
            "    \"header\": { \"mine\": { \"random\": false } },\n" +
            "    \"scene\":  { \"mine\": { \"random\": false } }\n" +
            "  }\n" +
            "}\n\n" +
            "A chart asks for a preset with SCENEPRESET:MYSTAGE, a folder with #SCENEPRESET:MYSTAGE in its box.def.\n" +
            "In a preset, a part left out is drawn at random and \"-\" leaves it empty. A set is drawn at random for\n" +
            "charts that name no preset unless it says \"random\": false.\n";

        // The set's files are here: the game carries it, or its main file is the player's or was fetched.
        public static bool Has(string part, string set)
        {
            var listed = Catalog;
            return carried.Contains(part + "/" + set) || HasOutside(part, listed.MainFile(part, set));
        }

        // The file's asset: the player's when their folder has one that can be read, else the
        // game's; null when neither has it (said in the log unless quiet, for a file a set may lack).
        public static OtanimAsset Load(string part, string name, bool quiet = false)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Prepare();
            string theirs = PlayerFile(part, name);
            DateTime stamp = Stamp(theirs);
            if (stamp != default)
            {
                if (loaded.TryGetValue(theirs, out var entry) && entry.stamp == stamp && entry.asset != null && entry.asset.texture != null) return entry.asset;
                if (loaded.Remove(theirs, out entry)) Release(entry.asset);
                var read = Read(theirs);
                if (read != null)
                {
                    loaded[theirs] = (read, stamp);
                    return read;
                }
            }
            string fetched = DownloadedFile(part, name);
            stamp = Stamp(fetched);
            if (stamp != default)
            {
                if (open.TryGetValue(fetched, out var had) && had.stamp == stamp && had.asset != null) return had.asset;
                Close(fetched);
                var bundle = AssetBundle.LoadFromFile(fetched);
                var assets = bundle != null ? bundle.LoadAllAssets<OtanimAsset>() : null;
                if (assets != null && assets.Length > 0)
                {
                    open[fetched] = (bundle, assets[0], stamp);
                    return assets[0];
                }
                if (bundle != null) bundle.Unload(true);
                Log.Warn("Stage", $"The fetched backdrop {fetched} is not used: it is not a bundle this game reads.");
            }
            var asset = Resources.Load<OtanimAsset>(StageCatalog.Path(part, name));
            if (asset == null && !quiet) Log.Warn("Stage", $"No backdrop file {part}/{name}.otanim: not the player's, not fetched and not the game's.");
            return asset;
        }

        // A player's file as the asset an imported one is: its animation, and its picture decoded.
        static OtanimAsset Read(string path)
        {
            Texture2D texture = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var file = Otanim.Parse(bytes);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path), hideFlags = HideFlags.DontSave };
                if (file.Texture == null || !texture.LoadImage(file.Texture, true)) throw new InvalidDataException("its picture is not a PNG");
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                var asset = ScriptableObject.CreateInstance<OtanimAsset>();
                asset.name = Path.GetFileNameWithoutExtension(path);
                asset.hideFlags = HideFlags.DontSave;
                asset.bytes = Otanim.WithoutTexture(bytes);
                asset.texture = texture;
                return asset;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is InvalidDataException
                || error is ArgumentException || error is IndexOutOfRangeException || error is OverflowException)
            {
                Destroy(texture);
                Log.Warn("Stage", $"The backdrop {path} is not used: {error.Message}.");
                return null;
            }
        }

        // Frees the files from outside the game that are not among these: a play keeps only its own.
        public static void Keep(IEnumerable<(string part, string name)> used)
        {
            var kept = new HashSet<string>();
            foreach (var (part, name) in used) { kept.Add(PlayerFile(part, name)); kept.Add(DownloadedFile(part, name)); }
            var freed = new List<string>();
            foreach (string path in loaded.Keys)
                if (!kept.Contains(path)) freed.Add(path);
            foreach (string path in freed)
            {
                Release(loaded[path].asset);
                loaded.Remove(path);
            }
            var closed = new List<string>();
            foreach (string path in open.Keys)
                if (!kept.Contains(path)) closed.Add(path);
            foreach (string path in closed) Close(path);
        }

        static void Close(string path)
        {
            if (!open.TryGetValue(path, out var entry)) return;
            if (entry.bundle != null) entry.bundle.Unload(true);
            open.Remove(path);
        }

        public static int LoadedOutsideFiles => loaded.Count + open.Count;
    }
}
