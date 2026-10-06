using System.IO;
using System.Linq;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The test charts (TRIPLE HELIX with its music, the silent Input Calibration and Branch Training)
    // live in Songs~, which Unity does not import. TestData.Use copies them to a temporary songs
    // folder that LocalSongLibrary reads like the player's own; the number prefixes keep the file
    // name order TRIPLE HELIX, Calibration, Branch Training. Tests that load a play scene directly
    // Select a song first.
    public static class TestSongs
    {
        public const string TripleHelix = "1 TripleHelix", Calibration = "2 Calibration", BranchTraining = "3 BranchTraining";

        public static string Source => Path.Combine(Application.dataPath, "OurTaiko", "Tests", "Shared", "Songs~");
        public static string Root => Path.Combine(Application.temporaryCachePath, "playmode-songs");
        public static string ChartPath(string key) => Path.Combine(Root, key + ".tja");

        // A fresh copy, so tests may edit the charts in Root.
        public static void Install()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            foreach (string file in Directory.GetFiles(Source))
                File.Copy(file, Path.Combine(Root, Path.GetFileName(file)));
            LocalSongLibrary.EnsureInstance().UseRoot(Root);
        }

        public static void Uninstall()
        {
            if (LocalSongLibrary.Instance != null) LocalSongLibrary.Instance.UseRoot(LocalSongLibrary.DefaultRoot);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        public static SongDefinition Load(string key) =>
            LocalSongLibrary.EnsureInstance().Songs.FirstOrDefault(song => song.name == key)
            ?? throw new FileNotFoundException("Missing test song " + key, ChartPath(key));

        // What a direct run of SinglePlayScene used to play: TRIPLE HELIX on its own course, not auto.
        public static SongDefinition Select(string key = TripleHelix, string course = null, bool autoPlay = false)
        {
            var song = Load(key);
            SceneSwitcher.EnsureInstance().Select(song, course, autoPlay);
            return song;
        }
    }
}
