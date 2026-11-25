using System;
using System.Collections.Generic;
using System.IO;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // The layers a nameplate can wear (NameplateSlots): .otanim files in the player's Nameplate
    // folder beside their songs, Nameplate/<slot>/<name>.otanim, read when a plate first wears one
    // and again when the file changes. Each draws on the whole plate canvas: its view must be
    // 0, 0, Width, Height (NameplateLayout), y up, and it plays its "idle" clip (else its first)
    // over and over. A file that cannot be read, or is drawn for another box, is said in the log
    // and not worn: the plate is drawn as if the slot were empty.
    public static class NameplateItems
    {
        public const string FolderName = "Nameplate", Extension = ".otanim", ClipName = "idle";

        static string folder;
        // The files read, by path, with the time each was written (null: it could not be used).
        static readonly Dictionary<string, (OtanimAsset asset, DateTime stamp)> loaded = new Dictionary<string, (OtanimAsset, DateTime)>();
        // Earlier reads of files that changed: plates on screen may still draw them until they are
        // shown again, so they are freed with the rest.
        static readonly List<OtanimAsset> replaced = new List<OtanimAsset>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Forget();
            folder = null;
        }

        public static string Folder => folder ??= Path.Combine(GamePaths.Root, FolderName);

        // Another folder as the player's (tests, the Editor's preview).
        public static void UseFolder(string path)
        {
            Forget();
            folder = path;
        }

        // Frees what was read; the next use reads the files again.
        public static void Forget()
        {
            foreach (var entry in loaded.Values) Release(entry.asset);
            loaded.Clear();
            PlateComposition.ForgetCatalog();
            foreach (var asset in replaced) Release(asset);
            replaced.Clear();
        }

        public static string PathOf(NameplateSlot slot, string name) => Path.Combine(Folder, NameplateSlots.Folder(slot), name + Extension);

        // The names of the slot's files, in order.
        public static List<string> Names(NameplateSlot slot)
        {
            var names = new List<string>();
            string dir = Path.Combine(Folder, NameplateSlots.Folder(slot));
            if (slot == NameplateSlot.Band)
            {
                var catalog = PlateComposition.ReadCatalog();
                foreach (var id in catalog.BandIds)
                    if (NameplateSlots.TryParse(id, out _, out string name)) names.Add(name);
                names.Add("lumen-ai-3");
            }
            if (slot == NameplateSlot.Icon) names.Add("lumen-rankup");
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

        // The item an id names, if it is in the slot it is worn in and can be drawn; else null.
        public static OtanimAsset Load(NameplateSlot slot, string id)
        {
            if (!NameplateSlots.TryParse(id, out var named, out string name) || named != slot) return null;
            string path = PathOf(slot, name);
            return Resources.Load<OtanimAsset>(FolderName + "/" + id) ?? LoadPath(path);
        }

        internal static OtanimAsset CompositionPart(string name) =>
            NameplateSlots.IsName(name) ? Resources.Load<OtanimAsset>(FolderName + "/composition/" + name) : null;

        static OtanimAsset LoadPath(string path)
        {
            DateTime stamp;
            try
            {
                if (!File.Exists(path)) return null;
                stamp = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return null; }
            if (loaded.TryGetValue(path, out var known))
            {
                if (known.stamp == stamp) return known.asset;
                if (known.asset != null) replaced.Add(known.asset);
                loaded.Remove(path);
            }
            var asset = Read(path);
            loaded[path] = (asset, stamp);
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

        static OtanimAsset Read(string path)
        {
            OtanimAsset asset = null;
            try
            {
                asset = OtanimAsset.FromBytes(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
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
