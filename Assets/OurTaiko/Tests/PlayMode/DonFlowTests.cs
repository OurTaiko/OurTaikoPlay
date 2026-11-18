using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The 3D Don ported from YataiDON on the lane: it stands above the lane, cheers every tenth
    // combo, dances through GOGO TIME, holds a balloon while it is hit, and stays away with the
    // setting off.
    public sealed class DonFlowTests
    {
        const string Chart = "TITLE:Don Test\nBPM:240\nBALLOON:12\nCOURSE:Oni\nLEVEL:5\n#START\n" +
            "1111,\n1111,\n1111,\n#GOGOSTART\n1111,\n1111,\n#GOGOEND\n0,\n7000,\n0800,\n1000,\n0,\n0,\n#END";

        static IEnumerator OpenPlay(SongDefinition song, GameSettings settings)
        {
            SettingManager.EnsureInstance().UseUnsaved(settings);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            yield return TestPlay.Open(song, "Oni", true);
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene);
        }

        [UnityTest]
        public IEnumerator TheDonFollowsThePlay()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset(Chart);
            try
            {
                yield return OpenPlay(song, new GameSettings());
                var play = Object.FindAnyObjectByType<PlayScene>();
                float deadline = Time.realtimeSinceStartup + 20;
                while (play.Session == null) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                var lane = play.Lanes[0];
                Assert.That(lane.DonChara, Is.Not.Null, "The lane has a Don.");
                var view = lane.DonChara.View;
                Assert.That(view.Chara, Is.Not.Null);
                Assert.That(((RectTransform)view.transform).anchoredPosition, Is.EqualTo(new Vector2(248, -29)));

                var seen = new HashSet<DonAnim>();
                bool captured = false, capturedBalloon = false;
                Vector2 balloonSpot = default;
                while (!play.IsFinished && Time.realtimeSinceStartup < deadline + 15)
                {
                    yield return null;
                    var anim = (DonAnim)((int)view.Chara.Anim % (int)DonAnim.MirrorOffset);
                    seen.Add(anim);
                    if (!captured && anim == DonAnim.Combo) { captured = true; TestCapture.Capture("DonPlayCombo.png"); }
                    if (anim == DonAnim.BalloonLoop)
                    {
                        balloonSpot = ((RectTransform)view.transform).anchoredPosition;
                        if (!capturedBalloon) { capturedBalloon = true; TestCapture.Capture("DonPlayBalloon.png"); }
                    }
                }
                Assert.That(seen, Does.Contain(DonAnim.Combo), "Every tenth combo cheers.");
                Assert.That(seen.Contains(DonAnim.SabiStart) || seen.Contains(DonAnim.Sabi), "GOGO TIME dances.");
                Assert.That(seen, Does.Contain(DonAnim.BalloonLoop), "A balloon being hit is held.");
                Assert.That(balloonSpot, Is.EqualTo(new Vector2(630, -180)), "The Don holds the balloon at the skin's game_balloon.");
                Assert.That(seen, Does.Contain(DonAnim.BalloonSuccess), "A popped balloon cheers.");
                Assert.That(((RectTransform)view.transform).anchoredPosition, Is.EqualTo(new Vector2(248, -29)), "Back above the lane.");
                Assert.That(view.GetComponent<UnityEngine.UI.RawImage>().texture, Is.Not.Null);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }

        [UnityTest]
        public IEnumerator EachLaneOfTwoPlayersHasItsDon()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset(Chart);
            try
            {
                SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                while (SceneSwitcher.Instance.IsInputBlocked) yield return null;
                SceneSwitcher.Instance.SetSeats(Side.P1, Side.P2);
                yield return TestPlay.Open(song, "Oni", true, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 20;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.DoublePlayScene)
                { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                var play = Object.FindAnyObjectByType<PlayScene>();
                while (play.Lanes.Count < 2 || play.SongTime < 0.5) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                var first = play.Lanes[0].DonChara; var second = play.Lanes[1].DonChara;
                Assert.That(first, Is.Not.Null); Assert.That(second, Is.Not.Null);
                Assert.That(((RectTransform)first.View.transform).anchoredPosition, Is.EqualTo(new Vector2(248, -29)));
                Assert.That(((RectTransform)second.View.transform).anchoredPosition, Is.EqualTo(new Vector2(240, -558)), "game_p2: below the 2P lane.");
                Assert.That(first.View.GetComponent<UnityEngine.UI.RawImage>().enabled && second.View.GetComponent<UnityEngine.UI.RawImage>().enabled);
                TestCapture.Capture("DonDoublePlay.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }

        // A signed-in player's Don is downloaded on the Presented card, with a bar, before Entry.
        [UnityTest]
        public IEnumerator PresentedDownloadsTheSignedInPlayersDon()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            var manager = SettingManager.EnsureInstance();
            var site = new FanmadeFixture();
            var model = Resources.Load<GlbAsset>(DonLibrary.Root + "cos/0").bytes;
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = System.BitConverter.ToString(sha.ComputeHash(model)).Replace("-", "").ToLowerInvariant();
            string user = FanmadeFixture.UserId("don");
            site.CustomRequest = context =>
            {
                string path = context.Request.Url.AbsolutePath;
                byte[] body;
                if (path == "/api/v2/users/" + user + "/don")
                    body = System.Text.Encoding.UTF8.GetBytes("{\"look\":{\"costume\":true,\"cos\":88,\"head\":0,\"body\":0,\"puchi\":0,\"colors\":null},"
                        + "\"files\":[{\"kind\":\"cos\",\"id\":88,\"3dCharaPath\":\"3dchara/cos/88.glb\",\"size\":" + model.Length + ",\"sha256\":\"" + hash + "\"}]}");
                else if (path == "/3dchara/cos/88.glb")
                {
                    // Slowly, so the bar is seen moving.
                    context.Response.ContentLength64 = model.Length;
                    for (int at = 0; at < model.Length; at += 8192)
                    {
                        context.Response.OutputStream.Write(model, at, System.Math.Min(8192, model.Length - at));
                        context.Response.OutputStream.Flush();
                        Thread.Sleep(60);
                    }
                    context.Response.Close();
                    return true;
                }
                else return false;
                context.Response.ContentLength64 = body.Length;
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.Close();
                return true;
            };
            try
            {
                var settings = new GameSettings();
                settings.accounts.p1Username = "don"; settings.accounts.p1Password = "katsu";
                manager.UseUnsaved(settings);
                site.Name = "Built-in";
                TestData.UseServers(site);
                DonDownloads.Forget(0);
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.PresentedScene);
                var presented = Object.FindAnyObjectByType<PresentedScene>();
                presented.minimumSeconds = 1;
                float deadline = Time.realtimeSinceStartup + 30;
                while (!DonDownloads.Downloading || DonDownloads.Received == 0)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "the download never started");
                }
                yield return null;
                var bar = presented.progress.rectTransform.parent.Find("DonDownload");
                Assert.That(bar != null && bar.gameObject.activeSelf, Is.True, "the bar shows");
                Assert.That(presented.progress.text, Does.Contain("MB"));
                TestCapture.Capture("PresentedDonDownload.png");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.PresentedScene), "Entry waits for the Don");
                while (SceneManager.GetActiveScene().name != SceneSwitcher.EntryScene || SceneSwitcher.Instance.IsSwitching)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Entry never came");
                }
                Assert.That(DonLooks.For(0).cos, Is.EqualTo(88), "worn on arriving at Entry");
                for (int i = 0; i < 90; i++) yield return null;
                TestCapture.Capture("EntryDownloadedDon.png");
                Assert.That(DonLibrary.Part("cos", 88), Is.Not.Null);
            }
            finally
            {
                DonDownloads.Forget(0);
                PlayerInfoController.Instance.UseUnsaved(new PlayerInfo());
                manager.UseUnsaved(new GameSettings());
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                TestData.UseServers();
                site.Dispose();
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }

        [UnityTest]
        public IEnumerator TurnedOffThereIsNoDon()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset(Chart);
            try
            {
                var settings = new GameSettings();
                settings.display.chara3D = false;
                yield return OpenPlay(song, settings);
                var play = Object.FindAnyObjectByType<PlayScene>();
                float deadline = Time.realtimeSinceStartup + 20;
                while (play.Session == null) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                yield return null;
                Assert.That(play.Lanes[0].DonChara, Is.Null);
                Assert.That(Object.FindAnyObjectByType<DonView>(), Is.Null);
            }
            finally
            {
                // The tests after this one expect the default settings (the Don on).
                SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }
    }
}
