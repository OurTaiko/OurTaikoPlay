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
    // The nameplate set the game does not carry: lumen.json read from the distribution root, the
    // files the cache lacks downloaded by hash into the object cache and drawn from there, the list
    // kept for a launch that cannot reach the root, a file that cannot be had left out and fetched
    // next time, and a list or catalog this game cannot read refused.
    public sealed class NameplateDownloadsTests
    {
        // What the fixture publishes: three items of the baked set, under the whole catalog.
        static readonly string[] Published = { "band/lumen-title-wood", "icon/lumen-rankup", "composition/plates" };
        OnlineFixture site;
        string cache, listJson, corrupt;
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
                string path = "lumen/" + id + "-" + hash.Substring(0, 12) + NameplateItems.Extension;
                files["/nameplate/" + path] = bytes;
                listed[id] = new JObject { ["path"] = path, ["size"] = bytes.Length, ["sha256"] = hash };
            }
            var catalog = JObject.Parse(File.ReadAllText(Path.Combine(NameplateSet.BakedFolder, NameplateSet.CatalogId + ".json")));
            listJson = new JObject { ["version"] = 1, ["catalog"] = catalog, ["files"] = listed }.ToString();
            served = changes = 0;
            down = false;
            corrupt = null;
            site = new OnlineFixture();
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                if (!path.StartsWith("/nameplate/", StringComparison.Ordinal)) return false;
                if (down) return Reply(context, 503, new byte[0]);
                if (path == "/nameplate/lumen.json") return Reply(context, 200, Encoding.UTF8.GetBytes(listJson));
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
            Assert.That(PlateComposition.ReadCatalog().Titles.Count, Is.EqualTo(88));
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
            corrupt = files.Keys.Single(path => path.Contains("/icon/lumen-rankup-"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("icon/lumen-rankup could not be downloaded"));
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Load(NameplateSlot.Icon, PlateComposition.RankUpItem), Is.Null, "Its slot is drawn empty.");
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood"), Is.Not.Null, "The rest is drawn.");
            Assert.That(Directory.GetFiles(Path.Combine(cache, "objects"), "*", SearchOption.AllDirectories), Has.Length.EqualTo(Published.Length - 1), "Nothing is kept of the bad download.");

            int before = changes;
            corrupt = null;
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Load(NameplateSlot.Icon, PlateComposition.RankUpItem), Is.Not.Null);
            Assert.That(changes, Is.EqualTo(before + 1), "Plates are drawn again for the file that came.");
        }

        [UnityTest]
        public IEnumerator ACatalogThatCannotBeReadLeavesTheSetInUse()
        {
            TestData.UseBakedNameplates();
            var inUse = NameplateItems.Set;
            var list = JObject.Parse(listJson);
            ((JObject)list["catalog"]).Remove("titles");
            listJson = list.ToString();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Could not fetch the nameplate set"));
            yield return Wait(Refresh());
            Assert.That(NameplateItems.Set, Is.SameAs(inUse));
            Assert.That(File.Exists(Path.Combine(cache, NameplateDownloads.StateFile)), Is.False, "Not kept for the next launch either.");
        }

        [Test]
        public void AListThisGameCannotReadIsRefused()
        {
            var good = JObject.Parse(listJson);
            Assert.That(NameplateDownloads.Read(good.ToString()).files.Keys, Is.EquivalentTo(Published));
            JObject Wood(JObject list) => (JObject)list["files"]["band/lumen-title-wood"];
            var breaks = new Action<JObject>[]
            {
                list => list["version"] = 2,
                list => list.Remove("catalog"),
                list => list["files"]["hat/x"] = Wood(list).DeepClone(),
                list => list["files"]["band/X"] = Wood(list).DeepClone(),
                list => Wood(list)["path"] = "../x.otanim",
                list => Wood(list)["path"] = "lumen/band/x.png",
                list => Wood(list)["size"] = 0,
                list => Wood(list)["sha256"] = "ABC",
            };
            for (int i = 0; i < breaks.Length; i++)
            {
                var bad = (JObject)good.DeepClone();
                breaks[i](bad);
                Assert.Throws<FormatException>(() => NameplateDownloads.Read(bad.ToString()), "change " + i);
            }
            Assert.Throws<FormatException>(() => NameplateDownloads.Read("not a list"));
        }
    }
}
