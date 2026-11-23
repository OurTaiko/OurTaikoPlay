using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Editor;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The backdrops a game does not carry: the published catalog read from the distribution root
    // and kept for a launch that cannot reach it, a play's files downloaded by hash (asset
    // bundles for this platform) into Resources/Stage beside the songs and drawn from there, a
    // newer version fetched in an older one's place, and a backdrop that cannot be had passed
    // over for one that can.
    public sealed class StageDownloadsTests
    {
        const string Art = "Assets/OurTaiko/Resources/Stage/";
        FanmadeFixture site;
        string folder, fetched;
        byte[] header, scene, newer, catalogJson;
        static byte[] builtHeader, builtScene, builtNewer;
        static string built;
        string indexJson;
        int served;
        bool down, corrupt;

        static string Sha(byte[] b) => ObjectCache.Sha256(b);

        // A set "far" the game does not carry, and a preset "GONE" whose header is published nowhere.
        const string Published = @"{
          ""version"": 1,
          ""presets"": {
            ""FAR"":  { ""header"": ""far"",  ""scene"": ""far"", ""clear"": ""-"", ""dancers"": ""-"", ""footer"": ""-"", ""crowd"": ""-"", ""renda"": ""-"" },
            ""GONE"": { ""header"": ""gone"", ""scene"": ""far"", ""clear"": ""-"", ""dancers"": ""-"", ""footer"": ""-"", ""crowd"": ""-"", ""renda"": ""-"" }
          },
          ""sets"": {
            ""header"": { ""common_0"": {}, ""far"": { ""random"": false }, ""gone"": { ""random"": false } },
            ""scene"": { ""common_0"": {}, ""far"": { ""random"": false } }
          }
        }";

        string Index(byte[] headerBytes) => JsonUtility.ToJson(new StageDownloads.Index
        {
            files = new[]
            {
                new StageDownloads.Entry { name = "header/far", file = "header-far." + Sha(headerBytes).Substring(0, 16), size = headerBytes.Length, sha256 = Sha(headerBytes) },
                new StageDownloads.Entry { name = "scene/far", file = "scene-far." + Sha(scene).Substring(0, 16), size = scene.Length, sha256 = Sha(scene) },
            },
        });

        // Three small real files as bundles for the platform the Editor runs on, built once for
        // all the tests: importing for a platform the project is not switched to takes a while.
        [OneTimeSetUp]
        public void BuildBundles()
        {
            built = Path.Combine(Application.temporaryCachePath, "stage-downloads-" + Guid.NewGuid().ToString("N"));
            var files = StageBundles.Files().Where(f => f.name == "practice" || (f.part == "header" && f.name == "dojo")).ToArray();
            var index = StageBundles.Build(built, StageBundles.TargetNamed(StageLibrary.Platform).Value, files);
            string dir = Path.Combine(built, "stage", "bundle", StageBundles.Version, StageLibrary.Platform);
            byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(dir, index.files.First(e => e.name == name).file));
            builtHeader = Bytes("header/practice");
            builtScene = Bytes("scene/practice");
            builtNewer = Bytes("header/dojo");
        }

        [OneTimeTearDown]
        public void DeleteBundles()
        {
            try { Directory.Delete(built, true); } catch (IOException) { }
        }

        [SetUp]
        public void SetUp()
        {
            string root = Path.Combine(Path.GetTempPath(), "OurTaikoStageDownloads", Guid.NewGuid().ToString("N"));
            folder = Path.Combine(root, "Stage");
            fetched = Path.Combine(root, "Resources", "Stage");
            StageLibrary.UseFolder(folder, fetched);
            StageDownloads.Forget();
            // Published as the header and the scene of the set "far"; the dojo's header is a newer "far".
            header = builtHeader;
            scene = builtScene;
            newer = builtNewer;
            string bundles = "/stage/bundle/" + StageLibrary.BundleVersion + "/" + StageLibrary.Platform + "/";
            catalogJson = System.Text.Encoding.UTF8.GetBytes(Published);
            indexJson = Index(header);
            served = 0;
            down = corrupt = false;
            site = new FanmadeFixture();
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                if (!path.StartsWith("/stage/", StringComparison.Ordinal)) return false;
                if (down) return Reply(context, 503, new byte[0]);
                if (path == "/stage/catalog.json") return Reply(context, 200, catalogJson);
                if (path == bundles + "index.json") return Reply(context, 200, System.Text.Encoding.UTF8.GetBytes(indexJson));
                if (path.StartsWith(bundles + "header-far.", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref served);
                    // Whatever version the address names.
                    byte[] bytes = path.Contains(Sha(header).Substring(0, 16)) ? header : newer;
                    return Reply(context, 200, corrupt ? new byte[] { 1, 2, 3 } : bytes);
                }
                if (path.StartsWith(bundles + "scene-far.", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref served);
                    return Reply(context, 200, scene);
                }
                return Reply(context, 404, new byte[0]);
            };
        }

        [TearDown]
        public void TearDown()
        {
            StageLibrary.Forget();
            StageDownloads.Forget();
            site.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(folder), true); } catch (IOException) { }
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

        Task<StageChoice> Resolve(params string[] presets) => StageDownloads.ResolveAsync(presets, StageMode.Single, 1, new System.Random(1), site.BaseUrl);

        // As the next launch finds things: what was fetched is on disk, nothing is in memory.
        void Relaunch()
        {
            StageLibrary.UseFolder(folder, fetched);
            StageDownloads.Forget();
        }

        static string[] Clips(string part, string name) => StageLibrary.Load(part, name).File.Clips.Select(c => c.Name).ToArray();
        static string[] ClipsOf(string file) => Otanim(file).Clips.Select(c => c.Name).ToArray();
        static Chara.Otanim Otanim(string file) => Chara.Otanim.Parse(File.ReadAllBytes(Art + file + ".otanim"));

        [UnityTest]
        public IEnumerator ThePublishedCatalogIsKeptForALaunchOffline()
        {
            Assert.That(StageLibrary.Catalog.Pack("FAR"), Is.Null);
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            Assert.That(StageDownloads.HasIndex, Is.True);
            Assert.That(File.Exists(Path.Combine(fetched, StageLibrary.CatalogFile)), Is.True, "Beside the fetched files, in Resources/Stage beside the songs.");
            Assert.That(StageLibrary.Catalog.packs.Select(p => p.preset), Is.EqualTo(new[] { "FAR", "GONE" }), "The published catalog is the one in use.");

            Relaunch();
            down = true;
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            Assert.That(StageDownloads.HasIndex, Is.False);
            Assert.That(StageLibrary.Catalog.Pack("FAR"), Is.Not.Null, "Still known from the copy it kept.");
            // Its files were never fetched, and cannot be now: the play gets what the game carries.
            var resolve = Resolve("FAR");
            yield return Wait(resolve);
            Assert.That((resolve.Result.Preset, resolve.Result.Header), Is.EqualTo(((string)null, "common_0")));
        }

        [UnityTest]
        public IEnumerator APresetsFilesAreDownloadedOnceAndANewerVersionAgain()
        {
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            Assert.That(StageLibrary.Has(StageCatalog.HeaderFolder, "far"), Is.False);
            var resolve = Resolve("FAR");
            long total = header.Length + scene.Length;
            while (!resolve.IsCompleted)
            {
                long expected = StageDownloads.Expected, received = StageDownloads.Received;
                if (expected > 0)
                {
                    Assert.That(expected, Is.EqualTo(total));
                    Assert.That(received, Is.InRange(0, total));
                }
                yield return null;
            }
            yield return Wait(resolve);
            Assert.That((resolve.Result.Preset, resolve.Result.Header, resolve.Result.Scene, served), Is.EqualTo(("FAR", "far", "far", 2)));
            Assert.That((StageDownloads.Received, StageDownloads.Expected), Is.EqualTo((0L, 0L)));
            Assert.That(File.ReadAllBytes(Path.Combine(fetched, "header", "far.bundle")), Is.EqualTo(header), "Under its own name, in Resources/Stage beside the songs.");
            Assert.That(StageLibrary.Has(StageCatalog.HeaderFolder, "far"), Is.True);
            Assert.That(Clips(StageCatalog.HeaderFolder, "far"), Is.EqualTo(ClipsOf("header/practice")));
            Assert.That(StageLibrary.Load(StageCatalog.SceneFolder, "far").texture, Is.Not.Null);

            // Asked again, in this launch and the next, nothing is downloaded; nor offline.
            yield return Wait(Resolve("FAR"));
            Relaunch();
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            yield return Wait(Resolve("FAR"));
            Assert.That(served, Is.EqualTo(2));
            Relaunch();
            down = true;
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            resolve = Resolve("FAR");
            yield return Wait(resolve);
            Assert.That((resolve.Result.Preset, served), Is.EqualTo(("FAR", 2)), "What was fetched plays without the root.");
            down = false;

            // The header is published anew: the next play that uses it fetches the new one.
            indexJson = Index(newer);
            Relaunch();
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            yield return Wait(Resolve("FAR"));
            Assert.That(served, Is.EqualTo(3));
            Assert.That(Clips(StageCatalog.HeaderFolder, "far"), Is.EqualTo(ClipsOf("header/dojo")));

            // The player's own file of that name is still the one drawn.
            Directory.CreateDirectory(Path.Combine(folder, "header"));
            File.Copy(Art + "header/tutorial.otanim", Path.Combine(folder, "header", "far.otanim"));
            Assert.That(Clips(StageCatalog.HeaderFolder, "far"), Is.EqualTo(ClipsOf("header/tutorial")));
        }

        [UnityTest]
        public IEnumerator ABackdropThatCannotBeHadIsPassedOver()
        {
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            // GONE's header is in no index: the next one asked for is the one.
            var resolve = Resolve("GONE", "nothing", "FAR");
            yield return Wait(resolve);
            Assert.That(resolve.Result.Preset, Is.EqualTo("FAR"));
            // With nothing after it, the play gets a backdrop out of what the game carries.
            resolve = Resolve("GONE");
            yield return Wait(resolve);
            Assert.That((resolve.Result.Preset, resolve.Result.Header, resolve.Result.Scene), Is.EqualTo(((string)null, "common_0", "common_0")));

            // A file that is not what the index says is refused, and its backdrop with it.
            Directory.Delete(fetched, true);
            Relaunch();
            corrupt = true;
            yield return Wait(StageDownloads.RefreshAsync(site.BaseUrl));
            resolve = Resolve("FAR");
            yield return Wait(resolve);
            Assert.That((resolve.Result.Preset, resolve.Result.Header), Is.EqualTo(((string)null, "common_0")));
            Assert.That(File.Exists(Path.Combine(fetched, "header", "far.bundle")), Is.False);
        }
    }
}
