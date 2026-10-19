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
        public void RootChartsSortByPathAndPlainFoldersListInline()
        {
            Write("b.tja", Chart("B"));
            Write("A.TJA", Chart("A"));
            Write("Pack/Song/c.tja", Chart("C"));
            Write("notes.txt", Chart("Ignored"));
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Songs.Select(s => s.Key), Is.EqualTo(new[] { "A", "b", "Pack/Song/c" }));
            Assert.That(catalog.Folders, Is.Empty);
        }

        // OpenTaiko: a box.def directory is a folder read the same way as the root, so folders nest;
        // a directory without one lists its charts and folders where the directory itself is.
        [Test]
        public void BoxDefFoldersNestAndPlainFoldersStayTransparent()
        {
            Write("Anime/box.def", "#TITLE:Anime Songs\n#TITLEJA:アニメ\n#GENRE:アニメ\n");
            Write("Anime/z.tja", Chart("Alpha"));
            Write("Anime/Deep/a.tja", Chart("Zulu"));
            Write("Anime/Sub/box.def", "#TITLE:Sub\n");
            Write("Anime/Sub/s.tja", Chart("S"));
            Write("Anime/Sub/More/box.def", "#GENRE:GAME\n");
            Write("Anime/Sub/More/m.tja", Chart("M"));
            Write("Anime/Plain/Boxed/box.def", "#TITLE:Boxed\n");
            Write("Anime/Plain/Boxed/b.tja", Chart("B"));
            Write("Outer/Inner/box.def", "#TITLE:Inner\n");
            Write("Outer/Inner/i.tja", Chart("I"));
            Write("Outer/x.tja", Chart("X"));
            Write("Empty/box.def", "#TITLE:Empty\n");
            Write("Empty/Nested/box.def", "#TITLE:Nested\n");
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Songs.Select(s => s.Key), Is.EqualTo(new[] { "Outer/x" }));
            Assert.That(catalog.Folders.Select(f => (f.Key, f.Title, f.Genre)),
                Is.EqualTo(new[] { ("local/Anime", "Anime Songs", "アニメ"), ("local/Outer/Inner", "Inner", "") }),
                "Folders without a chart beneath them are left out.");
            var anime = catalog.Folders[0];
            Assert.That(anime.Charts.Select(c => c.Key), Is.EqualTo(new[] { "Anime/Deep/a", "Anime/z" }));
            Assert.That(anime.Folders.Select(f => (f.Key, f.Title)),
                Is.EqualTo(new[] { ("local/Anime/Plain/Boxed", "Boxed"), ("local/Anime/Sub", "Sub") }));
            var sub = anime.Folders[1];
            Assert.That(sub.Charts.Single().Key, Is.EqualTo("Anime/Sub/s"));
            Assert.That(sub.Folders.Select(f => (f.Key, f.Title, f.Genre)), Is.EqualTo(new[] { ("local/Anime/Sub/More", "More", "GAME") }));
            Assert.That(sub.Folders[0].Charts.Single().Key, Is.EqualTo("Anime/Sub/More/m"));
            Assert.That(LocalSongScanner.Scan(root, "ja").Folders[0].Title, Is.EqualTo("アニメ"));
        }

        [Test]
        public void BoxDefReadsLocalizedTitlesAndGenreLikeOpenTaiko()
        {
            Write("A/box.def", "  #TITLEZH: 动画\n#TITLE: Re:Zero \n#COLLECTION:VOCALOID\n#GENRE:ANIME\n#GENRE:GAME\n; comment\n");
            Write("A/a.tja", Chart("A"));
            Write("B/box.def", "#COLLECTION:VOCALOID\n#TITLE:\n");
            Write("B/b.tja", Chart("B"));
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Folders.Select(f => (f.Title, f.Genre)), Is.EqualTo(new[] { ("Re:Zero", "GAME"), ("B", "VOCALOID") }));
            Assert.That(LocalSongScanner.Scan(root, "zh").Folders[0].Title, Is.EqualTo("动画"), "#TITLE<LANG> wins wherever it is.");
            Assert.That(LocalSongScanner.Scan(root, "ja").Folders[0].Title, Is.EqualTo("Re:Zero"));
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
        public void ScoreHashUsesOriginalBytesAndSurvivesRenamingButNotContentOrEncodingChanges()
        {
            const string text = "TITLE:Hash Test\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1,\n#END\n";
            Write("original.tja", text);
            const string expected = "8cbb7fa324a28a36715ec28c3a8a87d00f81e32a1670eca054b7cb44882b079f";
            Assert.That(LocalSongScanner.Scan(root).Songs.Single().Sha256, Is.EqualTo(expected));
            Directory.CreateDirectory(Path.Combine(root, "Moved"));
            File.Move(Path.Combine(root, "original.tja"), Path.Combine(root, "Moved/renamed.tja"));
            Write("copy.tja", text);
            Assert.That(LocalSongScanner.Scan(root).Songs.Select(s => s.Sha256), Is.All.EqualTo(expected));
            Write("copy.tja", text.Replace("1,", "2,"));
            Assert.That(LocalSongScanner.Scan(root).Songs.Select(s => s.Sha256), Is.Unique);
            File.WriteAllBytes(Path.Combine(root, "copy.tja"), new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray());
            var catalog = LocalSongScanner.Scan(root);
            Assert.That(catalog.Songs.Select(s => s.Text).Distinct().Count(), Is.EqualTo(1));
            Assert.That(catalog.Songs.Select(s => s.Sha256), Is.Unique, "BOM is part of the file even when decoded text is identical.");
            Write("copy.tja", text.Replace("\n", "\r\n"));
            Assert.That(LocalSongScanner.Scan(root).Songs.Select(s => s.Sha256), Is.Unique);
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
