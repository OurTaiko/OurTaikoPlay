using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    public sealed class OnlinePreviewTests
    {
        FanmadeFixture api, origin;
        FanmadeClient client;
        FanmadeEndpoint endpoint;
        string cache;
        FanmadeFixture.Chart chart;
        byte[] audio;
        int heads, ranges, manifests, forbidden, failures;
        int failStatus;
        bool ignoreRange, badRange, changed, wrongTag, shortBody;
        static T Run<T>(Func<Task<T>> action) => Task.Run(action).GetAwaiter().GetResult();
        static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        [SetUp] public void Setup()
        {
            heads = ranges = manifests = forbidden = failures = failStatus = 0;
            ignoreRange = badRange = changed = wrongTag = shortBody = false;
            audio = Enumerable.Range(0, PreviewRangeStream.BlockSize * 4).Select(i => (byte)(i % 251)).ToArray();
            api = new FanmadeFixture { SongIdOnly = true, CourseKeyed = true, ResourceDownloadVersion = 1 };
            origin = new FanmadeFixture();
            chart = new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = audio };
            api.Charts.Add(chart);
            api.Manifest = c =>
            {
                manifests++;
                JObject Resource(byte[] bytes, string type) => new JObject {
                    ["url"] = origin.BaseUrl + "/object?signature=get%2B", ["headUrl"] = origin.BaseUrl + "/object?signature=head%2B",
                    ["sha256"] = changed && manifests > 1 ? new string('a', 64) : FanmadeFixture.Sha(bytes), ["size"] = bytes.Length, ["contentType"] = type };
                return new JObject { ["chartId"] = c.Id, ["expiresAt"] = DateTime.UtcNow.AddMinutes(15).ToString("o"),
                    ["resources"] = new JObject { ["tja"] = Resource(c.Tja, "application/octet-stream"), ["audio"] = Resource(c.Audio, "audio/ogg") } };
            };
            origin.CustomRequest = context =>
            {
                var req = context.Request; var res = context.Response;
                bool head = req.HttpMethod == "HEAD";
                if (req.Headers["Authorization"] != null || req.Headers["Cookie"] != null || req.Headers["Idempotency-Key"] != null) forbidden++;
                if (req.RawUrl != "/object?signature=" + (head ? "head%2B" : "get%2B")) forbidden++;
                if (head) heads++; else ranges++;
                if (failures-- > 0) { res.StatusCode = failStatus; res.Close(); return true; }
                res.Headers["ETag"] = wrongTag && !head ? "\"changed\"" : "\"actual-etag\"";
                if (head) { res.ContentLength64 = audio.Length; res.Close(); return true; }
                if (req.Headers["If-Match"] != "\"actual-etag\"" || req.Headers["If-Range"] != null) forbidden++;
                string[] span = req.Headers["Range"].Substring(6).Split('-');
                int start = int.Parse(span[0]), end = int.Parse(span[1]);
                res.StatusCode = ignoreRange ? 200 : 206;
                res.Headers["Content-Range"] = "bytes " + (badRange ? start + 1 : start) + "-" + end + "/" + audio.Length;
                int size = end - start + 1 - (shortBody ? 1 : 0);
                res.ContentLength64 = size; res.OutputStream.Write(audio, start, size); res.Close(); return true;
            };
            cache = Path.Combine(Path.GetTempPath(), "ourtaiko-preview-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache); endpoint = client.Add(api.Server("don", "katsu")); endpoint.AllowLoopbackResourcesForTests = true;
            Run(() => client.ConnectAsync(endpoint, false));
        }
        [TearDown] public void Cleanup() { client.Dispose(); api.Dispose(); origin.Dispose(); Directory.Delete(cache, true); }
        PreviewRangeStream Open() => Run(() => PreviewRangeStream.OpenAsync(endpoint, client.Charts[0], CancellationToken.None));
        [Test] public void HeadAndRangeUseSeparateSignaturesActualEtagAndNoCredentials()
        {
            using var stream = Open();
            Run(() => stream.FetchBlockAsync(2)); Run(() => stream.FetchBlockAsync(2));
            stream.Position = PreviewRangeStream.BlockSize * 2;
            var bytes = new byte[30]; Assert.That(stream.Read(bytes, 0, bytes.Length), Is.EqualTo(30));
            Assert.That(bytes, Is.EqualTo(audio.Skip(PreviewRangeStream.BlockSize * 2).Take(30)));
            Assert.That((heads, ranges, manifests, forbidden), Is.EqualTo((1, 1, 1, 0)));
            Assert.That(stream.DownloadedBytes, Is.EqualTo(PreviewRangeStream.BlockSize));
        }
        [Test] public void LegacyServerKeepsApiAuthenticationAndHashEtagConvention()
        {
            api.SongIdOnly = api.CourseKeyed = false; api.ResourceDownloadVersion = 0;
            Run(() => client.ConnectAsync(endpoint, false));
            int authenticated = 0;
            api.CustomRequest = ctx =>
            {
                if (!ctx.Request.Url.AbsolutePath.EndsWith("/audio")) return false;
                if (ctx.Request.Headers["Authorization"]?.StartsWith("Bearer ") == true) authenticated++;
                ctx.Response.Headers["ETag"] = "\"" + FanmadeFixture.Sha(audio) + "\"";
                if (ctx.Request.HttpMethod == "HEAD") { ctx.Response.ContentLength64 = audio.Length; ctx.Response.Close(); return true; }
                Assert.That(ctx.Request.Headers["If-Range"], Is.EqualTo("\"" + FanmadeFixture.Sha(audio) + "\""));
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = "bytes 0-" + (PreviewRangeStream.BlockSize - 1) + "/" + audio.Length;
                ctx.Response.ContentLength64 = PreviewRangeStream.BlockSize;
                ctx.Response.OutputStream.Write(audio, 0, PreviewRangeStream.BlockSize); ctx.Response.Close(); return true;
            };
            using var stream = Open(); Run(() => stream.FetchBlockAsync(0));
            Assert.That(authenticated, Is.EqualTo(2)); Assert.That(manifests, Is.Zero);
        }
        [Test] public void DecoderReadNeverFetchesNetwork()
        {
            using var stream = Open(); var buffer = new byte[10];
            Assert.That(stream.Read(buffer, 0, 10), Is.Zero); Assert.That(stream.MissingBlock, Is.Zero); Assert.That(ranges, Is.Zero);
        }
        [Test] public void First403RefreshPreservesPinnedContentAndCachedBlocks()
        {
            using var stream = Open(); Run(() => stream.FetchBlockAsync(0));
            failures = 1; failStatus = 403; Run(() => stream.FetchBlockAsync(1)); Run(() => stream.FetchBlockAsync(0));
            Assert.That(manifests, Is.EqualTo(2)); Assert.That(ranges, Is.EqualTo(3)); Assert.That(forbidden, Is.Zero);
        }
        [Test] public void Second403StopsWithoutLogin()
        {
            using var stream = Open(); failures = 3; failStatus = 403;
            Assert.Throws<HttpStatusException>(() => Run(() => stream.FetchBlockAsync(0)));
            Assert.That(manifests, Is.EqualTo(2)); Assert.That(ranges, Is.EqualTo(2));
            Assert.That(api.Requests.Count(x => x.Contains("login")), Is.EqualTo(1));
        }
        [Test] public void HashChangeOnRefreshStopsOldSession()
        {
            using var stream = Open(); failures = 1; failStatus = 403; changed = true;
            Assert.That(Assert.Throws<FanmadeException>(() => Run(() => stream.FetchBlockAsync(0))).Message, Is.EqualTo("PREVIEW_RESOURCE_CHANGED"));
            Assert.That(stream.DownloadedBytes, Is.Zero);
        }
        [Test] public void PreconditionFailureStopsOldSession()
        {
            using var stream = Open(); failures = 1; failStatus = 412;
            Assert.That(Assert.Throws<HttpStatusException>(() => Run(() => stream.FetchBlockAsync(0))).Status, Is.EqualTo(412));
            Assert.That(manifests, Is.EqualTo(1));
        }
        [TestCase("200")] [TestCase("range")] [TestCase("etag")] [TestCase("short")]
        public void InvalidResponsesNeverEnterBlockCache(string kind)
        {
            using var stream = Open(); ignoreRange = kind == "200"; badRange = kind == "range"; wrongTag = kind == "etag"; shortBody = kind == "short";
            Assert.Throws<FanmadeException>(() => Run(() => stream.FetchBlockAsync(0)));
            Assert.That(stream.DownloadedBytes, Is.Zero);
        }
        [Test] public void CancelledSessionNeverStartsAnotherRange()
        {
            using var cancel = new CancellationTokenSource();
            using var stream = Run(() => PreviewRangeStream.OpenAsync(endpoint, client.Charts[0], cancel.Token));
            cancel.Cancel(); Assert.Throws<OperationCanceledException>(() => Run(() => stream.FetchBlockAsync(0))); Assert.That(ranges, Is.Zero);
        }
    }
}
