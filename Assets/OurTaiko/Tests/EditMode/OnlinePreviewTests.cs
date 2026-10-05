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
        byte[] preview;
        string cache;
        int gets, forbidden, failures, manifests;
        bool missing, corrupt;
        [SetUp] public void Setup()
        {
            gets = forbidden = failures = manifests = 0; missing = corrupt = false;
            api = new FanmadeFixture { SongIdOnly = true, CourseKeyed = true, ResourceDownloadVersion = 1 };
            origin = new FanmadeFixture();
            preview = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
            api.Charts.Add(new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = new byte[10000] });
            api.Manifest = c => {
                manifests++;
                JObject R(byte[] bytes, string type) => new JObject { ["url"] = origin.BaseUrl + "/preview?signature=get", ["headUrl"] = origin.BaseUrl + "/preview?signature=head", ["sha256"] = FanmadeFixture.Sha(bytes), ["size"] = bytes.Length, ["contentType"] = type };
                var resources = new JObject { ["tja"] = R(c.Tja,"application/octet-stream"), ["audio"] = R(c.Audio,"audio/ogg") };
                if (!missing) resources["preview"] = R(preview,"audio/ogg");
                return new JObject { ["chartId"] = c.Id, ["expiresAt"] = DateTime.UtcNow.AddMinutes(15).ToString("o"), ["resources"] = resources };
            };
            origin.CustomRequest = ctx => {
                gets++;
                if (ctx.Request.HttpMethod != "GET" || ctx.Request.Headers["Range"] != null || ctx.Request.Headers["Authorization"] != null || ctx.Request.Headers["Cookie"] != null || ctx.Request.RawUrl != "/preview?signature=get") forbidden++;
                if (failures-- > 0) { ctx.Response.StatusCode = 403; ctx.Response.Close(); return true; }
                var bytes = (byte[])preview.Clone(); if (corrupt) bytes[0] ^= 1;
                ctx.Response.ContentLength64 = bytes.Length; ctx.Response.OutputStream.Write(bytes,0,bytes.Length); ctx.Response.Close(); return true;
            };
            cache = Path.Combine(Path.GetTempPath(), "preview-test-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
            var endpoint = client.Add(api.Server("don","katsu")); endpoint.AllowLoopbackResourcesForTests = true;
            Run(() => client.ConnectAsync(endpoint,false));
        }
        static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();
        static void Run(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();
        string Download() => Run(() => client.PreparePreviewAsync(client.Charts[0]));
        [TearDown] public void Cleanup() { client.Dispose(); api.Dispose(); origin.Dispose(); if (Directory.Exists(cache)) Directory.Delete(cache,true); }
        [Test] public void DownloadsOnlyPreviewAndReusesVerifiedCache() {
            string path=Download(); Assert.That(File.ReadAllBytes(path),Is.EqualTo(preview));
            Assert.That(Download(),Is.EqualTo(path)); Assert.That(gets,Is.EqualTo(1)); Assert.That(forbidden,Is.Zero);
        }
        [Test] public void ChangedPreviewGetsNewCacheWithoutChangingOriginalAudio() {
            string old=Download(); preview[0]^=1; string current=Download();
            Assert.That(current,Is.Not.EqualTo(old)); Assert.That(gets,Is.EqualTo(2));
        }
        [Test] public void CorruptCacheIsDownloadedAgain() { string path=Download(); File.WriteAllBytes(path,new byte[preview.Length]); Download(); Assert.That(gets,Is.EqualTo(2)); }
        [Test] public void CorruptDownloadNeverEntersCache() { corrupt=true; Assert.That(Assert.Throws<FanmadeException>(()=>Download()).Message,Is.EqualTo("DOWNLOAD_INTEGRITY_FAILED")); Assert.That(Directory.Exists(cache) ? Directory.GetFiles(cache,"*.ogg",SearchOption.AllDirectories).Length : 0,Is.Zero); }
        [Test] public void ExpiredSignatureRefreshesOnce() { failures=1; Download(); Assert.That(manifests,Is.EqualTo(2)); Assert.That(gets,Is.EqualTo(2)); }
        [Test] public void RepeatedForbiddenDoesNotDownloadFullSong() { failures=10; Assert.Throws<HttpStatusException>(()=>Download()); Assert.That(gets,Is.EqualTo(2)); }
        [Test] public void MissingPreviewDoesNotDownloadFullSong() { missing=true; Assert.That(Assert.Throws<FanmadeException>(()=>Download()).Message,Is.EqualTo("PREVIEW_UNAVAILABLE")); Assert.That(gets,Is.Zero); }
        [Test] public void CancellationStartsNoDownload() { using var c=new CancellationTokenSource(); c.Cancel(); Assert.Catch<OperationCanceledException>(()=>Run(()=>client.PreparePreviewAsync(client.Charts[0],c.Token))); Assert.That(gets,Is.Zero); }
    }
}
