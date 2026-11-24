using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Multi;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // Where public files come from (Distribution): the root the site names for this player, asked
    // once, joined to the paths the API gives, and the canonical root to fall back on.
    public sealed class DistributionTests
    {
        OnlineFixture site;

        static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

        [SetUp] public void SetUp() => site = new OnlineFixture();

        [TearDown]
        public void TearDown()
        {
            site.Dispose();
            Distribution.Forget();
        }

        [Test]
        public void TheRootIsAskedOnceAndJoinedToPaths()
        {
            site.FilesRoot = site.BaseUrl + "/mirror/ourtaiko/";
            site.Mirror = true;
            var root = Run(() => Distribution.RootAsync(site.BaseUrl + "/"));
            Assert.That((root.Base, root.Canonical, root.Mirror), Is.EqualTo((site.BaseUrl + "/mirror/ourtaiko/", site.BaseUrl + "/", true)));
            Assert.That(Run(() => Distribution.RootAsync(site.BaseUrl)), Is.SameAs(root), "Asked once, whatever the spelling of the site.");
            Assert.That(site.Requests.Count(r => r == "GET /api/v2/distribution/source"), Is.EqualTo(1));
            var address = root.Locate("fanmade/" + new string('a', 64) + ".ogg");
            Assert.That(address.Url, Is.EqualTo(site.BaseUrl + "/mirror/ourtaiko/fanmade/" + new string('a', 64) + ".ogg"));
            Assert.That(address.Fallback, Is.EqualTo(site.BaseUrl + "/fanmade/" + new string('a', 64) + ".ogg"));
            Assert.That(Distribution.CanonicalRoot.Locate("3dchara/cos/0.glb").Url, Is.EqualTo("https://distrib.ourtaiko.org/3dchara/cos/0.glb"));
            Assert.That(Distribution.CanonicalRoot.Locate("3dchara/cos/0.glb").Fallback, Is.Null, "Nothing else to try.");
            Assert.That(Assert.Throws<OnlineException>(() => root.Locate("../x")).Message, Is.EqualTo("RESOURCE_PATH_INVALID"));
        }

        [Test]
        public void ASiteThatCannotSayLeavesTheCanonicalRoot()
        {
            site.FilesRoot = null;
            Assert.That(Run(() => Distribution.RootAsync(site.BaseUrl)), Is.SameAs(Distribution.CanonicalRoot));
            Assert.That(Run(() => Distribution.RootAsync("http://127.0.0.1:9")), Is.SameAs(Distribution.CanonicalRoot), "Nothing listens there.");
        }

        [Test]
        public void OnlyAnHttpsRootWithoutCredentialsQueryOrFragmentIsUsed()
        {
            const string net = "https://net.ourtaiko.org";
            JObject Answer(string baseUrl, string canonical = Distribution.CanonicalBase) => new JObject { ["mirror"] = true, ["baseUrl"] = baseUrl, ["canonicalBaseUrl"] = canonical };
            var mirror = Distribution.Read(Answer("https://mirror.example/ourtaiko/"), net);
            Assert.That((mirror.Base, mirror.Canonical, mirror.Mirror), Is.EqualTo(("https://mirror.example/ourtaiko/", Distribution.CanonicalBase, true)));
            foreach (string bad in new[]
            {
                "http://mirror.example/ourtaiko/", "https://user:secret@mirror.example/", "https://mirror.example/?a=1/", "https://mirror.example/#x/",
                "https://mirror.example/ourtaiko", "ftp://mirror.example/", "mirror.example/", "", "http://127.0.0.1:8096/",
            })
                Assert.That(Distribution.Read(Answer(bad), net), Is.SameAs(Distribution.CanonicalRoot), bad);
            Assert.That(Distribution.Read(new JObject { ["baseUrl"] = 5 }, net), Is.SameAs(Distribution.CanonicalRoot));
            Assert.That(Distribution.Read(new JArray(), net), Is.SameAs(Distribution.CanonicalRoot));
            // A canonical root that cannot be used is the game's own.
            Assert.That(Distribution.Read(Answer("https://mirror.example/", "http://elsewhere.example/"), net).Canonical, Is.EqualTo(Distribution.CanonicalBase));
            // A site on this machine may name a plain-HTTP root on this machine (development, tests).
            Assert.That(Distribution.Read(Answer("http://127.0.0.1:8096/api/v2/files/"), "http://127.0.0.1:8096").Base, Is.EqualTo("http://127.0.0.1:8096/api/v2/files/"));
        }

        [Test]
        public void PathsAreRelativeAndClean()
        {
            foreach (string good in new[] { "fanmade/a.ogg", "database/" + new string('b', 64) + ".tja", "3dchara/cos/0.glb", "nameplate/shadow.png", "sso/avatars/a/b.webp" })
                Assert.That(Distribution.ValidPath(good), Is.True, good);
            foreach (string bad in new[] { null, "", "/fanmade/a.ogg", "fanmade//a.ogg", "fanmade/./a.ogg", "fanmade/../a.ogg", "..", "fanmade/a.ogg?v=1",
                "fanmade/a.ogg#x", "fanmade\\a.ogg", "fanmade/a%2e.ogg", "https://distrib.ourtaiko.org/a.ogg", "fanmade/a b.ogg", "fanmade/" })
                Assert.That(Distribution.ValidPath(bad), Is.False, bad ?? "null");
            Assert.That(Distribution.PathOf("https://distrib.ourtaiko.org/sso/avatars/a/b.webp"), Is.EqualTo("sso/avatars/a/b.webp"));
            foreach (string other in new[] { "https://distrib.ourtaiko.org/sso/a.webp?v=1", "https://distrib.ourtaiko.org.evil/a.webp", "http://distrib.ourtaiko.org/a.webp",
                "https://elsewhere.example/a.webp", "https://distrib.ourtaiko.org/" })
                Assert.That(Distribution.PathOf(other), Is.Null, other);
        }

        [Test]
        public void AFailedFileIsTriedOnceMoreFromTheCanonicalRoot()
        {
            var tried = new List<string>();
            Task<string> Fetch(string url)
            {
                lock (tried) tried.Add(url);
                if (url.StartsWith("https://mirror.example/", StringComparison.Ordinal)) throw new OnlineException("DOWNLOAD_INTEGRITY_FAILED");
                return Task.FromResult("bytes of " + url);
            }
            var root = new FileRoot("https://mirror.example/", Distribution.CanonicalBase, true);
            Assert.That(Run(() => Distribution.FetchAsync(root.Locate("a/b"), Fetch)), Is.EqualTo("bytes of https://distrib.ourtaiko.org/a/b"));
            Assert.That(tried, Is.EqualTo(new[] { "https://mirror.example/a/b", "https://distrib.ourtaiko.org/a/b" }));

            // The canonical root's own failure is the answer: there is nothing else to try.
            tried.Clear();
            var gone = new FileRoot("https://mirror.example/", "https://mirror.example/");
            Assert.Throws<OnlineException>(() => Run(() => Distribution.FetchAsync(gone.Locate("a/b"), Fetch)));
            Assert.That(tried, Has.Count.EqualTo(1));

            // A cancelled download is not tried again.
            tried.Clear();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OnlineException>(() => Run(() => Distribution.FetchAsync(root.Locate("a/b"), Fetch, cancel.Token)));
            Assert.That(tried, Has.Count.EqualTo(1));
        }

        // HttpClient reports its own timeout as a cancellation (TaskCanceledException). A mirror
        // that hangs times out, and that is tried again from the canonical root; only the caller's
        // own cancellation is not.
        [Test]
        public void AMirrorThatTimesOutIsTriedOnceMoreFromTheCanonicalRoot()
        {
            var tried = new List<string>();
            Task<string> Fetch(string url)
            {
                lock (tried) tried.Add(url);
                if (url.StartsWith("https://mirror.example/", StringComparison.Ordinal)) throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");
                return Task.FromResult("bytes of " + url);
            }
            var root = new FileRoot("https://mirror.example/", Distribution.CanonicalBase, true);
            using var waiting = new CancellationTokenSource();
            Assert.That(Run(() => Distribution.FetchAsync(root.Locate("a/b"), Fetch, waiting.Token)), Is.EqualTo("bytes of https://distrib.ourtaiko.org/a/b"));
            Assert.That(tried, Is.EqualTo(new[] { "https://mirror.example/a/b", "https://distrib.ourtaiko.org/a/b" }));
        }

        // The account center gives full addresses. One on the canonical root is read from the same
        // path on the player's root, and from the canonical address when that fails; any other is
        // read as it is.
        [Test]
        public void AnAccountPictureOnTheCanonicalRootComesFromThePlayersRoot()
        {
            byte[] picture = { 1, 2, 3 };
            int mirrored = 0;
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                if (path == "/mirror/sso/avatars/a/b.webp") mirrored++;
                else if (path != "/elsewhere/c.webp") return false;
                context.Response.ContentLength64 = picture.Length;
                context.Response.OutputStream.Write(picture, 0, picture.Length);
                context.Response.Close();
                return true;
            };
            Distribution.Use(ServerConfig.Site, new FileRoot(site.BaseUrl + "/mirror/", Distribution.CanonicalBase, true));
            Assert.That(Run(() => MultiAvatars.DownloadAsync("https://distrib.ourtaiko.org/sso/avatars/a/b.webp", CancellationToken.None)), Is.EqualTo(picture));
            Assert.That(mirrored, Is.EqualTo(1));
            Assert.That(Run(() => MultiAvatars.DownloadAsync(site.BaseUrl + "/elsewhere/c.webp", CancellationToken.None)), Is.EqualTo(picture), "Another address is read as it is.");
            Assert.That(site.Requests, Has.None.EqualTo("GET /api/v2/distribution/source"));
        }
    }
}
