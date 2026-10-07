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
        bool failDownload;
        [SetUp] public void Setup()
        {
            downloaded = requests = wrongMethod = delayMs = 0; failDownload = false;
            api = new FanmadeFixture();
            origin = new FanmadeFixture();
            audio = File.ReadAllBytes("Assets/OurTaiko/Audio/song_select/bgm.ogg");
            api.Charts.Add(new FanmadeFixture.Chart { Title = "Streaming preview", Audio = audio, DemoStart = 15,
                Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()) });
            api.Manifest = c =>
            {
                JObject R(byte[] bytes, string type) => new JObject { ["url"] = origin.BaseUrl + "/audio?get", ["headUrl"] = origin.BaseUrl + "/audio?head",
                    ["sha256"] = FanmadeFixture.Sha(bytes), ["size"] = bytes.Length, ["contentType"] = type };
                return new JObject { ["chartId"] = c.Id, ["expiresAt"] = DateTime.UtcNow.AddMinutes(15).ToString("o"),
                    ["resources"] = new JObject { ["tja"] = R(c.Tja, "application/octet-stream"), ["audio"] = R(c.Audio, "audio/ogg"), ["preview"] = R(audio, "audio/ogg") } };
            };
            origin.CustomRequest = ctx =>
            {
                var r = ctx.Response;
                Interlocked.Increment(ref requests);
                if (delayMs > 0) Thread.Sleep(delayMs);
                if (ctx.Request.HttpMethod != "GET" || ctx.Request.Headers["Range"] != null) Interlocked.Increment(ref wrongMethod);
                if (failDownload) { r.StatusCode = 403; r.Close(); return true; }
                r.ContentLength64 = audio.Length; r.OutputStream.Write(audio, 0, audio.Length); r.Close();
                Interlocked.Add(ref downloaded, audio.Length); return true;
            };
            TestData.UseServers(new ServerList());
            endpoint = OnlineManager.Instance.Client.Add(api.Server());
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var old = SceneManager.GetActiveScene(); SceneManager.SetActiveScene(SceneManager.CreateScene("PreviewTearDown"));
            yield return SceneManager.UnloadSceneAsync(old);
            api.Dispose(); origin.Dispose(); TestData.UseServers(new ServerList());
        }
        [UnityTest] public IEnumerator SwitchingWhileDownloadIsPendingCancelsTheOldPreview()
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
            for (int i = 0; i < select.Manager.BoardCount; i++) if (select.Manager.KindAt(i) == SongSelectManager.ItemKind.Folder) { folder = i; break; }
            Assert.That(folder, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < select.Manager.BoardCount && select.Manager.Focused != folder; i++) { select.Manager.Right(); yield return new WaitForSecondsRealtime(.3f); }
            Assert.That(select.Manager.Focused, Is.EqualTo(folder));
            yield return new WaitForSecondsRealtime(.8f); select.Manager.Confirm();
            yield return new WaitForSecondsRealtime(.5f); select.Manager.Right();
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (requests == 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(requests, Is.GreaterThan(0)); Assert.That(select.IsPreviewPlaying, Is.False);
            select.Manager.Left(); int atCancel = requests;
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(select.IsPreviewPlaying, Is.False); Assert.That(requests, Is.EqualTo(atCancel));
            Assert.That(select.bgm.IsAudioPlaying(), Is.True);
        }
        [UnityTest] public IEnumerator NativeSelectionPlaysPreview() { yield return PlaySelection(); }
        [UnityTest] public IEnumerator FailedPreviewKeepsBgmPlaying() { failDownload = true; yield return PlaySelection(); }
        IEnumerator PlaySelection()
        {
            var settings = SettingManager.EnsureInstance();
            var saved = settings.Settings.Clone();
            var options = saved.Clone(); options.audio.backend = AudioBackend.BassSimple;
            settings.UseUnsaved(options);
            if (AudioEngine.Instance != null) Object.Destroy(AudioEngine.Instance.gameObject);
            yield return null;
            AudioEngine.EnsureInstance();
            try {
            var online = OnlineManager.Instance;
            var connect = online.Client.ConnectAsync(endpoint, true); while (!connect.IsCompleted) yield return null;
            Assert.That(connect.IsCompletedSuccessfully, Is.True, connect.Exception?.ToString());
            online.RefreshSongs();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(1);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            int folder = -1;
            for (int i = 0; i < select.Manager.BoardCount; i++) if (select.Manager.KindAt(i) == SongSelectManager.ItemKind.Folder) { folder = i; break; }
            Assert.That(folder, Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < select.Manager.BoardCount && select.Manager.Focused != folder; i++) { select.Manager.Right(); yield return new WaitForSecondsRealtime(.3f); }
            Assert.That(select.Manager.Focused, Is.EqualTo(folder));
            yield return new WaitForSecondsRealtime(.8f); select.Manager.Confirm();
            yield return new WaitForSecondsRealtime(.5f); select.Manager.Right();
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            if (failDownload) {
                while (requests < 2 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(requests, Is.EqualTo(2)); Assert.That(select.IsPreviewPlaying, Is.False);
                Assert.That(select.bgm.IsAudioPlaying(), Is.True); yield break;
            }
            while (!select.IsPreviewPlaying && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(select.IsPreviewPlaying, Is.True); Assert.That(online.IsOnline(select.Manager.FocusedSong), Is.True);
            Assert.That(select.preview.AudioPosition(), Is.LessThan(5), "Must start the excerpt at zero, not DEMOSTART=15");
            Assert.That(select.bgm.IsAudioPlaying(), Is.True);
            select.Manager.Left(); yield return new WaitForSecondsRealtime(.3f);
            Assert.That(select.IsPreviewPlaying, Is.False);
            int atStop = requests; yield return new WaitForSecondsRealtime(.3f); Assert.That(requests, Is.EqualTo(atStop));
            Assert.That(wrongMethod, Is.Zero);
            Assert.That(AudioEngine.Instance.Available, Is.True);
            } finally { settings.UseUnsaved(saved); if (AudioEngine.Instance != null) Object.Destroy(AudioEngine.Instance.gameObject); }
        }
    }
}
