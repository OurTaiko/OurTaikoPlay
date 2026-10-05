using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class OnlinePreviewFlowTests
    {
        FanmadeFixture api, origin;
        FanmadeEndpoint endpoint;
        long downloaded;
        int requests, wrongMethod, delayMs;
        byte[] audio;
        [SetUp] public void Setup()
        {
            downloaded = requests = wrongMethod = delayMs = 0;
            api = new FanmadeFixture { SongIdOnly = true, CourseKeyed = true, ResourceDownloadVersion = 1 };
            origin = new FanmadeFixture();
            audio = File.ReadAllBytes("Assets/OurTaiko/Audio/song_select/bgm.ogg");
            api.Charts.Add(new FanmadeFixture.Chart { Title = "Streaming preview", Audio = audio, DemoStart = 15,
                Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()) });
            api.Manifest = c =>
            {
                JObject R(byte[] bytes, string type) => new JObject { ["url"] = origin.BaseUrl + "/audio?get", ["headUrl"] = origin.BaseUrl + "/audio?head",
                    ["sha256"] = FanmadeFixture.Sha(bytes), ["size"] = bytes.Length, ["contentType"] = type };
                return new JObject { ["chartId"] = c.Id, ["expiresAt"] = DateTime.UtcNow.AddMinutes(15).ToString("o"),
                    ["resources"] = new JObject { ["tja"] = R(c.Tja, "application/octet-stream"), ["audio"] = R(c.Audio, "audio/ogg") } };
            };
            origin.CustomRequest = ctx =>
            {
                var r = ctx.Response; r.Headers["ETag"] = "\"object-etag\"";
                if (ctx.Request.HttpMethod == "HEAD") { r.ContentLength64 = audio.Length; r.Close(); return true; }
                Interlocked.Increment(ref requests);
                if (delayMs > 0) Thread.Sleep(delayMs);
                string range = ctx.Request.Headers["Range"];
                if (range == null) { Interlocked.Increment(ref wrongMethod); r.StatusCode = 400; r.Close(); return true; }
                string[] parts = range.Substring(6).Split('-'); int start = int.Parse(parts[0]), end = int.Parse(parts[1]);
                r.StatusCode = 206; r.Headers["Content-Range"] = "bytes " + start + "-" + end + "/" + audio.Length;
                r.ContentLength64 = end - start + 1; r.OutputStream.Write(audio, start, end - start + 1); r.Close();
                Interlocked.Add(ref downloaded, end - start + 1); return true;
            };
            TestData.UseServers(new ServerList());
            endpoint = OnlineManager.Instance.Client.Add(api.Server()); endpoint.AllowLoopbackResourcesForTests = true;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var old = SceneManager.GetActiveScene(); SceneManager.SetActiveScene(SceneManager.CreateScene("PreviewTearDown"));
            yield return SceneManager.UnloadSceneAsync(old);
            api.Dispose(); origin.Dispose(); TestData.UseServers(new ServerList());
        }
        [UnityTest] public IEnumerator DecoderSeeksDemoStartAndPlaysOnlyAnExcerpt()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            var online = OnlineManager.Instance;
            var connect = online.Client.ConnectAsync(endpoint, true);
            while (!connect.IsCompleted) yield return null;
            Assert.That(connect.IsCompletedSuccessfully, Is.True, connect.Exception?.ToString());
            var engine = AudioEngine.EnsureInstance(); Assert.That(engine.Native, Is.True, engine.Diagnostics);
            int generation = engine.Generation;
            using var cancel = new CancellationTokenSource();
            var task = Task.Run(() => OnlinePreviewDecoder.PrepareAsync(endpoint, online.Client.Charts[0], engine, generation, cancel.Token));
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (!task.IsCompleted) cancel.Cancel();
            Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString());
            using var sample = task.Result;
            Assert.That(sample.Length, Is.InRange(11.9, 12.1));
            Assert.That(downloaded, Is.LessThan(audio.Length)); Assert.That(wrongMethod, Is.Zero);
            sample.Play(0.1f, false); yield return new WaitForSecondsRealtime(.2f);
            Assert.That(sample.Playing, Is.True); sample.Stop();
        }
        [UnityTest] public IEnumerator SwitchingWhileRangeIsPendingCancelsTheOldPreview()
        {
            delayMs = 600;
            var online = OnlineManager.Instance;
            var connect = online.Client.ConnectAsync(endpoint, true); while (!connect.IsCompleted) yield return null;
            Assert.That(connect.IsCompletedSuccessfully, Is.True, connect.Exception?.ToString());
            online.RefreshSongs();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(1);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            int folder = -1;
            for (int i = 0; i < select.BoardCount; i++) if (select.KindAt(i) == SongSelectScene.BoardKind.Folder) { folder = i; break; }
            Assert.That(folder, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < select.BoardCount && select.Focused != folder; i++) { select.Right(); yield return new WaitForSecondsRealtime(.3f); }
            Assert.That(select.Focused, Is.EqualTo(folder));
            yield return new WaitForSecondsRealtime(.8f); select.Confirm();
            yield return new WaitForSecondsRealtime(.5f); select.Right();
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (requests == 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(requests, Is.GreaterThan(0)); Assert.That(select.IsPreviewPlaying, Is.False);
            select.Left(); int atCancel = requests;
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(select.IsPreviewPlaying, Is.False); Assert.That(requests, Is.EqualTo(atCancel));
        }
        [UnityTest] public IEnumerator SongSelectionPlaysPreviewAndMovingAwayStopsIt()
        {
            var online = OnlineManager.Instance;
            var connect = online.Client.ConnectAsync(endpoint, true); while (!connect.IsCompleted) yield return null;
            Assert.That(connect.IsCompletedSuccessfully, Is.True, connect.Exception?.ToString());
            online.RefreshSongs();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(1);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            int folder = -1;
            for (int i = 0; i < select.BoardCount; i++) if (select.KindAt(i) == SongSelectScene.BoardKind.Folder) { folder = i; break; }
            Assert.That(folder, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < select.BoardCount && select.Focused != folder; i++) { select.Right(); yield return new WaitForSecondsRealtime(.3f); }
            Assert.That(select.Focused, Is.EqualTo(folder));
            yield return new WaitForSecondsRealtime(.8f); select.Confirm();
            yield return new WaitForSecondsRealtime(.5f); select.Right();
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (!select.IsPreviewPlaying && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(select.IsPreviewPlaying, Is.True); Assert.That(online.IsOnline(select.FocusedSong), Is.True);
            select.Left(); yield return new WaitForSecondsRealtime(.3f);
            Assert.That(select.IsPreviewPlaying, Is.False);
            int atStop = requests; yield return new WaitForSecondsRealtime(.3f); Assert.That(requests, Is.EqualTo(atStop));
            Assert.That(wrongMethod, Is.Zero);
        }
    }
}
