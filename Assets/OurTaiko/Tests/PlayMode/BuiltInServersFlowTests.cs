using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The game connects to its two built-in servers only: no servers.json, and no login screen
    // between Entry and song select.
    public sealed class BuiltInServersFlowTests
    {
        string folder;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            // The servers.json test loads no scene: the one left by the last tear-down stays.
            if (previous.name != "BuiltInServersTearDown")
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("BuiltInServersTearDown"));
                yield return SceneManager.UnloadSceneAsync(previous);
            }
            TestData.UseServers();
            TestSongs.Install();
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        static string[] Urls(OnlineManager online) => online.BuiltInServers.Select(server => server.baseUrl).ToArray();

        // A manager starting in a data folder leaves no servers.json there, and one a player still
        // has from an older version is neither read nor changed.
        [UnityTest]
        public IEnumerator TheGameNeitherCreatesNorReadsServersJson()
        {
            using var fixture = new FanmadeFixture();
            folder = Path.Combine(Application.temporaryCachePath, "built-in-servers-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "servers.json");
            string old = "{\"servers\":[{\"enabled\":true,\"name\":\"Mine\",\"baseUrl\":\"" + fixture.BaseUrl
                + "\",\"username\":\"don\",\"password\":\"katsu\",\"httpProxy\":\"\",\"autoLogin\":true}]}";
            string[] builtIn = { "https://fanmade.ourtaiko.org", "https://database.ourtaiko.org" };
            // GamePaths has no seam of its own: the data folder is swapped for this test only.
            var root = typeof(GamePaths).GetField("root", BindingFlags.NonPublic | BindingFlags.Static);
            string real = GamePaths.Root;
            Object.DestroyImmediate(OnlineManager.Instance.gameObject);
            root.SetValue(null, folder);
            try
            {
                var online = OnlineManager.EnsureInstance();
                Assert.That(Urls(online), Is.EqualTo(builtIn));
                Assert.That(File.Exists(file), Is.False, "No servers.json is created.");
                Object.DestroyImmediate(online.gameObject);

                File.WriteAllText(file, old);
                online = OnlineManager.EnsureInstance();
                Assert.That(Urls(online), Is.EqualTo(builtIn), "A server listed in an old servers.json is not connected to.");
                Assert.That(online.Client.Endpoints, Is.Empty);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(File.ReadAllText(file), Is.EqualTo(old), "The player's old file is left as it is.");
                Assert.That(Directory.GetFiles(folder, "servers.json*"), Has.Length.EqualTo(1));
                Assert.That(fixture.Requests, Is.Empty);
                Object.DestroyImmediate(online.gameObject);
            }
            finally
            {
                if (OnlineManager.Instance != null) Object.DestroyImmediate(OnlineManager.Instance.gameObject);
                root.SetValue(null, real);
                OnlineManager.EnsureInstance();
            }
        }

        // 演奏ゲーム leaves Entry straight for song select; on the way every folder closes and the
        // songs folder is read again, so a song added since the last visit is listed.
        [UnityTest]
        public IEnumerator EntryGoesStraightToSongSelectAndListsSongsAddedSince()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded("ServerLogin"), Is.False, "The login scene is not in the build.");
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            folder = Path.Combine(Application.temporaryCachePath, "built-in-songs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            static string Chart(string title) => $"TITLE:{title}\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1,\n#END\n";
            File.WriteAllText(Path.Combine(folder, "before.tja"), Chart("Before"));
            LocalSongLibrary.EnsureInstance().UseRoot(folder);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var manager = Object.FindAnyObjectByType<SongSelectScene>().Manager;
            Assert.That(manager.Folders[0].SongCount, Is.EqualTo(1));
            manager.OpenFolderAt(0);
            Assert.That(manager.OpenFolderKey, Is.EqualTo(SongSelectManager.LocalKey));

            Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            File.WriteAllText(Path.Combine(folder, "after.tja"), Chart("After"));
            var entry = Object.FindAnyObjectByType<EntryScene>();
            Assert.That(entry.view.boards[0].scene, Is.EqualTo(SceneSwitcher.SongSelectScene));
            Assert.That(entry.view.boards.Select(board => board.scene), Has.None.EqualTo("ServerLogin"));
            yield return Waits.Until(() => entry.Flow.CanJoin(entry.Now) && !SceneSwitcher.Instance.IsInputBlocked);
            entry.Don();
            yield return Waits.Until(() => entry.Flow.IsModeReady(entry.Now));
            Assert.That(entry.Flow.SelectedMode, Is.Zero);
            entry.Don();
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.SongSelectScene && !SceneSwitcher.Instance.IsInputBlocked);
            Assert.That(SceneSwitcher.LastScene, Is.EqualTo(SceneSwitcher.EntryScene), "No scene between Entry and song select.");
            manager = Object.FindAnyObjectByType<SongSelectScene>().Manager;
            Assert.That(manager.OpenFolder, Is.Null, "Every folder is closed again.");
            Assert.That(manager.Folders[0].SongCount, Is.EqualTo(2), "The songs folder was read again.");
        }
    }
}
