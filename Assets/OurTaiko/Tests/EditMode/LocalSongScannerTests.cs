using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class LocalSongScannerTests
    {
        string root;

        [SetUp]
        public void CreateRoot()
        {
            root = Path.Combine(Application.temporaryCachePath, "scanner-songs");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void DeleteRoot() => Directory.Delete(root, true);

        static string Chart(string title, string course = "Oni", string wave = null) =>
            $"TITLE:{title}\n" + (wave != null ? $"WAVE:{wave}\n" : "") + $"COURSE:{course}\nLEVEL:5\n#START\n1,\n#END\n";

        void Write(string relative, string text, Encoding encoding = null)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, (encoding ?? new UTF8Encoding(false)).GetBytes(text));
        }

        [Test]
        public void RootChartsSortByFileNameAndPlainFoldersListInline()
        {
            Write("b.tja", Chart("B"));
            Write("A.TJA", Chart("A"));
            Write("Pack/Song/c.tja", Chart("C"));
            Write("notes.txt", Chart("Ignored"));
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Songs.Select(s => s.Key), Is.EqualTo(new[] { "A", "b", "Pack/Song/c" }));
            Assert.That(catalog.Folders, Is.Empty);
        }

        [Test]
        public void BoxDefFoldersHoldEveryChartBelowSortedByTitle()
        {
            Write("Anime/box.def", "#TITLE:Anime Songs\n#TITLEJA:アニメ\n#GENRE:アニメ\n");
            Write("Anime/z.tja", Chart("Alpha"));
            Write("Anime/Deep/a.tja", Chart("Zulu"));
            Write("Outer/Inner/box.def", "#TITLE:Inner\n");
            Write("Outer/x.tja", Chart("X"));
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Songs, Is.Empty);
            Assert.That(catalog.Folders.Select(f => (f.Key, f.Title, f.Genre)),
                Is.EqualTo(new[] { ("local/Anime", "Anime Songs", "アニメ"), ("local/Outer", "Outer", "") }));
            Assert.That(catalog.Folders[0].Charts.Select(c => c.Info.Title), Is.EqualTo(new[] { "Alpha", "Zulu" }));
            Assert.That(catalog.Folders[1].Charts.Single().Key, Is.EqualTo("Outer/x"));
            Assert.That(LocalSongScanner.Scan(root, "ja").Folders[0].Title, Is.EqualTo("アニメ"));
        }

        [Test]
        public void SkipsChartsWithoutAPlayableCourseAndResolvesWave()
        {
            Write("Song/song.tja", Chart("Song", wave: "song.ogg"));
            File.WriteAllBytes(Path.Combine(root, "Song", "song.ogg"), new byte[] { 1 });
            Write("missing.tja", Chart("Missing", wave: "none.ogg"));
            Write("unknown.tja", Chart("Unknown", course: "Tower"));
            Write("empty.tja", "");
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Songs.Select(s => s.Key), Is.EqualTo(new[] { "missing", "Song/song" }));
            Assert.That(catalog.Songs[1].AudioPath, Is.EqualTo(Path.Combine(Path.GetFullPath(root), "Song", "song.ogg")));
            Assert.That(catalog.Songs[0].AudioPath, Is.Null);
        }

        [Test]
        public void ReadsBomEncodingsAndShiftJis()
        {
            string text = Chart("太鼓の達人");
            Assert.That(LocalSongScanner.ReadText(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()), Is.EqualTo(text));
            Assert.That(LocalSongScanner.ReadText(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()), Is.EqualTo(text));
            Assert.That(LocalSongScanner.ReadText(Encoding.UTF8.GetBytes(text)), Is.EqualTo(text));
            Encoding shiftJis;
            try { shiftJis = Encoding.GetEncoding(932); }
            catch (System.Exception) { Assert.Ignore("This runtime has no Shift-JIS encoding."); return; }
            Assert.That(LocalSongScanner.ReadText(shiftJis.GetBytes(text)), Is.EqualTo(text));
        }

        [Test]
        public void MissingRootIsEmpty()
        {
            var catalog = LocalSongScanner.Scan(Path.Combine(root, "nowhere"));
            Assert.That(catalog.Songs, Is.Empty);
            Assert.That(catalog.Folders, Is.Empty);
        }
    }
}
