using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    public sealed class ResourceDownloadTests
    {
        FanmadeFixture api, origin;
        FanmadeFixture.Chart chart;
        FanmadeClient client;
        FanmadeEndpoint endpoint;
        string cache;
        int manifests, tjaGets, audioGets, forbidden;
        public int ResourceStatus;
        bool corrupt, mismatch;
        static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        static T Run<T>(Func<Task<T>> action) => Task.Run(action).GetAwaiter().GetResult();
        [SetUp] public void Setup()
        {
            manifests = tjaGets = audioGets = forbidden = ResourceStatus = 0;
            corrupt = mismatch = false;
            api = new FanmadeFixture();
            origin = new FanmadeFixture();
            chart = new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = new byte[] { 1, 2, 3 } };
            api.Charts.Add(chart);
            api.Manifest = c =>
            {
                manifests++;
                JObject Resource(string kind, byte[] bytes, string type) => new JObject
                {
                    ["url"] = origin.BaseUrl + "/" + kind + "?signature=a%2Fb&mode=get", ["headUrl"] = origin.BaseUrl + "/" + kind + "?signature=head",
                    ["sha256"] = FanmadeFixture.Sha(bytes), ["size"] = bytes.Length, ["contentType"] = type,
                };
                return new JObject { ["chartId"] = c.Id,
                    ["resources"] = new JObject { ["tja"] = Resource("tja", c.Tja, "application/octet-stream"),
                        ["audio"] = Resource("audio", mismatch ? new byte[] { 9 } : c.Audio, "audio/ogg") } };
            };
            origin.CustomRequest = context =>
            {
                if (context.Request.Headers["Authorization"] != null || context.Request.Headers["Cookie"] != null || context.Request.Headers["Idempotency-Key"] != null) forbidden++;
                if (context.Request.RawUrl != context.Request.Url.AbsolutePath + "?signature=a%2Fb&mode=get") forbidden++;
                bool audio = context.Request.Url.AbsolutePath == "/audio";
                if (audio) audioGets++; else tjaGets++;
                context.Response.StatusCode = ResourceStatus == 0 ? 200 : ResourceStatus;
                byte[] bytes = audio ? chart.Audio : chart.Tja;
                if (corrupt) bytes = bytes.Select(b => (byte)(b ^ 1)).ToArray();
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close(); return true;
            };
            cache = Path.Combine(Path.GetTempPath(), "ourtaiko-resources-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
            endpoint = client.Add(api.Server("don", "katsu"));
            Run(() => client.ConnectAsync(endpoint, false));
        }
        [TearDown] public void Cleanup() { client.Dispose(); api.Dispose(); origin.Dispose(); Directory.Delete(cache, true); }
        [TestCase(null)] [TestCase("shift-jis")] [TestCase("invalid")]
        public void Utf8DownloadIgnoresEncodingMetadataAndAcceptsBom(string encoding)
        {
            chart.Encoding = encoding;
            chart.Tja = Encoding.UTF8.GetBytes("\uFEFF" + FanmadeFixture.SimpleTja());
            Run(() => client.ConnectAsync(endpoint, false));
            var prepared = Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(prepared.Tja, Does.Contain("TITLE:" + chart.Title));
            Assert.That(prepared.Tja.IndexOf('\uFEFF'), Is.EqualTo(-1));
            Assert.That(prepared.Tja, Does.Contain("#START"));
            Assert.That(tjaGets, Is.EqualTo(1));
            Assert.That(forbidden, Is.Zero);
        }
        [TestCase("chartId")] [TestCase("sha256")] [TestCase("size")] [TestCase("url")] [TestCase("missing")]
        public void InvalidManifestIsRejectedBeforeDownload(string field)
        {
            var original = api.Manifest;
            api.Manifest = c =>
            {
                var v = original(c); var tja = (JObject)v["resources"]["tja"];
                if (field == "chartId") v["chartId"] = new string('f', 32);
                else if (field == "missing") ((JObject)v["resources"]).Remove("tja");
                else if (field == "size") tja["size"] = long.MaxValue;
                else tja[field] = field == "url" ? "http://example.com/file" : "bad";
                return v;
            };
            Assert.Throws<FanmadeException>(() => Run(() => client.PrepareAsync(client.Charts[0])));
            Assert.That(tjaGets + audioGets, Is.Zero);
        }
        [Test] public void First403RefreshCanRecoverWithoutChangingAccount()
        {
            var original = origin.CustomRequest; bool first = true;
            origin.CustomRequest = context =>
            {
                if (!first) return original(context);
                first = false; context.Response.StatusCode = 403; context.Response.Close(); return true;
            };
            Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(manifests, Is.EqualTo(2)); Assert.That(forbidden, Is.Zero);
        }
        [Test] public void CancelledPreparationDoesNotPublish()
        {
            using var cancel = new System.Threading.CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => Run(() => client.PrepareAsync(client.Charts[0], cancel.Token)));
            Assert.That(tjaGets + audioGets, Is.Zero);
        }
        [Test] public void TranslationSelectionPreservesOriginalAndFallsBackToIt()
        {
            var c = client.Charts[0]; c.Titles["en"] = "English"; c.Titles["zh"] = "中文"; c.Titles["ko"] = " ";
            Assert.That(c.ToSongInfo("zh-Hans").Title, Is.EqualTo("中文"));
            Assert.That(c.ToSongInfo("ko").Title, Is.EqualTo(chart.Title), "A blank translation falls back to the original, not to en or ja.");
            Assert.That(c.Title, Is.EqualTo(chart.Title));
            Assert.That(c.TitleHeaders(), Does.Contain("TITLEEN:English"));
        }
        [Test] public void DirectDownloadCacheRepairAndIndependentHashUpdates()
        {
            var first = Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(File.Exists(first.AudioPath), Is.True);
            Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That((tjaGets, audioGets, manifests, forbidden, api.Downloads), Is.EqualTo((1, 1, 2, 0, 0)));
            File.WriteAllBytes(first.AudioPath, new byte[] { 9, 9, 9 });
            Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(audioGets, Is.EqualTo(2));
            chart.Audio = new byte[] { 4, 5, 6 };
            var updated = Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That((tjaGets, audioGets), Is.EqualTo((1, 3)));
            Assert.That(updated.AudioPath, Is.Not.EqualTo(first.AudioPath));
            Assert.That(File.ReadAllBytes(updated.AudioPath), Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(File.ReadAllBytes(first.AudioPath), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        [Test] public void MetadataOnlyChangesRebuildTjaWithoutDownloadingObjects()
        {
            var first = Run(() => client.PrepareAsync(client.Charts[0]));
            chart.Title = "Metadata only";
            chart.Difficulties[0] = ("Oni", 10, "");
            var second = Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(second.AudioPath, Is.EqualTo(first.AudioPath));
            Assert.That(second.Tja, Does.Contain("TITLE:Metadata only\n"));
            Assert.That(second.Tja, Does.Contain("LEVEL:10\n"));
            Assert.That((tjaGets, audioGets), Is.EqualTo((1, 1)));
            Assert.That(Directory.GetFiles(Path.Combine(cache, "objects"), "*", SearchOption.AllDirectories), Has.Length.EqualTo(2));
        }

        [Test] public void DifferentServersShareBytesButKeepTheirOwnMetadata()
        {
            var first = Run(() => client.PrepareAsync(client.Charts[0]));
            using var otherApi = new FanmadeFixture();
            otherApi.Charts.Add(new FanmadeFixture.Chart { Tja = chart.Tja, Audio = chart.Audio, Title = "Other server" });
            var other = client.Add(otherApi.Server());
            Run(() => client.ConnectAsync(other, true));
            var second = Run(() => client.PrepareAsync(client.Charts.Single(c => c.Server == other.Id)));
            Assert.That(second.AudioPath, Is.EqualTo(first.AudioPath));
            Assert.That(second.Tja, Does.Contain("TITLE:Other server\n"));
            Assert.That(otherApi.Downloads, Is.Zero);
        }

        [Test] public void CancelAfterTjaPublicationLeavesReusableObjectAndNoTemporaryFiles()
        {
            using var cancel = new System.Threading.CancellationTokenSource();
            Assert.Catch<OperationCanceledException>(() => Run(() => client.PrepareAsync(client.Charts[0], cancel.Token,
                progress => { if (progress.Chart.Status == FileProgress.State.Complete) cancel.Cancel(); })));
            Assert.That((tjaGets, audioGets), Is.EqualTo((1, 0)));
            Assert.That(Directory.GetFiles(cache, "*.part", SearchOption.AllDirectories), Is.Empty);
            Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That((tjaGets, audioGets), Is.EqualTo((1, 1)));
        }

        [TestCase(false)] [TestCase(true)]
        public void ConcurrentServersPublishTheSameObjects(bool repair)
        {
            if (repair)
            {
                var first = Run(() => client.PrepareAsync(client.Charts[0]));
                File.WriteAllBytes(first.AudioPath, new byte[] { 9, 9, 9 });
            }
            using var otherApi = new FanmadeFixture();
            otherApi.Charts.Add(new FanmadeFixture.Chart { Tja = chart.Tja, Audio = chart.Audio });
            var other = client.Add(otherApi.Server());
            Run(() => client.ConnectAsync(other, true));
            var selected = client.Charts.ToArray();
            // Both requests finish verifying the same missing/damaged object before either publishes.
            using var ready = new System.Threading.Barrier(2);
            Action<DownloadProgress> progress = p => {
                if (p.Audio.Status == FileProgress.State.Downloading && p.Audio.Received == 0 &&
                    !ready.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Download barrier");
            };
            var results = Run(() => Task.WhenAll(selected.Select(c => client.PrepareAsync(c, progress: progress))));
            Assert.That(results[0].AudioPath, Is.EqualTo(results[1].AudioPath));
            Assert.That(File.ReadAllBytes(results[0].AudioPath), Is.EqualTo(chart.Audio));
            Assert.That(Directory.GetFiles(cache, "*.part", SearchOption.AllDirectories), Is.Empty);
        }

        [Test] public void ManifestWithoutExpiryDownloadsWithoutRefresh()
        {
            var result = Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(result.Tja, Does.Contain("#START"));
            Assert.That(File.ReadAllBytes(result.AudioPath), Is.EqualTo(chart.Audio));
            Assert.That((manifests, tjaGets, audioGets, forbidden), Is.EqualTo((1, 1, 1, 0)));
        }
        [TestCase(403)] [TestCase(404)] [TestCase(503)]
        public void ResourceErrorsAreBoundedAndNeverLoginOrProxy(int status)
        {
            ResourceStatus = status;
            Assert.Throws<HttpStatusException>(() => Run(() => client.PrepareAsync(client.Charts[0])));
            Assert.That(manifests, Is.EqualTo(2)); Assert.That(tjaGets, Is.EqualTo(2));
            Assert.That(api.Requests.Count(x => x.Contains("login")), Is.EqualTo(1)); Assert.That(api.Downloads, Is.Zero);
        }
        [Test] public void HashMismatchNeverPublishesPlayablePair()
        {
            corrupt = true;
            Assert.That(Assert.Throws<FanmadeException>(() => Run(() => client.PrepareAsync(client.Charts[0]))).Message, Is.EqualTo("DOWNLOAD_INTEGRITY_FAILED"));
            string objects = Path.Combine(cache, "objects");
            Assert.That(Directory.Exists(objects) ? Directory.GetFiles(objects, "*", SearchOption.AllDirectories) : new string[0], Is.Empty);
        }
        [Test] public void DetailManifestRaceIsBounded()
        {
            mismatch = true;
            Assert.That(Assert.Throws<FanmadeException>(() => Run(() => client.PrepareAsync(client.Charts[0]))).Message, Is.EqualTo("CHART_UPDATING"));
            Assert.That(manifests, Is.EqualTo(2)); Assert.That(tjaGets + audioGets, Is.Zero);
        }
        [Test] public void ChangedFilesStopOldUploads()
        {
            var c = client.Charts[0]; api.ScoreFailures.Enqueue(503);
            client.Submit(c, 3, new FanmadeScore { Score = 99, ClearStatus = 1 }); Run(() => client.WaitForUploadsAsync());
            var queued = client.UploadQueue.Pending(endpoint.Id).Single();
            Assert.That(JObject.Parse(queued.Body).ContainsKey("versionId"), Is.False);
            Assert.That(queued.TjaHash, Is.EqualTo(c.TjaHash));
            chart.Audio = new byte[] { 10 }; client.RetryNow(); Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.Zero); Assert.That(client.UploadQueue.RejectedCount(endpoint.Id), Is.EqualTo(1));
            Assert.That(api.AcceptedScores, Is.Empty);
        }
        [Test] public void BestScoresFollowTheLatestBootstrap()
        {
            api.AddAccountScore("don", chart, "Oni", 500, 3);
            Run(() => client.ConnectAsync(endpoint, false));
            Assert.That(client.Best(client.Charts[0], 3).ClearStatus, Is.EqualTo(3));
            api.AccountScores.Clear(); Run(() => client.ConnectAsync(endpoint, false));
            Assert.That(client.Best(client.Charts[0], 3), Is.Null);
        }
        [Test] public void QueuedScoresWithoutFileHashesAreKeptAsRejected()
        {
            client.UploadQueue.Enqueue(endpoint.Id, new string('c', 64), "{\"songId\":\"" + chart.Id + "\"}");
            client.RetryNow(); Run(() => client.WaitForUploadsAsync());
            Assert.That(client.UploadQueue.RejectedCount(endpoint.Id), Is.EqualTo(1)); Assert.That(api.AcceptedScores, Is.Empty);
        }
        [Test] public void CoursesMatchStartPlayerInsteadOfArrayPosition()
        {
            chart.Difficulties.Clear(); chart.Difficulties.Add(("Oni", 9, "P2")); chart.Difficulties.Add(("Oni", 8, "P1"));
            chart.Tja = Encoding.UTF8.GetBytes("BPM:120\nCOURSE:Oni\n#START P1\n1,\n#END\n#START P2\n2,\n#END\n");
            Run(() => client.ConnectAsync(endpoint, false));
            var result = Run(() => client.PrepareAsync(client.Charts[0]));
            string text = result.Tja;
            Assert.That(TjaParser.Parse(text, "Oni_1p").Level, Is.EqualTo(8));
            Assert.That(TjaParser.Parse(text, "Oni_2p").Level, Is.EqualTo(9));
            var player2 = result.Chart.ForPlayer("P2");
            client.Submit(player2, 3, new FanmadeScore { Score = 123 }); Run(() => client.WaitForUploadsAsync());
            Assert.That(api.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That((string)body["difficulty"], Is.EqualTo("Oni_2p")); Assert.That(body.ContainsKey("versionId"), Is.False);
        }
    }
}
