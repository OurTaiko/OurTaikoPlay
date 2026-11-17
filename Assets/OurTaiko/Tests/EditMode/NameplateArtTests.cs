using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The nameplate's art is not in the game: the bucket's catalog.json names the files, which are
    // downloaded into the object cache by hash, cut into the plate's pieces, and remembered for when
    // the bucket cannot be reached.
    public sealed class NameplateArtTests
    {
        static readonly (string key, string file, int w, int h)[] Files =
        {
            ("shadow", "shadow.png", 408, 96), ("outline", "outline.png", 408, 96), ("bands", "frame_top.png", 408, 280),
            ("rainbow", "frame_top_rainbow.png", 408, 336), ("danBackground", "dan_emblem_bg.png", 160, 48),
            ("danRingSilver", "dan_ring_silver.png", 72, 40), ("danRingGold", "dan_ring_gold.png", 72, 40),
            ("danRingRainbow", "dan_ring_rainbow.png", 72, 40), ("danText", "dan_text.png", 360, 200),
            ("danTextGold", "dan_text_gold.png", 360, 200), ("badge1p", "1p.png", 88, 88), ("badge2p", "2p.png", 88, 88),
        };

        FanmadeFixture site;
        string cache;
        readonly Dictionary<string, byte[]> served = new Dictionary<string, byte[]>();
        JObject catalog;
        int downloads;
        bool siteDown;

        string From => site.BaseUrl + "/nameplate/";

        static string Sha(byte[] b)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        // Each file its own colour, so no two share a hash.
        static byte[] Png(int w, int h, int shade = 0)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(shade * 20), 0, 0, 255);
            t.SetPixels32(pixels);
            try { return t.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }

        [SetUp]
        public void SetUp()
        {
            NameplateArt.Forget();
            downloads = 0;
            siteDown = false;
            served.Clear();
            cache = Path.Combine(Path.GetTempPath(), "OurTaikoNameplateArt", Guid.NewGuid().ToString("N"));
            var files = new JObject();
            for (int i = 0; i < Files.Length; i++)
            {
                var (key, file, w, h) = Files[i];
                var png = Png(w, h, i);
                served[file] = png;
                files[key] = new JObject { ["path"] = file, ["size"] = png.Length, ["sha256"] = Sha(png) };
            }
            catalog = new JObject { ["version"] = 1, ["files"] = files };
            site = new FanmadeFixture();
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                if (!path.StartsWith("/nameplate/")) return false;
                if (siteDown) return Reply(context, 503, new byte[0]);
                string name = path.Substring("/nameplate/".Length);
                if (name == "catalog.json") return Reply(context, 200, System.Text.Encoding.UTF8.GetBytes(catalog.ToString()));
                if (!served.TryGetValue(name, out var body)) return Reply(context, 404, new byte[0]);
                Interlocked.Increment(ref downloads);
                return Reply(context, 200, body);
            };
        }

        [TearDown]
        public void TearDown()
        {
            NameplateArt.Forget();
            site.Dispose();
            try { Directory.Delete(cache, true); } catch (IOException) { }
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

        [UnityTest]
        public IEnumerator TheArtIsDownloadedCutRememberedAndKeptOffline()
        {
            int changes = 0;
            void Count() => changes++;
            NameplateArt.Changed += Count;
            try
            {
                Assert.That(NameplateArt.Ready, Is.False, "the game carries no nameplate art");
                yield return Wait(NameplateArt.FetchAsync(cache, from: From));
                Assert.That(NameplateArt.Ready, Is.True);
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(downloads, Is.EqualTo(Files.Length));
                foreach (var png in served.Values)
                {
                    string hash = Sha(png);
                    Assert.That(File.Exists(Path.Combine(cache, "objects", hash.Substring(0, 2), hash)), Is.True, "in the object cache");
                }
                Assert.That(File.Exists(Path.Combine(cache, NameplateArt.StateFile)), Is.True);

                // Cut as the game's sprites were: rows from the top, 段位 cells 5 to a row.
                Assert.That(NameplateArt.Bands.Length, Is.EqualTo(5));
                Assert.That(NameplateArt.Bands[0].rect, Is.EqualTo(new Rect(0, 224, 408, 56)));
                Assert.That(NameplateArt.Bands[4].rect, Is.EqualTo(new Rect(0, 0, 408, 56)));
                Assert.That(NameplateArt.Rainbow.Length, Is.EqualTo(6));
                Assert.That(NameplateArt.Rainbow[5].rect, Is.EqualTo(new Rect(0, 0, 408, 56)));
                Assert.That(NameplateArt.DanRings.Length, Is.EqualTo(3));
                Assert.That(NameplateArt.DanTexts.Length, Is.EqualTo(25));
                Assert.That(NameplateArt.DanTexts[0].rect, Is.EqualTo(new Rect(0, 160, 72, 40)));
                Assert.That(NameplateArt.DanTexts[6].rect, Is.EqualTo(new Rect(72, 120, 72, 40)));
                Assert.That(NameplateArt.GoldDanTexts[24].rect, Is.EqualTo(new Rect(288, 0, 72, 40)));
                Assert.That(NameplateArt.Badge2P.rect, Is.EqualTo(new Rect(0, 0, 88, 88)));

                // Again: nothing downloaded, the plates are not redrawn.
                yield return Wait(NameplateArt.FetchAsync(cache, from: From));
                Assert.That(downloads, Is.EqualTo(Files.Length));
                Assert.That(changes, Is.EqualTo(1));

                // The bucket cannot be reached: the remembered catalog, from the cache.
                siteDown = true;
                NameplateArt.Forget();
                yield return Wait(NameplateArt.FetchAsync(cache, from: From));
                Assert.That(NameplateArt.Ready, Is.True, "remembered");

                // Nothing cached either: the plates stay text.
                NameplateArt.Forget();
                Directory.Delete(cache, true);
                yield return Wait(NameplateArt.FetchAsync(cache, from: From));
                Assert.That(NameplateArt.Ready, Is.False);
            }
            finally { NameplateArt.Changed -= Count; }
        }

        [UnityTest]
        public IEnumerator RegionalCatalogUsesMirrorAndFallsBackAfterBadBytes()
        {
            using var api = new FanmadeFixture();
            using var mirror = new FanmadeFixture();
            int mirrorGets = 0;
            mirror.CustomRequest = context => {
                Interlocked.Increment(ref mirrorGets);
                Assert.That(context.Request.Headers["Authorization"], Is.Null);
                return Reply(context, 200, new byte[] { 1, 2, 3 });
            };
            var routed = (JObject)catalog.DeepClone();
            routed["baseUrl"] = From;
            routed["mirrorBaseUrl"] = mirror.BaseUrl + "/";
            routed["mirrorPaths"] = new JArray("shadow.png");
            api.CustomRequest = context => Reply(context, 200, System.Text.Encoding.UTF8.GetBytes(routed.ToString()));
            yield return Wait(NameplateArt.FetchAsync(cache, from: From, catalogUrl: api.BaseUrl + "/api/v1/nameplate-art/catalog"));
            Assert.That(NameplateArt.Ready, Is.True);
            Assert.That(mirrorGets, Is.EqualTo(1));
            Assert.That(downloads, Is.EqualTo(Files.Length), "canonical bytes repaired the bad mirror");
        }

        [UnityTest]
        public IEnumerator AFileThatDoesNotMatchItsHashIsRefused()
        {
            catalog["files"]["shadow"]["sha256"] = new string('0', 64);
            yield return Wait(NameplateArt.FetchAsync(cache, from: From));
            Assert.That(NameplateArt.Ready, Is.False);
            Assert.That(File.Exists(Path.Combine(cache, "objects", "00", new string('0', 64))), Is.False);
            Assert.That(File.Exists(Path.Combine(cache, NameplateArt.StateFile)), Is.False);
        }

        [Test]
        public void AnImageOfTheWrongSizeLeavesTheArtUnset()
        {
            var images = new Dictionary<string, byte[]>();
            foreach (var (key, _, w, h) in Files) images[key] = Png(w, h);
            images["bands"] = Png(408, 56);
            Assert.Throws<InvalidDataException>(() => NameplateArt.Use(images));
            Assert.That(NameplateArt.Ready, Is.False);
        }
    }
}
