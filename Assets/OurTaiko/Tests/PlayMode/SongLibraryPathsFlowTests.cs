using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongLibraryPathsFlowTests
    {
        string directory, originalFile;
        GameSettings originalSettings;
        ScoreStore originalScores;

        // The user's default Songs folder is always present; isolate assertions to this
        // fixture's audio paths without modifying or relying on the contents of that folder.
        SongDefinition[] CustomSongs(LocalSongLibrary library) => library.Songs
            .Where(s => s.audioPath != null && s.audioPath.StartsWith(directory + Path.DirectorySeparatorChar, System.StringComparison.Ordinal)).ToArray();

        [SetUp]
        public void Setup()
        {
            var manager = SettingManager.EnsureInstance();
            originalFile = manager.FilePath;
            originalSettings = manager.Settings.Clone();
            directory = Path.Combine(Application.temporaryCachePath, "library-flow-" + System.Guid.NewGuid());
            Directory.CreateDirectory(directory);
            originalScores = ScoreStore.Shared;
            ScoreStore.Shared = new ScoreStore(Path.Combine(directory, "scores.sqlite3"));
        }

        [TearDown]
        public void Cleanup()
        {
            var manager = SettingManager.EnsureInstance();
            if (originalFile != null) manager.Load(originalFile);
            else manager.UseUnsaved(originalSettings);
            // Finish any scan before removing the temporary library.
            _ = LocalSongLibrary.EnsureInstance().Songs;
            ScoreStore.Shared = originalScores;
            Directory.Delete(directory, true);
        }

        [UnityTest]
        public IEnumerator IdenticalFilesShareScoresAcrossMovesWhileChangedFilesStartFresh()
        {
            string first = Path.Combine(directory, "First"), second = Path.Combine(directory, "Second");
            Directory.CreateDirectory(first); Directory.CreateDirectory(second);
            string file = Path.Combine(first, "original.tja");
            File.WriteAllText(file, "TITLE:Hash Test\nBPM:120\nWAVE:song.ogg\nCOURSE:Oni\nLEVEL:1\n#START\n1,\n#END\n");
            File.WriteAllBytes(Path.Combine(first, "song.ogg"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(second, "song.ogg"), new byte[] { 2 });
            File.Copy(file, Path.Combine(second, "copy.tja"));
            var configured = new GameSettings();
            configured.general.songLibraryPaths = new[] { first, second };
            SettingManager.EnsureInstance().UseUnsaved(configured);
            var library = LocalSongLibrary.EnsureInstance();
            var songs = CustomSongs(library);
            Assert.That(songs.Length, Is.EqualTo(2));
            Assert.That(songs[0], Is.Not.SameAs(songs[1]), "Each entry keeps its own audio path.");
            Assert.That(songs[0].audioPath, Is.Not.EqualTo(songs[1].audioPath));
            string key = songs[0].ScoreKey;
            Assert.That(key, Does.Match("^[a-f0-9]{64}$"));
            Assert.That(songs[1].ScoreKey, Is.EqualTo(key));
            ScoreStore.Shared.Save(new PlayResult { ChartKey = key, Difficulty = Difficulty.Oni, Score = 123456, Good = 1 });
            Assert.That(SongScores.Get(songs[1], Difficulty.Oni).score, Is.EqualTo(123456));
            Assert.That(SongScores.Get(songs[1], Difficulty.Hard), Is.Null);
            File.Move(file, Path.Combine(first, "renamed.tja"));
            library.Refresh();
            Assert.That(CustomSongs(library).Select(s => s.ScoreKey), Is.All.EqualTo(key));
            File.AppendAllText(Path.Combine(first, "renamed.tja"), "// changed\n");
            library.Refresh();
            var changed = CustomSongs(library).First();
            Assert.That(changed.ScoreKey, Is.Not.EqualTo(key));
            Assert.That(SongScores.Get(changed, Difficulty.Oni), Is.Null);
            Assert.That(SongScores.Get(CustomSongs(library).Last(), Difficulty.Oni).score, Is.EqualTo(123456));
            Assert.That(new ScoreStore(Path.Combine(directory, "scores.sqlite3")).Get(key, Difficulty.Oni).score, Is.EqualTo(123456));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FileConfigurationLoadsMultipleRootsAndSurvivesOtherSettingsChangesAndRestart()
        {
            string first = Path.Combine(directory, "First"), second = Path.Combine(directory, "Second");
            foreach (string root in new[] { first, second })
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "same.tja"), $"TITLE:{Path.GetFileName(root)}\nWAVE:song.ogg\nCOURSE:Oni\nLEVEL:5\n#START\n1,\n#END\n");
                File.WriteAllBytes(Path.Combine(root, "song.ogg"), new byte[] { 1 });
            }
            string file = Path.Combine(directory, "settings.json");
            var configured = new GameSettings();
            configured.general.songLibraryPaths = new[] { first, second };
            File.WriteAllText(file, configured.ToJson());
            var manager = SettingManager.EnsureInstance();
            manager.Load(file);
            var library = LocalSongLibrary.EnsureInstance();
            Assert.That(library.Roots, Is.EqualTo(new[] { LocalSongLibrary.DefaultRoot, first, second }));
            Assert.That(CustomSongs(library).Length, Is.EqualTo(2));
            var keys = CustomSongs(library).Select(s => s.name).ToArray();
            Assert.That(keys, Is.Unique);
            var menu = new SettingsMenu(SettingsMenu.Catalog(), manager.Settings);
            menu.Settings.display.targetFrameRate = 60;
            manager.Set(menu.Settings);
            Assert.That(GameSettings.FromJson(File.ReadAllText(file)).general.songLibraryPaths, Is.EqualTo(new[] { first, second }));
            Object.Destroy(library.gameObject);
            yield return null;
            library = LocalSongLibrary.EnsureInstance();
            Assert.That(library.Roots, Is.EqualTo(new[] { LocalSongLibrary.DefaultRoot, first, second }));
            Assert.That(CustomSongs(library).Select(s => s.name), Is.EqualTo(keys));
            configured.general.songLibraryPaths = new[] { second };
            File.WriteAllText(file, configured.ToJson());
            manager.Load(file);
            Assert.That(library.Roots, Is.EqualTo(new[] { LocalSongLibrary.DefaultRoot, second }));
            Assert.That(CustomSongs(library).Single().ReadDisplayInfo().Title, Is.EqualTo("Second"));
            Assert.That(CustomSongs(library).Single().name, Is.EqualTo(keys[1]));
        }
    }
}
