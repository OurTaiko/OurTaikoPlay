using System.Linq;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The test charts (TRIPLE HELIX with its music, the silent Input Calibration and Branch Training).
    // The game ships no local songs: TestData.Use lists these as SongSelect's local songs, and tests
    // that load a play scene directly Select one first.
    public static class TestSongs
    {
        public const string Folder = "Assets/OurTaiko/Tests/Shared/Songs/";
        public static readonly string[] Names = { "TripleHelix", "Calibration", "BranchTraining" };

        public static SongDefinition Load(string name)
        {
#if UNITY_EDITOR
            var song = UnityEditor.AssetDatabase.LoadAssetAtPath<SongDefinition>(Folder + name + ".asset");
            if (song == null) throw new System.IO.FileNotFoundException("Missing test song " + name, Folder + name + ".asset");
            return song;
#else
            throw new System.NotSupportedException("Test songs load through the AssetDatabase.");
#endif
        }

        public static SongDefinition[] All => Names.Select(Load).ToArray();

        // What a direct run of SinglePlayScene used to play: TRIPLE HELIX on its own course, not auto.
        public static SongDefinition Select(string name = "TripleHelix", string course = null, bool autoPlay = false)
        {
            var song = Load(name);
            SceneSwitcher.EnsureInstance().Select(song, course, autoPlay);
            return song;
        }
    }
}
