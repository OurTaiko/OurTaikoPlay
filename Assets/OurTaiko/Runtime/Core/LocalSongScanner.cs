using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OurTaiko
{
    // One playable TJA found on disk. Key is its path under the songs root without the extension
    // ('/' separated); it identifies the library entry. Sha256 keys scores independently
    // of the entry's location and is calculated from the file's original bytes.
    public sealed class LocalChart
    {
        public string Key, FilePath, Text, AudioPath, Sha256;
        public SongInfo Info;
    }

    public sealed class LocalFolder
    {
        public string Key, Title, Genre = "";
        public readonly List<LocalChart> Charts = new List<LocalChart>();
    }

    public sealed class LocalSongCatalog
    {
        public readonly List<LocalChart> Songs = new List<LocalChart>();
        public readonly List<LocalFolder> Folders = new List<LocalFolder>();
    }

    // The songs folder as OurTaikoPlayer reads tja_path (filesystem.cpp get_song_files, navigator.cpp
    // load_current_directory / parse_box_def), flattened to the one folder level song select has:
    // a top-level directory holding a box.def (itself or below) is a folder with every chart beneath
    // it, sorted by title; charts anywhere else list at the root, sorted by file name. Only .tja is
    // read (no .osu/.osz/fumen/song_list.txt), and a chart needs at least one Easy–Edit course.
    public static class LocalSongScanner
    {
        const int MaxDepth = 32;  // Stands in for the canonical-path loop check on symlinked folders.

        // Merge configured roots in order. Absolute file identities distinguish same-named
        // charts and stay stable when roots are reordered or overlap. The default library
        // retains its original relative entry keys. Scores use the separate content hash.
        public static LocalSongCatalog ScanRoots(string[] roots, string defaultRoot, string language = "en")
        {
            var combined = new LocalSongCatalog();
            var seen = new HashSet<string>(SongLibraryPaths.Comparer);
            SongLibraryPaths.TryNormalize(defaultRoot, out string legacyRoot);
            foreach (string root in roots)
            {
                try
                {
                    // Check each root independently; one unavailable drive must not hide others.
                    using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) entries.MoveNext();
                    var catalog = Scan(root, language);
                    bool Keep(LocalChart chart)
                    {
                        if (!seen.Add(chart.FilePath)) return false;
                        chart.Key = LibraryKey(chart.FilePath, legacyRoot, false);
                        return true;
                    }
                    combined.Songs.AddRange(catalog.Songs.Where(Keep));
                    foreach (var folder in catalog.Folders)
                    {
                        string relative = folder.Key.Substring("local/".Length);
                        folder.Key = LibraryKey(Path.Combine(root, relative), legacyRoot, true);
                        folder.Charts.RemoveAll(chart => !Keep(chart));
                        if (folder.Charts.Count > 0) combined.Folders.Add(folder);
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                    error is System.Security.SecurityException)
                {
                    UnityEngine.Debug.LogWarning($"Skipping song library {root}: {error.Message}");
                }
            }
            return combined;
        }

        static string LibraryKey(string path, string legacyRoot, bool folder)
        {
            string prefix = legacyRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (path.StartsWith(prefix, comparison))
            {
                string relative = Relative(legacyRoot, path);
                return folder ? "local/" + relative : relative.Substring(0, relative.Length - Path.GetExtension(relative).Length);
            }
            if (Path.DirectorySeparatorChar == '\\') path = path.ToUpperInvariant();
            using var hash = SHA256.Create();
            return "local-path/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        public static LocalSongCatalog Scan(string root, string language = "en")
        {
            var catalog = new LocalSongCatalog();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return catalog;
            root = Path.GetFullPath(root);
            foreach (string file in Files(root)) Add(catalog.Songs, root, file);
            foreach (string dir in Directories(root))
            {
                if (!HasDefFile(dir, 0))
                {
                    foreach (string file in ChartFiles(dir)) Add(catalog.Songs, root, file);
                    continue;
                }
                var folder = ReadBoxDef(dir, language);
                folder.Key = "local/" + Relative(root, dir);
                foreach (string file in ChartFiles(dir)) Add(folder.Charts, root, file);
                folder.Charts.Sort((a, b) => AlphaCompare(a.Info.Title, b.Info.Title));
                catalog.Folders.Add(folder);
            }
            catalog.Songs.Sort((a, b) =>
            {
                int byName = AlphaCompare(Path.GetFileName(a.FilePath), Path.GetFileName(b.FilePath));
                return byName != 0 ? byName : string.CompareOrdinal(a.Key, b.Key);
            });
            catalog.Folders.Sort((a, b) => AlphaCompare(a.Key, b.Key));
            return catalog;
        }

        static void Add(List<LocalChart> list, string root, string file)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                string text = ReadText(bytes);
                var info = SongInfo.Read(text);
                if (info.Courses.Count == 0) return;
                string audio = string.IsNullOrWhiteSpace(info.Wave) ? null : Path.Combine(Path.GetDirectoryName(file), info.Wave.Trim());
                string relative = Relative(root, file);
                using var hash = SHA256.Create();
                list.Add(new LocalChart
                {
                    Key = relative.Substring(0, relative.Length - Path.GetExtension(relative).Length),
                    Sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                    FilePath = file, Text = text, Info = info,
                    AudioPath = audio != null && File.Exists(audio) ? audio : null,
                });
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException)
            {
                UnityEngine.Debug.LogWarning($"Skipping song {file}: {error.Message}");
            }
        }

        // tja.cpp test_encodings: a BOM decides; without one the file is UTF-8 when it decodes
        // cleanly, otherwise Shift-JIS (code page 932) where the platform has it.
        public static string ReadText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { }
            try { return Encoding.GetEncoding(932).GetString(bytes); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException) { }
            return new UTF8Encoding(false).GetString(bytes);
        }

        // navigator.cpp parse_box_def: #TITLE (a #TITLE<LANG> line wins) and #GENRE, else #COLLECTION.
        static LocalFolder ReadBoxDef(string dir, string language)
        {
            var folder = new LocalFolder { Title = Path.GetFileName(dir) };
            string file = Path.Combine(dir, "box.def");
            if (!File.Exists(file)) return folder;
            string localized = "#TITLE" + (language ?? "en").ToUpperInvariant() + ":";
            bool titleLocalized = false;
            string text;
            try { text = ReadText(File.ReadAllBytes(file)); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return folder; }
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#GENRE:", StringComparison.Ordinal)) folder.Genre = line.Substring(7).Trim();
                else if (line.StartsWith("#COLLECTION:", StringComparison.Ordinal)) folder.Genre = line.Substring(12).Trim();
                else if (line.StartsWith(localized, StringComparison.Ordinal)) { folder.Title = line.Substring(localized.Length).Trim(); titleLocalized = true; }
                else if (line.StartsWith("#TITLE:", StringComparison.Ordinal) && !titleLocalized) folder.Title = line.Substring(7).Trim();
            }
            return folder;
        }

        static bool HasDefFile(string dir, int depth)
        {
            if (File.Exists(Path.Combine(dir, "box.def"))) return true;
            return depth < MaxDepth && Directories(dir).Any(child => HasDefFile(child, depth + 1));
        }

        static IEnumerable<string> ChartFiles(string dir, int depth = 0)
        {
            foreach (string file in Files(dir)) yield return file;
            if (depth >= MaxDepth) yield break;
            foreach (string child in Directories(dir))
                foreach (string file in ChartFiles(child, depth + 1)) yield return file;
        }

        static IEnumerable<string> Files(string dir)
        {
            try
            {
                return Directory.GetFiles(dir)
                    .Where(f => string.Equals(Path.GetExtension(f), ".tja", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        static IEnumerable<string> Directories(string dir)
        {
            try { return Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.Ordinal).ToArray(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        static string Relative(string root, string path) =>
            path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');

        // navigator.cpp alpha_less: per character, case-insensitive, then shorter first.
        public static int AlphaCompare(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                char ca = char.ToLowerInvariant(a[i]), cb = char.ToLowerInvariant(b[i]);
                if (ca != cb) return ca.CompareTo(cb);
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
