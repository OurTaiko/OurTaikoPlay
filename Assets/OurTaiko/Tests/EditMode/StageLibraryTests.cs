using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The catalog as the JSON a person edits, and the Stage folder a player puts their own
    // backdrops and catalog in: theirs are drawn in place of the game's, and what of theirs
    // cannot be read leaves the game's as it was.
    public sealed class StageLibraryTests
    {
        const string Art = "Assets/OurTaiko/Resources/Stage/";
        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "OurTaikoStage", Guid.NewGuid().ToString("N"));
            StageLibrary.UseFolder(folder);
        }

        [TearDown]
        public void TearDown()
        {
            StageLibrary.Forget();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }

        void Put(string relative, string from)
        {
            string to = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(Art + from, to, true);
        }

        void Write(string text)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, StageLibrary.CatalogFile), text);
        }

        static string[] Clips(Chara.OtanimAsset asset) => asset.File.Clips.Select(c => c.Name).ToArray();

        static string GameCatalog => Resources.Load<TextAsset>(StageLibrary.BuiltInCatalog).text;

        // The game's own catalog, and the published one of every set (Tools/stage-publish).
        [TestCase(false)]
        [TestCase(true)]
        public void ACatalogIsReadableJsonThatWritesBackTheSame(bool published)
        {
            string text = published ? File.ReadAllText("Tools/stage-publish/catalog.json") : GameCatalog;
            var warnings = new List<string>();
            var catalog = StageCatalogFile.Read(text, warnings.Add);
            try
            {
                Assert.That(warnings, Is.Empty);
                Assert.That(catalog.packs.Select(p => p.preset), Has.Member(StageCatalog.DanPreset));
                Assert.That((catalog.Pack("dan").header, catalog.Pack("DAN").clear), Is.EqualTo(("dojo", StagePack.None)));
                if (published)
                {
                    Assert.That((catalog.Pack("toho").header, catalog.Pack("TOHO").troupe, catalog.Troupe("toho").leadFirst), Is.EqualTo(("toho", "toho", true)));
                    Assert.That(catalog.packs.Length, Is.GreaterThan(20));
                }
                // As the game writes it: an entry a line, nothing that says only what is the default.
                string written = StageCatalogFile.Write(catalog);
                Assert.That(written.Replace("\r", ""), Is.EqualTo(text.Replace("\r", "")), "Write the catalog with StageCatalogFile.Write so the next change to it shows as its own lines.");
                Assert.That(written, Does.Contain("    \"DAN\": { \"header\": \"dojo\", \"scene\": \"dojo\", \"clear\": \"-\""));
                Assert.That(written, Does.Contain("      \"common_0\": {}"));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void ACatalogSaysWhatIsWrongAndWhere()
        {
            string Wrong(string json) => Assert.Throws<FormatException>(() => StageCatalogFile.Read(json)).Message;
            Assert.That(Wrong("{ presets: "), Does.StartWith("it is not JSON"));
            Assert.That(Wrong("{}"), Does.Contain("\"version\" is missing"));
            Assert.That(Wrong("{\"version\": 99}"), Does.Contain("reads up to " + StageCatalogFile.Version));
            Assert.That(Wrong("{\"version\": 1, \"presets\": []}"), Is.EqualTo("presets must be { ... }"));
            Assert.That(Wrong("{\"version\": 1, \"presets\": {\"A\": {\"header\": 3}}}"), Is.EqualTo("presets.A.header must be a name in quotes"));
            Assert.That(Wrong("{\"version\": 1, \"presets\": {\"A\": {\"soloOnly\": \"yes\"}}}"), Is.EqualTo("presets.A.soloOnly must be true or false"));
            Assert.That(Wrong("{\"version\": 1, \"sets\": {\"header\": {\"x\": true}}}"), Does.StartWith("sets.header.x must be { ... }"));
            Assert.That(Wrong("{\"version\": 1, \"sets\": {\"dancers\": {\"x\": {\"members\": 9}}}}"), Is.EqualTo("sets.dancers.x.members must be 1 to 5"));
            Assert.That(Wrong("{\"version\": 1, \"sets\": {\"dancers\": {\"x\": {\"dance\": 100, \"leave\": 50}}}}"), Does.StartWith("sets.dancers.x: leave must come after dance"));
            Assert.That(Wrong("{\"version\": 1, \"sets\": {\"renda\": {\"x\": {\"spread\": [1, 2]}}}}"), Does.StartWith("sets.renda.x.spread must be [[x, y], ...]"));

            // Comments are allowed, and a key the game does not know is passed over with a word.
            var warnings = new List<string>();
            var catalog = StageCatalogFile.Read(@"{
                // mine
                ""version"": 1,
                ""presets"": { ""MINE"": { ""header"": ""a"", ""dancer"": ""b"", ""soloOnly"": true } }, /* one preset */
                ""sets"": { ""header"": { ""a"": { ""random"": false, ""colour"": 1 } }, ""headers"": {} },
                ""note"": ""x""
            }", warnings.Add);
            try
            {
                Assert.That((catalog.Pack("mine").header, catalog.Pack("mine").troupe, catalog.Pack("mine").singleOnly, catalog.headers[0].pooled), Is.EqualTo(("a", "", true, false)));
                Assert.That(warnings.Count, Is.EqualTo(4));
                Assert.That(warnings, Has.Some.StartWith("presets.MINE.dancer is not known here (known: header, scene, clear, dancers,"));
                Assert.That(warnings, Has.Some.StartWith("sets.headers is not a part"));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void APlayersFileIsDrawnInPlaceOfTheGames()
        {
            var games = StageLibrary.Load(StageCatalog.HeaderFolder, "common_0");
            Assert.That(games, Is.SameAs(Resources.Load<Chara.OtanimAsset>("Stage/header/common_0")));
            Assert.That(File.Exists(Path.Combine(folder, StageLibrary.ReadMeFile)), Is.True, "The folder is made, with a note on what goes in it.");

            // The dojo's header under the common header's name.
            Put("header/common_0.otanim", "header/dojo.otanim");
            var theirs = StageLibrary.Load(StageCatalog.HeaderFolder, "common_0");
            Assert.That(theirs, Is.Not.SameAs(games));
            Assert.That(Clips(theirs), Is.EqualTo(Clips(Resources.Load<Chara.OtanimAsset>("Stage/header/dojo"))));
            Assert.That((theirs.texture != null, theirs.texture.width > 2), Is.EqualTo((true, true)));
            Assert.That(StageLibrary.Load(StageCatalog.HeaderFolder, "common_0"), Is.SameAs(theirs), "Read once.");
            Assert.That(StageLibrary.Load(StageCatalog.HeaderFolder, "dojo"), Is.SameAs(Resources.Load<Chara.OtanimAsset>("Stage/header/dojo")), "Only the name they gave.");

            // A play on another backdrop frees it.
            Assert.That(StageLibrary.LoadedOutsideFiles, Is.EqualTo(1));
            StageLibrary.Keep(new (string, string)[0]);
            Assert.That((StageLibrary.LoadedOutsideFiles, theirs == null), Is.EqualTo((0, true)));

            // A file that is not an otanim is said in the log, and the game's is drawn.
            File.WriteAllText(Path.Combine(folder, "header", "common_0.otanim"), "not an otanim");
            Assert.That(StageLibrary.Load(StageCatalog.HeaderFolder, "common_0"), Is.SameAs(games));
            File.Delete(Path.Combine(folder, "header", "common_0.otanim"));
            Assert.That(StageLibrary.Load(StageCatalog.HeaderFolder, "common_0"), Is.SameAs(games));
        }

        [Test]
        public void APlayersCatalogAddsToTheGamesAndReplacesWhatItNames()
        {
            int presets = StageLibrary.Catalog.packs.Length, headers = StageLibrary.Catalog.headers.Length;
            Assert.That(StageLibrary.Catalog.Pack("MYSTAGE"), Is.Null);
            Write(@"{
              ""version"": 1,
              ""presets"": {
                ""MYSTAGE"": { ""header"": ""mine"", ""scene"": ""common_0"", ""clear"": ""-"", ""dancers"": ""-"" },
                ""dan"": { ""header"": ""mine"" }
              },
              ""sets"": { ""header"": { ""mine"": { ""random"": false }, ""loose"": {} } }
            }");
            var catalog = StageLibrary.Catalog;
            Assert.That((catalog.packs.Length, catalog.headers.Length), Is.EqualTo((presets + 1, headers + 2)));
            Assert.That(catalog.Pack("DAN").scene, Is.EqualTo(""), "Their DAN takes the game's place as they wrote it.");
            var choice = catalog.Choose(new[] { "mystage" }, StageMode.Single, new System.Random(1), StageLibrary.Has);
            Assert.That((choice.Preset, choice.Header, choice.Scene, choice.Clear, choice.Troupe), Is.EqualTo(("MYSTAGE", "mine", "common_0", StagePack.None, (string)null)));

            // A set is drawn at random only once its file is there.
            Assert.That((StageLibrary.Has(StageCatalog.HeaderFolder, "loose"), StageLibrary.Has(StageCatalog.HeaderFolder, "common_0")), Is.EqualTo((false, true)));
            var random = new System.Random(5);
            Assert.That(Enumerable.Range(0, 200).Select(_ => catalog.Choose(new string[0], StageMode.Single, random, StageLibrary.Has).Header), Has.None.EqualTo("loose"));
            Put("header/loose.otanim", "header/dojo.otanim");
            Assert.That(StageLibrary.Has(StageCatalog.HeaderFolder, "loose"), Is.True);
            Assert.That(Enumerable.Range(0, 200).Select(_ => catalog.Choose(new string[0], StageMode.Single, random, StageLibrary.Has).Header), Has.Some.EqualTo("loose"));
            Assert.That(Clips(StageLibrary.Load(StageCatalog.HeaderFolder, "loose")), Is.EqualTo(Clips(Resources.Load<Chara.OtanimAsset>("Stage/header/dojo"))));

            // A catalog that cannot be read is said in the log; the game's own is all there is.
            File.SetLastWriteTimeUtc(Path.Combine(folder, StageLibrary.CatalogFile), DateTime.UtcNow.AddSeconds(-30));
            Write("{ \"version\": 1, \"presets\": [] }");
            catalog = StageLibrary.Catalog;
            Assert.That((catalog.packs.Length, catalog.Pack("MYSTAGE"), catalog.Pack("DAN").scene), Is.EqualTo((presets, (StagePack)null, "dojo")));
            // And put right, it is read again.
            File.SetLastWriteTimeUtc(Path.Combine(folder, StageLibrary.CatalogFile), DateTime.UtcNow.AddSeconds(-20));
            Write("{ \"version\": 1, \"presets\": { \"MYSTAGE\": {} } }");
            Assert.That(StageLibrary.Catalog.Pack("MYSTAGE"), Is.Not.Null);
        }
    }
}
