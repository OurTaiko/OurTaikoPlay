using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurTaiko.Online;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Builds the published play backdrops' asset bundles: every backdrop file imported for a
    // platform, one bundle each, as StageDownloads fetches them. Written as they lie under the
    // distribution root's stage/ folder:
    //   stage/catalog.json                              the published catalog (Tools/stage-publish/catalog.json)
    //   stage/bundle/<version>/<platform>/index.json    each bundle's name ("header/miku"), file, size and SHA-256
    //   stage/bundle/<version>/<platform>/<part>-<name>.<hash>
    // A published file is never replaced (the distribution's mirror keeps the first copy it reads,
    // and a game refuses a file whose hash is not the index's), so a bundle's name carries the
    // start of its own SHA-256 and only the JSON says which one is current. One bundle a file, so a
    // part several presets share is fetched once.
    //
    // The files are the sets the game carries and the rest, which are not in the repository:
    // Fetch brings them from the root into Sources. Tools/stage-publish/README.md says how a set
    // is published; the "Stage bundles" workflow runs BuildBatch for each platform.
    public static class StageBundles
    {
        public const string Version = StageLibrary.BundleVersion;
        public const string Carried = "Assets/OurTaiko/Resources/Stage";
        public const string Sources = "Assets/OurTaiko/StageSource";
        // Every set and preset there is; the game's own catalog (Carried's) lists only what it carries.
        public const string Catalog = "Tools/stage-publish/catalog.json";
        // How much of a file's SHA-256 its published name carries.
        public const int HashLength = 16;

        // The platform's name in the files' addresses; null for one the game is not built for.
        public static string PlatformName(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 => "windows",
            BuildTarget.StandaloneOSX => "macos",
            BuildTarget.StandaloneLinux64 => "linux",
            BuildTarget.Android => "android",
            BuildTarget.iOS => "ios",
            _ => null,
        };

        public static BuildTarget? TargetNamed(string platform)
        {
            foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
                if (PlatformName(target) == platform) return target;
            return null;
        }

        // Every backdrop file in the project, as (part, name, asset path), in order.
        public static List<(string part, string name, string path)> Files()
        {
            var files = new List<(string part, string name, string path)>();
            foreach (string root in new[] { Carried, Sources })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.GetFiles(root, "*.otanim", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
                {
                    string relative = path.Substring(root.Length + 1);
                    int slash = relative.LastIndexOf('/');
                    // Parts are the folders directly under the root.
                    if (slash <= 0 || relative.IndexOf('/') != slash) throw new InvalidDataException(path + " is not in a part's folder under " + root);
                    files.Add((relative.Substring(0, slash), Path.GetFileNameWithoutExtension(relative), path));
                }
            }
            files.Sort((a, b) => string.CompareOrdinal(a.part + "/" + a.name, b.part + "/" + b.name));
            return files;
        }

        // The sets the published catalog lists that the project has no file for: published without
        // one, a set could never be drawn.
        public static List<string> Missing()
        {
            var catalog = StageCatalogFile.Read(File.ReadAllText(Catalog));
            try
            {
                var here = new HashSet<string>(Files().Select(f => f.part + "/" + f.name));
                return catalog.Sets().Where(s => !here.Contains(s.folder + "/" + catalog.MainFile(s.folder, s.set))).Select(s => s.folder + "/" + s.set).ToList();
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        // Downloads the published files the game does not carry into Sources, from the ones
        // <root>/otanim/index.json lists (each checked against its size and SHA-256). One already
        // here with that hash is left. Returns what it did, for the log.
        public static string Fetch(string root)
        {
            root = root.TrimEnd('/');
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var listed = JsonUtility.FromJson<StageDownloads.Index>(http.GetStringAsync(root + "/otanim/index.json").GetAwaiter().GetResult());
            if (listed?.files == null || listed.files.Length == 0) throw new InvalidDataException(root + "/otanim/index.json lists no files");
            int fetched = 0, kept = 0, carried = 0;
            foreach (var entry in listed.files)
            {
                string[] name = (entry.name ?? "").Split('/');
                if (name.Length != 2 || !Distribution.ValidPath(entry.name) || !Distribution.ValidPath("otanim/" + entry.file) || !ObjectCache.ValidHash(entry.sha256))
                    throw new InvalidDataException("The index has an entry that cannot be used: " + entry.name);
                if (File.Exists(Carried + "/" + entry.name + ".otanim")) { carried++; continue; }
                string to = Sources + "/" + entry.name + ".otanim";
                if (File.Exists(to) && ObjectCache.Sha256(File.ReadAllBytes(to)) == entry.sha256) { kept++; continue; }
                byte[] bytes = http.GetByteArrayAsync(root + "/otanim/" + entry.file).GetAwaiter().GetResult();
                if (bytes.LongLength != entry.size || ObjectCache.Sha256(bytes) != entry.sha256) throw new InvalidDataException(entry.file + " does not match its hash");
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, bytes);
                fetched++;
            }
            AssetDatabase.Refresh();
            return $"Stage sources from {root}: {fetched} fetched, {kept} already here, {carried} carried by the game";
        }

        // A batch run's setting: the value after `argument` on the command line (a CI's builder
        // passes arguments, not its environment), else the environment variable.
        static string Setting(string argument, string variable)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, argument);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable(variable);
        }

        // -stageBundleDir <folder> (or STAGE_BUNDLE_DIR) is where to write. -stageSourceRoot <address>
        // (or STAGE_SOURCE_ROOT), a published folder's address, fetches the sources from there
        // first, as a machine without them does. The bundles are for the active build target.
        public static void BuildBatch()
        {
            string dir = Setting("-stageBundleDir", "STAGE_BUNDLE_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Pass -stageBundleDir <folder>, or set STAGE_BUNDLE_DIR");
            string from = Setting("-stageSourceRoot", "STAGE_SOURCE_ROOT");
            if (!string.IsNullOrWhiteSpace(from)) Debug.Log(Fetch(from));
            BuildAll(dir, EditorUserBuildSettings.activeBuildTarget);
        }

        [MenuItem("OurTaiko/Build/Stage Bundles (this platform, for a trial)")]
        static void BuildMenu()
        {
            string dir = EditorUtility.SaveFolderPanel("Stage bundles (the distribution root's layout is written under it)", "", "");
            if (!string.IsNullOrEmpty(dir)) BuildAll(dir, EditorUserBuildSettings.activeBuildTarget);
        }

        // Puts every published set on this machine as a game that had fetched them all has them:
        // built for the platform the Editor runs on, into StageLibrary.Downloads with the catalog.
        // For the preset viewer and the tests of sets the game does not carry.
        [MenuItem("OurTaiko/Build/Install Stage Bundles Here")]
        public static void InstallHere()
        {
            var target = TargetNamed(StageLibrary.Platform) ?? throw new InvalidOperationException("No bundles are built for this platform");
            string dir = Path.Combine(Application.temporaryCachePath, "stage-install-" + Guid.NewGuid().ToString("N"));
            try
            {
                var index = BuildAll(dir, target);
                string from = Path.Combine(dir, "stage", "bundle", Version, PlatformName(target));
                StageLibrary.Forget();
                StageDownloads.Forget();
                foreach (var entry in index.files)
                {
                    string[] name = entry.name.Split('/');
                    string to = StageLibrary.DownloadedFile(name[0], name[1]);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(Path.Combine(from, entry.file), to, true);
                }
                // What StageDownloads keeps beside them: the catalog, and which version of each file is here.
                File.Copy(Catalog, StageLibrary.DownloadedCatalog, true);
                File.WriteAllText(Path.Combine(StageLibrary.Downloads, StageDownloads.HereFile), JsonUtility.ToJson(index, true));
                Debug.Log($"{index.files.Length} stage bundles installed in {StageLibrary.Downloads}");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // The catalog and one platform's bundles. Nothing is written unless every set the catalog
        // lists has its file.
        public static StageDownloads.Index BuildAll(string dir, BuildTarget target)
        {
            var missing = Missing();
            if (missing.Count > 0) throw new InvalidOperationException("The published catalog lists sets the project has no file for: " + string.Join(", ", missing));
            Directory.CreateDirectory(Path.Combine(dir, "stage"));
            File.Copy(Catalog, Path.Combine(dir, "stage", "catalog.json"), true);
            return Build(dir, target);
        }

        // Every one of the files is imported with its picture block-compressed, whatever its
        // import settings said: a published backdrop is as light on a player's memory as the
        // game's own. Returns how many had to be imported again.
        public static int Compress(IEnumerable<(string part, string name, string path)> files)
        {
            int changed = 0;
            foreach (var (_, _, path) in files)
            {
                if (!(AssetImporter.GetAtPath(path) is OtanimImporter importer)) throw new InvalidDataException(path + " is not imported as an otanim");
                if (importer.compress) continue;
                importer.compress = true;
                // Without this the setting is not written and the import reads the old one back.
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }

        // One platform's bundles and their index, under dir/stage/bundle/<Version>/<platform>.
        // files: only these (tests build a few); null for all of them.
        public static StageDownloads.Index Build(string dir, BuildTarget target, IReadOnlyList<(string part, string name, string path)> files = null)
        {
            string platform = PlatformName(target) ?? throw new InvalidOperationException("The game is not built for " + target);
            files ??= Files();
            Compress(files);
            string output = Path.Combine(dir, "stage", "bundle", Version, platform);
            // Left-over bundles of files since removed would stay in the index's folder.
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            // Unity lower-cases a bundle's name; ours are lower case already.
            var builds = files.Select(f => new AssetBundleBuild { assetBundleName = f.part + "-" + f.name, assetNames = new[] { f.path } }).ToArray();
            // LZ4 in chunks: a fetched bundle is read where it lies, without unpacking it whole.
            var manifest = BuildPipeline.BuildAssetBundles(output, builds, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) throw new InvalidOperationException("Building the stage bundles for " + target + " failed; see the log.");
            // Unity's own manifests are for its dependency loader; a backdrop's bundle stands alone.
            foreach (string extra in Directory.GetFiles(output, "*.manifest")) File.Delete(extra);
            File.Delete(Path.Combine(output, platform));
            var entries = new List<StageDownloads.Entry>();
            foreach (var (part, name, _) in files)
            {
                string built = Path.Combine(output, part + "-" + name);
                if (!File.Exists(built)) throw new InvalidOperationException("No bundle was written for " + part + "/" + name);
                byte[] bytes = File.ReadAllBytes(built);
                string sha = ObjectCache.Sha256(bytes), file = part + "-" + name + "." + sha.Substring(0, HashLength);
                File.Move(built, Path.Combine(output, file));
                entries.Add(new StageDownloads.Entry { name = part + "/" + name, file = file, size = bytes.Length, sha256 = sha });
            }
            var index = new StageDownloads.Index { files = entries.ToArray() };
            File.WriteAllText(Path.Combine(output, StageDownloads.IndexFile), JsonUtility.ToJson(index, true));
            Debug.Log($"Stage bundles for {platform}: {entries.Count} bundles, {entries.Sum(e => e.size) / 1e6:0.0} MB, in {output}");
            return index;
        }
    }
}
