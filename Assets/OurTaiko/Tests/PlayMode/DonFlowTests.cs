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
    //
    // Not in Finished/: DrawingOnlyTheDonsPartOfItsAreaGivesTheSamePicture fails in batchmode on
    // macOS (2 pixels differ, 1 allowed) while it passes in the Editor, and EachLaneOfTwoPlayersHasItsDon
    // once ran past the 180 s test timeout; promote the class once both pass reliably.
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

                var counter = (RectTransform)play.firstLane.judgeCounter.transform;
                Assert.That(Drawn(view).Overlaps(World(counter)), Is.False, "The judge counter stands beside the Don, not over it.");
                var seen = new HashSet<DonAnim>();
                bool captured = false, capturedBalloon = false;
                Vector2 balloonSpot = default;
                int balloonOrder = int.MinValue;
                var donCanvas = view.GetComponent<Canvas>();
                int laneOrder = play.firstLane.GetComponent<Canvas>().sortingOrder;
                int foregroundOrder = play.firstLane.noteLayer.parent.parent.Find("LaneForeground").GetComponent<Canvas>().sortingOrder;
                int balloonCounterOrder = play.firstLane.balloonCounter.GetComponentInParent<Canvas>().sortingOrder;
                Assert.That(donCanvas.sortingOrder, Is.LessThan(laneOrder), "Standing above the lane, the Don is behind it.");
                while (!play.IsFinished && Time.realtimeSinceStartup < deadline + 15)
                {
                    yield return null;
                    var anim = (DonAnim)((int)view.Chara.Anim % (int)DonAnim.MirrorOffset);
                    seen.Add(anim);
                    if (!captured && anim == DonAnim.Combo) { captured = true; TestCapture.Capture("DonPlayCombo.png"); }
                    if (anim == DonAnim.BalloonLoop)
                    {
                        balloonSpot = ((RectTransform)view.transform).anchoredPosition;
                        balloonOrder = donCanvas.sortingOrder;
                        Assert.That(Drawn(view).Overlaps(World(counter)), Is.False, "Nor over the Don holding the balloon.");
                        Assert.That(World((RectTransform)play.firstLane.balloonCounter.transform).Overlaps(World(counter)), Is.False,
                            "Nor over the balloon counter.");
                        if (!capturedBalloon) { capturedBalloon = true; TestCapture.Capture("DonPlayBalloon.png"); }
                    }
                }
                Assert.That(seen, Does.Contain(DonAnim.Combo), "Every tenth combo cheers.");
                Assert.That(seen.Contains(DonAnim.SabiStart) || seen.Contains(DonAnim.Sabi), "GOGO TIME dances.");
                Assert.That(seen, Does.Contain(DonAnim.BalloonLoop), "A balloon being hit is held.");
                Assert.That(balloonSpot, Is.EqualTo(new Vector2(630, -180)), "The Don holds the balloon at the skin's game_balloon.");
                // Player::draw_overlays (issue #113): the Don holding the balloon is drawn over the lane,
                // its notes and its foreground, and under the balloon counter.
                Assert.That(balloonOrder, Is.GreaterThan(laneOrder).And.GreaterThan(foregroundOrder),
                    "The Don holding the balloon is not covered by the lane.");
                Assert.That(balloonOrder, Is.LessThan(balloonCounterOrder), "The balloon counter stays on top.");
                Assert.That(seen, Does.Contain(DonAnim.BalloonSuccess), "A popped balloon cheers.");
                Assert.That(((RectTransform)view.transform).anchoredPosition, Is.EqualTo(new Vector2(248, -29)), "Back above the lane.");
                Assert.That(donCanvas.sortingOrder, Is.LessThan(laneOrder), "And behind it again.");
                Assert.That(view.GetComponent<UnityEngine.UI.RawImage>().texture, Is.Not.Null);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }

        // A rect's area on the canvas, and the part of a Don's that its last render drew in, black
        // line included (Chara3D.Covered: units of the area, y down from its top left).
        static Rect World(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        static Rect Drawn(DonView view)
        {
            var rect = (RectTransform)view.transform;
            var area = World(rect);
            var covered = view.Chara.Covered;
            float sx = area.width / rect.rect.width, sy = area.height / rect.rect.height;
            return Rect.MinMaxRect(area.xMin + covered.xMin * sx, area.yMax - covered.yMax * sy,
                area.xMin + covered.xMax * sx, area.yMax - covered.yMin * sy);
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
                yield return TestPlay.Open(song, new SeatPlay("Oni", true), new SeatPlay("Oni", true));
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
                Assert.That(Drawn(first.View).Overlaps(World((RectTransform)play.firstLane.judgeCounter.transform)), Is.False);
                Assert.That(Drawn(second.View).Overlaps(World((RectTransform)play.secondLane.judgeCounter.transform)), Is.False,
                    "2P's judge counter stands beside 2P's Don.");
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
            var site = new OnlineFixture();
            var model = Resources.Load<GlbAsset>(DonLibrary.Root + "cos/0").bytes;
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = System.BitConverter.ToString(sha.ComputeHash(model)).Replace("-", "").ToLowerInvariant();
            string user = OnlineFixture.UserId("don");
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

        // The canvas draws the Don over only the part of its area it is in (DonImage.Shown), which
        // must give the picture drawing the whole area gives (to a rounding step), mirrored as well.
        [UnityTest]
        public IEnumerator DrawingOnlyTheDonsPartOfItsAreaGivesTheSamePicture()
        {
            var root = new GameObject("DonShownTest", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            backdrop.transform.SetParent(root.transform, false);
            var area = (RectTransform)backdrop.transform;
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.sizeDelta = Vector2.zero;
            backdrop.GetComponent<UnityEngine.UI.Image>().color = new Color(0.93f, 0.91f, 0.86f);
            try
            {
                // A clock that stands still: the Dons are not drawn again between the captures.
                var first = DonView.Create(root.transform, "Don1P", 560.3f, 800.7f);
                first.Clock = () => 0;
                first.Show(DonLook.Default()).SetAnim(DonAnim.Combo);
                var second = DonView.Create(root.transform, "Don2P", 1360, 800);
                second.Clock = () => 0;
                second.Show(DonLook.Default(1), true).SetAnim(DonAnim.Sabi);
                yield return null;
                yield return null;

                var images = new[] { first.GetComponent<DonImage>(), second.GetComponent<DonImage>() };
                foreach (var image in images)
                {
                    Assert.That(image.enabled && image.texture != null, "The Don is drawn.");
                    Assert.That(image.Shown.width * image.Shown.height, Is.GreaterThan(0).And.LessThan(0.35f), "Only the Don's part of the area is drawn.");
                }
                TestCapture.Capture("DonShownPart.png", canvas: canvas);
                foreach (var image in images) image.Shown = new Rect(0, 0, 1, 1);
                TestCapture.Capture("DonShownWhole.png", canvas: canvas);

                var part = new Texture2D(2, 2); var whole = new Texture2D(2, 2);
                part.LoadImage(System.IO.File.ReadAllBytes("TestResults/DonShownPart.png"));
                whole.LoadImage(System.IO.File.ReadAllBytes("TestResults/DonShownWhole.png"));
                Color32[] shown = part.GetPixels32(), all = whole.GetPixels32();
                Object.Destroy(part); Object.Destroy(whole);
                var back = all[0];
                Assert.That(all.Count(p => p.r != back.r || p.g != back.g || p.b != back.b), Is.GreaterThan(20000), "The capture shows no Don.");
                // The same texels through a smaller quad: the texture coordinates are interpolated
                // from other corners, which may round a channel of a pixel the other way.
                int differing = 0, largest = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(shown[i].r - all[i].r), Mathf.Abs(shown[i].g - all[i].g), Mathf.Abs(shown[i].b - all[i].b));
                    if (d > 0) differing++;
                    largest = Mathf.Max(largest, d);
                }
                Assert.That(largest, Is.LessThanOrEqualTo(1), "Drawing only the Don's part changed the picture.");
                Assert.That(differing, Is.LessThan(2000), "Drawing only the Don's part changed the picture.");
            }
            finally { Object.Destroy(root); }
        }
    }
}
