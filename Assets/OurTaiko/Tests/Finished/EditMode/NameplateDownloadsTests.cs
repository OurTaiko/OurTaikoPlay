using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The nameplate set the game does not carry: what there is asked of the site (the layouts and
    // each picture's hash, and the presets), the files the cache lacks downloaded by hash from the
    // distribution root into the object cache and drawn from there, the answers kept for a launch
    // that cannot reach the site, a file that cannot be had left out and fetched next time, and
    // answers this game cannot read refused.
    public sealed class NameplateDownloadsTests
    {
        // What the fixture publishes: three items of the baked set, under the whole catalog. None is
        // one the game carries itself, which would be drawn whether it came or not.
        const string Icon = "icon/lumen-title-wec";
        static readonly string[] Published = { "band/lumen-title-wood", Icon, "composition/plates" };
        OnlineFixture site;
        string cache, setJson, partsJson, corrupt;
        // The files by their address under the root.
        readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
        int served, changes;
        bool down;

        [SetUp]
        public void SetUp()
        {
            // It publishes items of the baked set.
            TestData.RequireBakedNameplates();
            cache = Path.Combine(Path.GetTempPath(), "OurTaikoNameplateDownloads", Guid.NewGuid().ToString("N"));
            var listed = new JObject();
            files.Clear();
            foreach (string id in Published)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(NameplateSet.BakedFolder, id + NameplateItems.Extension));
                string hash = ObjectCache.Sha256(bytes);
                files["/" + NameplateDownloads.PathOf(hash)] = bytes;
                listed[id] = hash;
            }
            // As the site answers: the plate's layouts with the pictures' hashes, and the presets.
            var baked = NameplateSet.Baked().Catalog;
            var layout = (JObject)baked.DeepClone();
            layout.Remove("titles");
            layout.Remove("aiTitles");
            setJson = new JObject { ["version"] = 1, ["layout"] = layout, ["files"] = listed }.ToString();
            var presets = new JArray();
            foreach (var (kind, key) in new[] { ("player", "titles"), ("ai", "aiTitles") })
                foreach (var title in ((JObject)baked[key]).Properties())
                {
                    var layers = (JObject)title.Value.DeepClone();
                    layers["effectOverflow"] = (bool?)layers["effectOverflow"] ?? false;
                    presets.Add(new JObject { ["id"] = presets.Count + 1, ["name"] = title.Name, ["kind"] = kind, ["layers"] = layers });
                }
            partsJson = new JObject { ["items"] = new JArray(), ["presets"] = presets }.ToString();
            served = changes = 0;
            down = false;
            corrupt = null;
            site = new OnlineFixture();
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                bool asked = path == NameplateDownloads.SetPath || path == NameplateDownloads.PartsPath;
                if (!asked && !path.StartsWith("/" + NameplateDownloads.Folder, StringComparison.Ordinal)) return false;
                if (down) return Reply(context, 503, new byte[0]);
                if (asked) return Reply(context, 200, Encoding.UTF8.GetBytes(path == NameplateDownloads.SetPath ? setJson : partsJson));
                if (!files.TryGetValue(path, out var bytes)) return Reply(context, 404, new byte[0]);
                Interlocked.Increment(ref served);
                return Reply(context, 200, path == corrupt ? new byte[] { 1, 2, 3 } : bytes);
            };
            // As a first launch: no set yet.
            NameplateItems.Use(null);
            NameplateItems.SetChanged += Changed;
        }

        void Changed() => changes++;

        [TearDown]
        public void TearDown()
        {
            NameplateItems.SetChanged -= Changed;
            // Neither is there when SetUp found no set to publish.
            site?.Dispose();
            site = null;
            TestData.UseBakedNameplates();
            if (cache != null) try { Directory.Delete(cache, true); } catch (IOException) { }
            cache = null;
        }

        static bool Reply(HttpListenerContext context, int status, byte[] body)
        {
            context.Response.StatusCode = status;
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.Close();
            return true;
        }

        static IEnumerator Wait(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "timed out");
                yield return null;
            }
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        Task Refresh() => NameplateDownloads.RefreshAsync(cache, site.BaseUrl);

        [UnityTest]
        public IEnumerator TheSetIsDownloadedOnceAndKeptForALaunchOffline()
        {
            yield return Wait(Refresh());
            var set = NameplateItems.Set;
            Assert.That(set, Is.Not.Null);
            Assert.That(served, Is.EqualTo(Published.Length));
            foreach (string id in Published)
            {
                Assert.That(set.PathOf(id), Does.StartWith(Path.Combine(cache, "objects")), id + " is drawn from the object cache.");
                Assert.That(File.Exists(set.PathOf(id)), Is.True, id);
            }
            var wood = NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood");
            Assert.That(wood, Is.Not.Null);
            Assert.That(wood.name, Is.EqualTo("lumen-title-wood"), "Named as the item, not by its hash.");
            Assert.That(NameplateItems.CompositionPart("plates"), Is.Not.Null);
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold"), Is.Null, "Not published here: nothing to draw.");
            Assert.That(PlateComposition.ReadCatalog().Titles.Count, Is.GreaterThanOrEqualTo(88), "The arcade's 88, and any added since.");
            Assert.That(changes, Is.EqualTo(1));

            // The same list again: nothing downloaded, nothing drawn anew.
            yield return Wait(Refresh());
            Assert.That(served, Is.EqualTo(Published.Length));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(NameplateItems.Set, Is.SameAs(set));

            // The next launch, offline: the list kept in the cache, its files from there.
            NameplateItems.Use(null);
            down = true;
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Set, Is.Not.Null);
            Assert.That(NameplateItems.Set.PathOf("band/lumen-title-wood"), Is.EqualTo(set.PathOf("band/lumen-title-wood")));
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood"), Is.SameAs(wood), "The same file is read once.");
        }

        [UnityTest]
        public IEnumerator AFileThatCannotBeHadIsLeftOutAndFetchedNextTime()
        {
            corrupt = "/" + NameplateDownloads.PathOf(ObjectCache.Sha256(File.ReadAllBytes(Path.Combine(NameplateSet.BakedFolder, Icon + NameplateItems.Extension))));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(Icon + " could not be downloaded"));
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Load(NameplateSlot.Icon, Icon), Is.Null, "Its slot is drawn empty.");
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood"), Is.Not.Null, "The rest is drawn.");
            Assert.That(Directory.GetFiles(Path.Combine(cache, "objects"), "*", SearchOption.AllDirectories), Has.Length.EqualTo(Published.Length - 1), "Nothing is kept of the bad download.");

            int before = changes;
            corrupt = null;
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Load(NameplateSlot.Icon, Icon), Is.Not.Null);
            Assert.That(changes, Is.EqualTo(before + 1), "Plates are drawn again for the file that came.");
        }

        // A title the site gives says which picture each part is. One the set does not list (a part
        // added since it was read) is fetched by that hash when a plate first wears it, once, and the
        // plates are drawn again; a part the set does list is drawn from the title's picture, not
        // from the one the set has under that name.
        [UnityTest]
        public IEnumerator APartToldByItsHashIsFetchedWhenAPlateWearsIt()
        {
            yield return Wait(Refresh());
            byte[] gold = File.ReadAllBytes(Path.Combine(NameplateSet.BakedFolder, "band/lumen-title-gold" + NameplateItems.Extension));
            string hash = ObjectCache.Sha256(gold);
            files["/" + NameplateDownloads.PathOf(hash)] = gold;
            int before = changes, fetched = served;
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold", hash), Is.Null, "Not here yet, and the set has none of that name.");
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold", hash), Is.Null);
            float deadline = Time.realtimeSinceStartup + 20;
            while (changes == before)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "timed out");
                yield return null;
            }
            Assert.That(served, Is.EqualTo(fetched + 1), "Asked for once.");
            var picture = NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold", hash);
            Assert.That(picture, Is.Not.Null);
            Assert.That(picture.name, Is.EqualTo("lumen-title-gold"));
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold"), Is.Null, "By its name alone it is still no part of the set.");

            // The title's picture, whatever the set draws under that name.
            var wood = NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood");
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood", hash), Is.SameAs(picture));
            Assert.That(wood, Is.Not.SameAs(picture));

            // One that cannot be had: the part is drawn as the set has it, and is not asked for again.
            string none = new string('0', 64);
            bool said = false;
            void Heard(string message, string stack, LogType type) => said |= type == LogType.Warning && message.Contains(none);
            Application.logMessageReceived += Heard;
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("nameplate picture " + none + " could not be downloaded"));
                Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood", none), Is.SameAs(wood));
                deadline = Time.realtimeSinceStartup + 30;
                while (!said)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "timed out");
                    yield return null;
                }
            }
            finally { Application.logMessageReceived -= Heard; }
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood", none), Is.SameAs(wood));
            Assert.That(changes, Is.EqualTo(before + 1), "Nothing came, nothing is drawn anew.");
        }

        [UnityTest]
        public IEnumerator ACatalogThatCannotBeReadLeavesTheSetInUse()
        {
            TestData.UseBakedNameplates();
            var inUse = NameplateItems.Set;
            // No look called wood: the one every plate falls back on.
            var parts = JObject.Parse(partsJson);
            foreach (var preset in ((JArray)parts["presets"]).Where(preset => (string)preset["name"] == "wood").ToList()) preset.Remove();
            partsJson = parts.ToString();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Could not fetch the nameplate set"));
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Set, Is.SameAs(inUse));
            Assert.That(File.Exists(Path.Combine(cache, NameplateDownloads.StateFile)), Is.False, "Not kept for the next launch either.");
        }

        [Test]
        public void AnswersThisGameCannotReadAreRefused()
        {
            string good = NameplateDownloads.Kept(setJson, partsJson);
            var (catalog, read) = NameplateDownloads.Read(good);
            Assert.That(read.Keys, Is.EquivalentTo(Published));
            // The presets are the catalog's titles, each saying whether its effect runs over the plate.
            Assert.That(((JObject)catalog["titles"]).Count, Is.GreaterThanOrEqualTo(88));
            Assert.That(((JObject)catalog["aiTitles"]).Count, Is.EqualTo(8));
            Assert.That((bool?)catalog["titles"]["gold"]["effectOverflow"], Is.True);
            Assert.That(catalog["titles"]["ai_3"]["effectOverflow"], Is.Null);
            Assert.That((string)catalog["aiTitles"]["wood"]["band"], Is.EqualTo("band/lumen-ai-title-wood"));
            JObject Wood(JObject kept) => ((JArray)kept["parts"]["presets"]).OfType<JObject>().First(preset => (string)preset["name"] == "wood");
            var breaks = new Action<JObject>[]
            {
                kept => kept["set"]["version"] = 2,
                kept => ((JObject)kept["set"]).Remove("layout"),
                kept => ((JObject)kept["set"]).Remove("files"),
                kept => kept.Remove("parts"),
                kept => ((JObject)kept["parts"]).Remove("presets"),
                kept => kept["set"]["files"]["hat/x"] = kept["set"]["files"]["band/lumen-title-wood"],
                kept => kept["set"]["files"]["band/X"] = kept["set"]["files"]["band/lumen-title-wood"],
                kept => kept["set"]["files"]["band/lumen-title-wood"] = "ABC",
                kept => kept["set"]["files"]["band/lumen-title-wood"] = new JObject(),
                kept => Wood(kept)["kind"] = "other",
                kept => Wood(kept)["name"] = "Wood!",
                kept => ((JObject)Wood(kept)["layers"]).Remove("band"),
                kept => Wood(kept)["layers"]["icon"] = "band/lumen-title-wood",
            };
            for (int i = 0; i < breaks.Length; i++)
            {
                var bad = JObject.Parse(good);
                breaks[i](bad);
                Assert.Throws<FormatException>(() => NameplateDownloads.Read(bad.ToString()), "change " + i);
            }
            Assert.Throws<FormatException>(() => NameplateDownloads.Read("not a set"));
        }
    }
}
