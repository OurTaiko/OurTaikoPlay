using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongLibraryPathsTests
    {
        string directory, first, second, defaultRoot;

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Application.temporaryCachePath, "song-paths-" + Guid.NewGuid());
            first = Path.Combine(directory, "曲库 A");
            second = Path.Combine(directory, "Library B");
            defaultRoot = Path.Combine(directory, "Default");
            foreach (string root in new[] { first, second, defaultRoot }) Directory.CreateDirectory(root);
        }

        [TearDown] public void Cleanup() => Directory.Delete(directory, true);

        static void WriteChart(string root, string relative, string title)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"TITLE:{title}\nWAVE:song.ogg\nCOURSE:Oni\nLEVEL:5\n#START\n1,\n#END\n");
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path), "song.ogg"), new byte[] { 1 });
        }

        [Test]
        public void StringArraysRoundTripAndOldSettingsKeepDefaultWithoutAddingMenuItems()
        {
            var settings = new GameSettings();
            settings.general.songLibraryPaths = new[] { first, second };
            var loaded = GameSettings.FromJson(settings.ToJson());
            Assert.That(loaded.general.songLibraryPaths, Is.EqualTo(new[] { first, second }));
            loaded.general.songLibraryPaths[0] = defaultRoot;
            Assert.That(settings.general.songLibraryPaths[0], Is.EqualTo(first));
            foreach (string json in new[] { "{}", "{\"general\":{\"songLibraryPaths\":null}}", "{\"general\":{\"songLibraryPaths\":[]}}" })
                Assert.That(SongLibraryPaths.Resolve(GameSettings.FromJson(json).general.songLibraryPaths, defaultRoot), Is.EqualTo(new[] { defaultRoot }));
            Assert.That(SettingsMenu.Catalog().SelectMany(t => t.Items).Any(i => i.Label.Contains("Path")), Is.False);
        }

        [TestCase("")]
        [TestCase("Songs")]
        [TestCase("./Songs")]
        [TestCase("../Songs")]
        [TestCase("~/Songs")]
        [TestCase("C:Songs")]
        [TestCase(null)]
        public void RelativeAndEmptyEntriesAreRejected(string path)
        {
            Assert.That(SongLibraryPaths.TryNormalize(path, out _), Is.False);
            LogAssert.Expect(LogType.Warning, "Skipping song library path: an absolute folder path is required.");
            Assert.That(SongLibraryPaths.Resolve(new[] { path }, defaultRoot), Is.Empty,
                "An explicit invalid list must not fall back to an unrelated library.");
        }

        [Test]
        public void NormalizesDuplicatesAndRetainsConfiguredOrder()
        {
            Assert.That(SongLibraryPaths.Resolve(new[] { first, first + Path.DirectorySeparatorChar, Path.Combine(first, "."), second }, defaultRoot),
                Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void SameNamedChartsAndFoldersKeepIndependentStableIdentitiesAndAudio()
        {
            foreach (string root in new[] { first, second })
            {
                WriteChart(root, "same.tja", Path.GetFileName(root));
                WriteChart(root, "Pack/same.tja", "Pack " + Path.GetFileName(root));
                File.WriteAllText(Path.Combine(root, "Pack/box.def"), "#TITLE:Pack");
            }
            var catalog = LocalSongScanner.ScanRoots(new[] { first, second }, defaultRoot);
            Assert.That(catalog.Songs.Count, Is.EqualTo(2));
            Assert.That(catalog.Folders.Count, Is.EqualTo(2));
            Assert.That(catalog.Songs.Select(s => s.Key), Is.Unique);
            Assert.That(catalog.Folders.Select(f => f.Key), Is.Unique);
            Assert.That(catalog.Folders.SelectMany(f => f.Charts).Select(s => s.Key), Is.Unique);
            Assert.That(catalog.Songs.Select(s => s.AudioPath), Is.EqualTo(new[] { Path.Combine(first, "song.ogg"), Path.Combine(second, "song.ogg") }));
            var reversed = LocalSongScanner.ScanRoots(new[] { second, first }, defaultRoot);
            Assert.That(reversed.Songs.Select(s => s.Key), Is.EqualTo(catalog.Songs.Select(s => s.Key).Reverse()));
            Assert.That(reversed.Folders.Select(f => f.Key), Is.EqualTo(catalog.Folders.Select(f => f.Key).Reverse()));
        }

        [Test]
        public void OverlappingRootsDoNotDuplicateChartsAndDefaultEntryKeysStayCompatible()
        {
            WriteChart(defaultRoot, "Pack/same.tja", "Default song");
            string child = Path.Combine(defaultRoot, "Pack");
            var catalog = LocalSongScanner.ScanRoots(new[] { child, defaultRoot, child }, defaultRoot);
            Assert.That(catalog.Songs.Single().Key, Is.EqualTo("Pack/same"));
            Assert.That(LocalSongScanner.ScanRoots(new[] { defaultRoot, child }, defaultRoot).Songs.Single().Key, Is.EqualTo("Pack/same"));
            WriteChart(first, "Pack/same.tja", "Custom song");
            child = Path.Combine(first, "Pack");
            var single = LocalSongScanner.ScanRoots(new[] { first }, defaultRoot);
            Assert.That(LocalSongScanner.ScanRoots(new[] { child, first }, defaultRoot).Songs.Single().Key, Is.EqualTo(single.Songs.Single().Key));
        }

        [Test]
        public void MissingDirectoryAndFileDoNotStopOtherLibrariesOrGetCreated()
        {
            string missing = Path.Combine(directory, "missing");
            string file = Path.Combine(directory, "not-a-folder");
            File.WriteAllText(file, "test");
            WriteChart(second, "song.tja", "Available");
            LogAssert.Expect(LogType.Warning, new Regex("Skipping song library .*missing:"));
            LogAssert.Expect(LogType.Warning, new Regex("Skipping song library .*not-a-folder:"));
            var catalog = LocalSongScanner.ScanRoots(new[] { missing, file, second }, defaultRoot);
            Assert.That(catalog.Songs.Single().Info.Title, Is.EqualTo("Available"));
            Assert.That(Directory.Exists(missing), Is.False);
        }
    }
}
