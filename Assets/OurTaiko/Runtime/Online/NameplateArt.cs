using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OurTaiko
{
    // The nameplate's art (Nijiiro global/nameplate and the game's own 段位 rings and texts), which the
    // game does not carry: it is read from the public bucket's nameplate/ folder (uploaded by
    // OurTaikoNet tools/nameplate) in every sign-in check, as the 3D Don's files are
    // (DonDownloads), into the shared object cache (objects/<sha256>, checked by size and hash).
    // Both the catalog and the files come from the player's distribution root, or the canonical
    // one when that fails (Online.Distribution). A file is never replaced in place (a new one has
    // a new path), so its address carries nothing to defeat a cache.
    // The catalog last read is remembered (nameplate-art.json in the cache), so the art is there
    // offline once it was fetched. Until then a nameplate is its text alone.
    public static class NameplateArt
    {
        // The folder under the distribution root.
        public const string Folder = "nameplate/";
        public const string StateFile = "nameplate-art.json";
        // How long each request (the catalog, one picture) may take.
        static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        // The catalog's keys (OurTaikoNet tools/nameplate/catalog.py) and each file's size in pixels.
        static readonly (string key, int width, int height)[] files =
        {
            ("shadow", 408, 96), ("outline", 408, 96), ("bands", 408, 280), ("rainbow", 408, 336),
            ("danBackground", 160, 48), ("danRingSilver", 72, 40), ("danRingGold", 72, 40),
            ("danRingRainbow", 72, 40), ("danText", 360, 200), ("danTextGold", 360, 200),
            ("badge1p", 88, 88), ("badge2p", 88, 88),
        };

        public static bool Ready { get; private set; }
        public static Sprite Shadow { get; private set; }
        public static Sprite Outline { get; private set; }
        public static Sprite DanBackground { get; private set; }
        public static Sprite Badge1P { get; private set; }
        public static Sprite Badge2P { get; private set; }
        // frame_top's 5 title bands (PlayerInfo.TitleFrame), top to bottom.
        public static Sprite[] Bands { get; private set; } = Array.Empty<Sprite>();
        // frame_top_rainbow's 6 frames, top to bottom.
        public static Sprite[] Rainbow { get; private set; } = Array.Empty<Sprite>();
        // dan_ring_{silver,gold,rainbow}: PlayerInfo.DanComboTier 0..2.
        public static Sprite[] DanRings { get; private set; } = Array.Empty<Sprite>();
        // dan_text / dan_text_gold: 0 初級 .. 24 達人, 72x40 cells, 5 to a row (0..5 are blank).
        public static Sprite[] DanTexts { get; private set; } = Array.Empty<Sprite>();
        public static Sprite[] GoldDanTexts { get; private set; } = Array.Empty<Sprite>();

        // The art arrived (or was replaced): plates on screen draw it.
        public static event Action Changed;

        static readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
        static string loaded = ""; // the hashes of the files the sprites were made from

        // Each Play session starts without art, as a player does (the Editor here keeps statics
        // between Play sessions while the textures made in one are destroyed when it stops).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            made.Clear();
            loaded = "";
            Clear();
            Changed = null;
        }

        // Drops the art (plates on screen go back to their text).
        public static void Forget()
        {
            foreach (var old in made) Destroy(old);
            made.Clear();
            loaded = "";
            Clear();
            Changed?.Invoke();
        }

        static void Clear()
        {
            Ready = false;
            Shadow = Outline = DanBackground = Badge1P = Badge2P = null;
            Bands = Rainbow = DanRings = DanTexts = GoldDanTexts = Array.Empty<Sprite>();
        }

        // Reads the catalog and downloads the files it names that the cache lacks, from the
        // distribution root `site` names; on failure the remembered catalog is used when its files
        // are cached. Never throws but for cancellation.
        public static async Task FetchAsync(string cacheRoot, CancellationToken cancel = default, string site = Online.ServerConfig.Site)
        {
            try
            {
                var root = await Online.Distribution.RootAsync(site, cancel);
                var catalog = await Online.Distribution.FetchAsync(root.Locate(Folder + "catalog.json"), async url =>
                {
                    using var response = await Online.OurTaikoHttpClient.GetAsync(url, RequestTimeout, cancel);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                    return JObject.Parse(await response.Content.ReadAsStringAsync());
                }, cancel);
                var entries = Entries(catalog);
                foreach (var (key, path, size, hash) in entries.Values)
                {
                    string cached = ObjectPath(cacheRoot, hash);
                    if (await Task.Run(() => Matches(cached, size, hash), cancel)) continue;
                    var data = await Online.Distribution.FetchAsync(root.Locate(Folder + path), async url =>
                    {
                        using var response = await Online.OurTaikoHttpClient.GetAsync(url, RequestTimeout, cancel);
                        if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        if (bytes.LongLength != size || Sha256(bytes) != hash) throw new InvalidDataException("the download of " + path + " does not match its hash");
                        return bytes;
                    }, cancel);
                    await Task.Run(() => Store(cached, data), cancel);
                }
                Load(entries, cacheRoot);
                Remember(cacheRoot, catalog);
                return;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception error) { Log.Warn("Nameplate", "Could not fetch the nameplate art: " + error.Message); }

            if (Ready) return;
            try
            {
                string state = Path.Combine(cacheRoot, StateFile);
                if (!File.Exists(state)) return;
                var entries = Entries(JObject.Parse(File.ReadAllText(state)));
                foreach (var (_, _, size, hash) in entries.Values)
                    if (!Matches(ObjectPath(cacheRoot, hash), size, hash)) return;
                Load(entries, cacheRoot);
            }
            catch (Exception error) { Log.Warn("Nameplate", "The cached nameplate art is unusable: " + error.Message); }
        }

        // Every file the game needs, from {"files": {key: {path, size, sha256}}}; each path is
        // under the nameplate folder.
        static Dictionary<string, (string key, string path, long size, string hash)> Entries(JObject catalog)
        {
            var listed = catalog["files"] as JObject ?? throw new InvalidDataException("the catalog lists no files");
            var entries = new Dictionary<string, (string, string, long, string)>();
            foreach (var (key, _, _) in files)
            {
                if (!(listed[key] is JObject f)) throw new InvalidDataException("the catalog lacks " + key);
                string path = (string)f["path"], hash = (string)f["sha256"];
                if (!Online.Distribution.ValidPath(Folder + path) || hash == null || hash.Length != 64)
                    throw new InvalidDataException("bad catalog entry " + key);
                entries[key] = (key, path, (long?)f["size"] ?? -1, hash.ToLowerInvariant());
            }
            return entries;
        }

        static void Load(Dictionary<string, (string key, string path, long size, string hash)> entries, string cacheRoot)
        {
            string signature = "";
            foreach (var (key, _, _, hash) in entries.Values) signature += key + ":" + hash + ";";
            if (Ready && signature == loaded) return;
            var images = new Dictionary<string, byte[]>();
            foreach (var (key, _, _, hash) in entries.Values) images[key] = File.ReadAllBytes(ObjectPath(cacheRoot, hash));
            Use(images);
            loaded = signature;
        }

        // Makes the sprites from the PNGs by catalog key; a file of the wrong size leaves the art unset.
        public static void Use(IReadOnlyDictionary<string, byte[]> images)
        {
            var textures = new Dictionary<string, Texture2D>();
            try
            {
                foreach (var (key, width, height) in files)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Nameplate " + key };
                    textures[key] = texture;
                    if (!images.TryGetValue(key, out var png) || !texture.LoadImage(png, true) || texture.width != width || texture.height != height)
                        throw new InvalidDataException("nameplate " + key + " is not a " + width + "x" + height + " PNG");
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                }
            }
            catch
            {
                foreach (var t in textures.Values) Destroy(t);
                throw;
            }
            Use(textures);
        }

        // Plain textures of the right sizes, for tests that need plates drawn without the bucket.
        public static void UsePlaceholders()
        {
            var textures = new Dictionary<string, Texture2D>();
            foreach (var (key, width, height) in files)
                textures[key] = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Nameplate " + key };
            Use(textures);
        }

        static void Use(Dictionary<string, Texture2D> t)
        {
            foreach (var old in made) Destroy(old);
            made.Clear();
            loaded = "";
            made.AddRange(t.Values);

            Sprite Whole(string key) => Piece(t[key], 0, 0, t[key].width, t[key].height);
            // Row `row` from the top, `column` from the left, of cells w x h (texture y runs upwards).
            Sprite Cell(string key, int column, int row, int w, int h) => Piece(t[key], column * w, t[key].height - (row + 1) * h, w, h);
            Sprite[] Rows(string key, int count) { var s = new Sprite[count]; for (int i = 0; i < count; i++) s[i] = Cell(key, 0, i, 408, 56); return s; }
            Sprite[] Grid(string key) { var s = new Sprite[PlayerInfo.DanCount]; for (int i = 0; i < s.Length; i++) s[i] = Cell(key, i % 5, i / 5, 72, 40); return s; }

            Shadow = Whole("shadow");
            Outline = Whole("outline");
            DanBackground = Whole("danBackground");
            Badge1P = Whole("badge1p");
            Badge2P = Whole("badge2p");
            Bands = Rows("bands", PlayerInfo.TitleBackgroundCount);
            Rainbow = Rows("rainbow", 6);
            DanRings = new[] { Whole("danRingSilver"), Whole("danRingGold"), Whole("danRingRainbow") };
            DanTexts = Grid("danText");
            GoldDanTexts = Grid("danTextGold");
            Ready = true;
            Changed?.Invoke();
        }

        static Sprite Piece(Texture2D texture, int x, int y, int w, int h)
        {
            var sprite = Sprite.Create(texture, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            made.Add(sprite);
            return sprite;
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // ---- the object cache (shared with the 3D Don and the songs: objects/<first two>/<sha256>) ----

        static string ObjectPath(string cacheRoot, string hash) => Path.Combine(cacheRoot, "objects", hash.Substring(0, 2), hash);

        static string Sha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        static bool Matches(string path, long size, string hash)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
            return Sha256(File.ReadAllBytes(path)) == hash;
        }

        static void Store(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (IOException) when (File.Exists(path)) { }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static void Remember(string cacheRoot, JObject catalog)
        {
            try
            {
                Directory.CreateDirectory(cacheRoot);
                File.WriteAllText(Path.Combine(cacheRoot, StateFile), catalog.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception error) { Log.Warn("Nameplate", "Could not remember the nameplate art: " + error.Message); }
        }
    }
}
