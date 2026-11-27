using System;
using System.Collections.Generic;
using System.IO;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // The layers a nameplate can wear (NameplateSlots) and the plate's own pictures: the few the
    // game carries itself (Resources/Nameplate/<slot>/<name>: the rank-up effect, which is the
    // plate's own and no title's part), the items of the set in use (NameplateSet: the titles'
    // parts and the plate's composition) and the player's own .otanim files in the Nameplate
    // folder beside their songs, Nameplate/<slot>/<name>.otanim. Each is read when a plate first
    // wears it, and a player's
    // file again when it changes. An item draws on the whole plate canvas: its view must be
    // 0, 0, Width, Height (NameplateLayout), y up, and it plays its "idle" clip (else its first)
    // over and over. A file that cannot be read, or is drawn for another box, is said in the log
    // and not worn: the plate is drawn as if the slot were empty.
    public static class NameplateItems
    {
        public const string FolderName = "Nameplate", Extension = ".otanim", ClipName = "idle";
        // Where the items the game carries are, under Resources.
        public const string ResourceFolder = "Nameplate/";

        static string folder;
        static NameplateSet set;
        static PlateComposition.Catalog catalog;
        // The player's files read, by path, with the time each was written (null: it could not be used).
        static readonly Dictionary<string, (OtanimAsset asset, DateTime stamp)> loaded = new Dictionary<string, (OtanimAsset, DateTime)>();
        // The set's files read, by path. Kept while the game runs, as Resources kept assets: plates
        // draw them (the layers' mask is cut from one), and a downloaded file's path is its
        // contents' hash, so a later set naming it names the same picture.
        static readonly Dictionary<string, (OtanimAsset asset, DateTime stamp)> fromSet = new Dictionary<string, (OtanimAsset, DateTime)>();
        // Earlier reads of the player's files that changed: plates on screen may still draw them
        // until they are shown again, so they are freed with the rest.
        static readonly List<OtanimAsset> replaced = new List<OtanimAsset>();

        // The set the plates are drawn from and its catalog; null until one is used (a first
        // launch that has not downloaded it yet): a plate is then its text alone.
        public static NameplateSet Set => set;
        public static PlateComposition.Catalog Catalog => catalog;
        // Another set is used: plates on screen are drawn again from it.
        public static event Action SetChanged;

        // The set and what was read of it stay from one Editor Play session to the next (domain
        // reload is off): they hold no scene objects, and a launch starts from the set last
        // downloaded too.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Forget();
            folder = null;
            SetChanged = null;
        }

        public static string Folder => folder ??= Path.Combine(GamePaths.Root, FolderName);

        // Another folder as the player's (tests, the Editor's preview).
        public static void UseFolder(string path)
        {
            Forget();
            folder = path;
        }

        // Plates are drawn from `next` from now on (null: none). Throws InvalidDataException when its
        // catalog cannot be read; the set in use then stays.
        public static void Use(NameplateSet next)
        {
            PlateComposition.Catalog read = null;
            if (next != null)
            {
                try { read = new PlateComposition.Catalog(next.Catalog); }
                catch (Exception error) when (error is Newtonsoft.Json.JsonException || error is ArgumentException || error is InvalidCastException || error is FormatException)
                {
                    throw new InvalidDataException("the nameplate catalog cannot be read: " + error.Message, error);
                }
            }
            set = next;
            catalog = read;
            SetChanged?.Invoke();
        }

        // Frees what was read of the player's files; the next use reads them again.
        public static void Forget()
        {
            foreach (var entry in loaded.Values) Release(entry.asset);
            loaded.Clear();
            foreach (var asset in replaced) Release(asset);
            replaced.Clear();
        }

        public static string PathOf(NameplateSlot slot, string name) => Path.Combine(Folder, NameplateSlots.Folder(slot), name + Extension);

        // The names of the slot's files, in order.
        public static List<string> Names(NameplateSlot slot)
        {
            var names = new List<string>();
            string dir = Path.Combine(Folder, NameplateSlots.Folder(slot));
            if (catalog != null)
            {
                foreach (var id in catalog.Ids(slot))
                    if (NameplateSlots.TryParse(id, out _, out string name)) names.Add(name);
                if (slot == NameplateSlot.Icon && NameplateSlots.TryParse(PlateComposition.RankUpItem, out _, out string rankUp)) names.Add(rankUp);
            }
            try
            {
                foreach (string path in Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + Extension) : Array.Empty<string>())
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (NameplateSlots.IsName(name) && !names.Contains(name)) names.Add(name);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warn("Nameplate", $"Could not list {dir}: {error.Message}.");
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // The item an id names, if it is in the slot it is worn in and can be drawn: the game's own,
        // else the set's, else the player's file of that name; else null.
        public static OtanimAsset Load(NameplateSlot slot, string id)
        {
            if (!NameplateSlots.TryParse(id, out var named, out string name) || named != slot) return null;
            return Carried(slot, name) ?? LoadPath(set?.PathOf(id), name, fromSet) ?? LoadPath(PathOf(slot, name), name, loaded);
        }

        // An item the game carries, which needs no set and is Unity's to keep and free.
        static OtanimAsset Carried(NameplateSlot slot, string name) =>
            Resources.Load<OtanimAsset>(ResourceFolder + NameplateSlots.Folder(slot) + "/" + name);

        // One of the plate's own pictures (composition/<name>: plates, badges, blank, dan-*).
        public static OtanimAsset CompositionPart(string name) =>
            NameplateSlots.IsName(name) ? LoadPath(set?.PathOf("composition/" + name), name, fromSet) : null;

        // `name` names what is read (a downloaded file's own name is its hash).
        static OtanimAsset LoadPath(string path, string name, Dictionary<string, (OtanimAsset asset, DateTime stamp)> read)
        {
            if (path == null) return null;
            DateTime stamp;
            try
            {
                if (!File.Exists(path)) return null;
                stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return null; }
            if (read.TryGetValue(path, out var known))
            {
                if (known.stamp == stamp) return known.asset;
                // A set's file written again (the baked folder, baked anew) is left to whoever draws it.
                if (known.asset != null && read == loaded) replaced.Add(known.asset);
                read.Remove(path);
            }
            var asset = Read(path, name);
            read[path] = (asset, stamp);
            return asset;
        }

        // The clip a worn item plays.
        public static Otanim.Clip ClipOf(Otanim file) =>
            file == null || file.Clips.Length == 0 ? null : file.Find(ClipName) ?? file.Clips[0];

        // The box every item draws in: the whole plate canvas.
        public static readonly Rect View = new Rect(0, 0, NameplateLayout.Width, NameplateLayout.Height);

        public static bool Fits(Otanim file)
        {
            const float slack = 0.01f;
            var v = file.View;
            return Mathf.Abs(v.xMin - View.xMin) < slack && Mathf.Abs(v.yMin - View.yMin) < slack
                && Mathf.Abs(v.width - View.width) < slack && Mathf.Abs(v.height - View.height) < slack;
        }

        static OtanimAsset Read(string path, string name)
        {
            OtanimAsset asset = null;
            try
            {
                asset = OtanimAsset.FromBytes(File.ReadAllBytes(path), name);
                var file = asset.File;
                if (!Fits(file))
                    throw new InvalidDataException($"its view is {file.View.xMin} {file.View.yMin} {file.View.xMax} {file.View.yMax}, not the plate's 0 0 {View.width} {View.height}");
                if (file.Clips.Length == 0) throw new InvalidDataException("it has no clip");
                return asset;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException || error is InvalidDataException
                || error is ArgumentException || error is IndexOutOfRangeException || error is OverflowException)
            {
                Release(asset);
                Log.Warn("Nameplate", $"The nameplate layer {path} is not used: {error.Message}.");
                return null;
            }
        }

        static void Release(OtanimAsset asset)
        {
            if (asset == null) return;
            Destroy(asset.texture);
            Destroy(asset);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
