using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ItemKind = OurTaiko.SongSelectManager.ItemKind;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // OpenTaiko's song list: the Local source folder holds the box.def folders, a folder inside another
    // opens inline inside it, only the innermost open folder shows もどる boards, もどる and Back close
    // the folder the focus is in, and a folder reopens where it was left.
    public sealed class NestedFolderFlowTests
    {
        string root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("NestedFolderTearDown"));
            yield return SceneManager.UnloadSceneAsync(previous);
            TestData.UseServers(new ServerList());
            TestSongs.Install();
            if (root != null) Directory.Delete(root, true);
        }

        void Write(string relative, string text)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        static string Chart(string title, string genre = "") =>
            $"TITLE:{title}\nGENRE:{genre}\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1,\n#END\n";

        [UnityTest]
        public IEnumerator SubFoldersOpenInsideTheirFolderAndCloseFromTheInside()
        {
            TestData.UseServers(new ServerList());
            root = Path.Combine(Application.temporaryCachePath, "nested-songs-" + Guid.NewGuid().ToString("N"));
            Write("root.tja", Chart("Root song"));
            Write("Pack/box.def", "#TITLE:Pack\n#GENRE:GAME\n");
            for (int i = 1; i <= 8; i++) Write($"Pack/p{i}.tja", Chart("Pack " + i));
            Write("Pack/Sub/box.def", "#TITLE:Sub\n");
            Write("Pack/Sub/s.tja", Chart("Sub song", "ANIME"));
            LocalSongLibrary.EnsureInstance().UseRoot(root);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var manager = Object.FindFirstObjectByType<SongSelectScene>().Manager;

            // Only the Local folder is on the wheel at first; it holds the root song and Pack.
            Assert.That(manager.Folders.Select(f => f.Title), Is.EqualTo(new[] { SongSelectManager.LocalTitle, "Pack", "Sub" }));
            Assert.That(manager.Folders[0].SongCount, Is.EqualTo(10));
            Assert.That(manager.Folders[1].SongCount, Is.EqualTo(9), "A folder counts the songs in its sub-folders.");
            Assert.That(manager.Items.Select(i => i.Kind), Is.EqualTo(new[] { ItemKind.Folder, ItemKind.Search, ItemKind.Back }));

            // Local: もどる, its own search, then Pack before the root song.
            manager.OpenFolderAt(0);
            yield return null;
            Assert.That(manager.OpenFolder, Is.EqualTo(SongSelectManager.LocalKey));
            Assert.That(manager.Items.Select(i => i.Kind), Is.EqualTo(new[] { ItemKind.Back, ItemKind.Search, ItemKind.Folder, ItemKind.Song, ItemKind.Search }));
            Assert.That(manager.Items[1].Folder, Is.EqualTo(0), "The search inside Local searches Local only.");

            // Pack: もどる, the sub-folder before the songs, and もどる again before the seventh board after
            // it. Local's own もどる leaves the wheel while Pack is open inside it.
            var localBack = manager.Items[0];
            manager.OpenFolderAt(2);
            yield return null;
            Assert.That(manager.OpenFolder, Is.EqualTo("local/Pack"));
            Assert.That(manager.Focused, Is.EqualTo(1));
            Assert.That(manager.Items.Select(i => i.Kind).Take(12), Is.EqualTo(new[]
            {
                ItemKind.Search, ItemKind.Back, ItemKind.Folder, ItemKind.Song, ItemKind.Song, ItemKind.Song, ItemKind.Song,
                ItemKind.Song, ItemKind.Song, ItemKind.Back, ItemKind.Song, ItemKind.Song,
            }));
            Assert.That(manager.Items.Where(i => i.Kind == ItemKind.Back).Select(i => i.Folder), Is.EqualTo(new[] { 1, 1 }), "Only Pack, the innermost, has もどる.");
            Assert.That(manager.SongAt(12).ReadDisplayInfo().Title, Is.EqualTo("Root song"));
            Assert.That(manager.KindAt(13), Is.EqualTo(ItemKind.Search));
            Assert.That(manager.Items.Any(i => i.Kind == ItemKind.Back && i.Folder < 0), Is.False);
            Assert.That(manager.Items[3].Genre, Is.EqualTo(3), "Pack's songs wear its GAME genre.");
            Assert.That(manager.Items, Has.No.Member(localBack));

            // Sub opens inline inside Pack; its song keeps its own genre, Sub having none. Pack's もどる
            // boards leave the wheel in turn, so a single もどる shows however deep the folders nest.
            var packBack = manager.Items[1];
            manager.OpenFolderAt(2);
            yield return null;
            Assert.That(manager.OpenFolder, Is.EqualTo("local/Pack/Sub"));
            Assert.That(manager.Focused, Is.EqualTo(1));
            Assert.That(manager.Items.Select(i => i.Kind).Take(4), Is.EqualTo(new[] { ItemKind.Search, ItemKind.Back, ItemKind.Song, ItemKind.Song }));
            Assert.That(manager.Items.Count(i => i.Kind == ItemKind.Back), Is.EqualTo(1));
            Assert.That(manager.Items[1].Folder, Is.EqualTo(2), "The one もどる is Sub's.");
            Assert.That(manager.Items, Has.No.Member(packBack));
            var subSong = manager.SongAt(2);
            Assert.That(subSong.ReadDisplayInfo().Title, Is.EqualTo("Sub song"));
            Assert.That(manager.Items[2].Genre, Is.EqualTo(2));
            Assert.That(manager.SongAt(3).ReadDisplayInfo().Title, Is.EqualTo("Pack 1"));
            Assert.That(manager.Items.Count, Is.EqualTo(13), "Local's search, Sub's もどる and song, Pack's 8 songs, the root song and the root search.");

            // Closing from Sub's song closes Sub only and focuses its board; Pack's もどる boards return
            // (new items: the view gets them as Added). Reopening returns to the song.
            manager.Navigate(1);
            var changes = new List<SongSelectManager.FolderChange>();
            manager.FolderChanged += changes.Add;
            manager.CloseFolder();
            yield return null;
            Assert.That(manager.OpenFolder, Is.EqualTo("local/Pack"));
            Assert.That(manager.KindAt(2), Is.EqualTo(ItemKind.Folder));
            Assert.That(manager.Focused, Is.EqualTo(2));
            Assert.That(manager.Items.Where(i => i.Kind == ItemKind.Back).Select(i => i.Folder), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Removed.Select(i => i.Kind), Is.EqualTo(new[] { ItemKind.Back, ItemKind.Song }), "Sub's もどる and song leave.");
            Assert.That(changes[0].Added.Select(i => i.Kind), Is.EqualTo(new[] { ItemKind.Back, ItemKind.Folder, ItemKind.Back }), "Pack's two もどる and Sub's board come back.");
            Assert.That(changes[0].Anchor.Kind, Is.EqualTo(ItemKind.Back));
            Assert.That(changes[0].Added, Has.No.Member(manager.Items[0]).And.No.Member(manager.SongAt(3)), "Boards that stay keep their items.");
            manager.FolderChanged -= changes.Add;
            manager.OpenFolderAt(2);
            yield return null;
            Assert.That(manager.FocusedSong, Is.SameAs(subSong));

            // From a board in Local itself, もどる closes Local with every folder open inside it.
            manager.Navigate(-2);
            Assert.That(manager.FocusedKind, Is.EqualTo(ItemKind.Search));
            manager.CloseFolder();
            yield return null;
            Assert.That(manager.OpenFolder, Is.Null);
            Assert.That(manager.Items.Select(i => i.Kind), Is.EqualTo(new[] { ItemKind.Folder, ItemKind.Search, ItemKind.Back }));
            Assert.That(manager.Focused, Is.EqualTo(0));

            // Coming back from a song reopens the folders around it, on the song, with Sub's もどる alone.
            manager.OpenFolderAt(0);
            manager.OpenFolderAt(2);
            manager.OpenFolderAt(2);
            SceneSwitcher.EnsureInstance().Select(subSong);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            manager = Object.FindFirstObjectByType<SongSelectScene>().Manager;
            Assert.That(manager.OpenFolder, Is.EqualTo("local/Pack/Sub"));
            Assert.That(manager.Items.Select(i => i.Kind).Take(3), Is.EqualTo(new[] { ItemKind.Search, ItemKind.Back, ItemKind.Song }));
            Assert.That(manager.Items.Count(i => i.Kind == ItemKind.Back), Is.EqualTo(1));
            Assert.That(manager.FocusedSong, Is.SameAs(subSong));
        }
    }
}
