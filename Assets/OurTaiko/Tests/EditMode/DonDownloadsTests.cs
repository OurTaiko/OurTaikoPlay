using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // A seat's Don as its player chose it on the website: GET /api/v1/users/{id}/don, its files
    // downloaded into the object cache and checked by hash, remembered for when the site cannot be
    // reached, and the plain Don for a seat without one.
    public sealed class DonDownloadsTests
    {
        FanmadeFixture site;
        string cache;
        byte[] model;
        string hash;
        int served;
        bool siteDown;
        JObject look;

        static string Sha(byte[] b)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        [SetUp]
        public void SetUp()
        {
            model = File.ReadAllBytes(Chara3DTests.CopyOfPlainDon("download-source"));
            hash = Sha(model);
            cache = Path.Combine(Path.GetTempPath(), "OurTaikoDonDownloads", Guid.NewGuid().ToString("N"));
            site = new FanmadeFixture();
            look = JObject.Parse("{\"costume\":true,\"cos\":77,\"head\":0,\"body\":0,\"puchi\":0,\"colors\":[\"#112233\",\"#445566\",\"#778899\"]}");
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                if (path == "/api/v1/users/" + Player + "/don")
                {
                    if (siteDown) return Reply(context, 503, Encoding("{}"));
                    var answer = new JObject
                    {
                        ["look"] = look,
                        ["files"] = new JArray(new JObject { ["kind"] = "cos", ["id"] = 77, ["url"] = site.BaseUrl + "/3dchara/cos/77.glb?v=" + hash.Substring(0, 12), ["size"] = model.Length, ["sha256"] = hash }),
                    };
                    return Reply(context, 200, Encoding(answer.ToString()));
                }
                if (path == "/3dchara/cos/77.glb")
                {
                    Interlocked.Increment(ref served);
                    return Reply(context, 200, model);
                }
                return false;
            };
            DonDownloads.Forget(0);
        }

        [TearDown]
        public void TearDown()
        {
            DonDownloads.Forget(0);
            site.Dispose();
            try { Directory.Delete(cache, true); } catch (IOException) { }
        }

        const string Player = "0000000000000000000000000000d0d0";

        static byte[] Encoding(string s) => System.Text.Encoding.UTF8.GetBytes(s);

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
        public IEnumerator AChosenLookIsDownloadedRememberedAndWorn()
        {
            int changes = 0;
            void Count(int seat) { if (seat == 0) changes++; }
            DonDownloads.Changed += Count;
            try
            {
                Assert.That(DonLooks.For(0).cos, Is.EqualTo(0), "a guest wears the plain Don");
                yield return Wait(DonDownloads.FetchAsync(0, Player, site.BaseUrl, cache));
                var worn = DonLooks.For(0);
                Assert.That(worn.costume && worn.cos == 77, Is.True);
                Assert.That(worn.color1, Is.EqualTo(new Color32(0x11, 0x22, 0x33, 255)));
                Assert.That(worn.color3, Is.EqualTo(new Color32(0x77, 0x88, 0x99, 255)));
                Assert.That(File.Exists(Path.Combine(cache, "objects", hash.Substring(0, 2), hash)), Is.True, "in the object cache");
                Assert.That(DonLibrary.HasFile("cos", 77), Is.True);
                Assert.That(DonLibrary.Part("cos", 77), Is.Not.Null, "the downloaded model is read");
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(DonDownloads.Downloading, Is.False);

                // Cached: not downloaded again.
                yield return Wait(DonDownloads.FetchAsync(0, Player, site.BaseUrl, cache));
                Assert.That(served, Is.EqualTo(1));

                // The site cannot be reached: the remembered look, from the cache.
                siteDown = true;
                DonDownloads.Forget(0);
                Assert.That(DonLooks.For(0).cos, Is.EqualTo(0));
                yield return Wait(DonDownloads.FetchAsync(0, Player, site.BaseUrl, cache));
                Assert.That(DonLooks.For(0).cos, Is.EqualTo(77), "remembered");

                // Nothing remembered and nothing cached: the plain Don in the seat's colours.
                Directory.Delete(cache, true);
                yield return Wait(DonDownloads.FetchAsync(0, Player, site.BaseUrl, cache));
                Assert.That(DonLooks.For(0).cos, Is.EqualTo(0));
                Assert.That(DonLooks.For(0).color1, Is.EqualTo(DonLook.Default(0).color1));
            }
            finally { DonDownloads.Changed -= Count; }
        }

        [UnityTest]
        public IEnumerator AFileThatDoesNotMatchItsHashIsRefused()
        {
            hash = new string('0', 64);
            yield return Wait(DonDownloads.FetchAsync(0, Player, site.BaseUrl, cache));
            Assert.That(DonLooks.For(0).cos, Is.EqualTo(0), "the plain Don");
            Assert.That(File.Exists(Path.Combine(cache, "objects", "00", hash)), Is.False);
        }

        [Test]
        public void LooksWithoutColoursKeepTheSeats()
        {
            var l = DonDownloads.LookFrom(JObject.Parse("{\"costume\":false,\"cos\":3,\"head\":4,\"body\":5,\"puchi\":6,\"colors\":null}"), 1);
            Assert.That((l.costume, l.cos, l.head, l.body, l.puchi), Is.EqualTo((false, 3, 4, 5, 6)));
            Assert.That(l.color1, Is.EqualTo(DonLook.Default(1).color1));
        }
    }
}
