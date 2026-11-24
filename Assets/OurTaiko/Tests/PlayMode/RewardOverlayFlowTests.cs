using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The rewards a player was given (RewardInbox, RewardOverlay) against the fixture site: the
    // header on any answer brings them, they wait for a screen that allows them, come one at a time
    // over it, take its keys while up, and are marked seen when closed.
    public sealed class RewardOverlayFlowTests
    {
        FanmadeFixture fixture;
        string language;

        [SetUp]
        public void SetUp()
        {
            language = L10n.Language;
            fixture = new FanmadeFixture { RewardsOn = true };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RewardOverlay.Instance?.Reset();
            RewardInbox.Shared.Clear();
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("RewardOverlayTearDown"));
            yield return SceneManager.UnloadSceneAsync(previous);
            fixture.Dispose();
            TestData.UseServers();
            L10n.SetLanguage(language);
        }

        // Signed in as "don" through a site with one source, as the built-in servers are.
        IEnumerator Connect()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers();
            var online = OnlineManager.Instance;
            online.UseUnsavedSite(TestData.OnlineCache, fixture.BaseUrl, ("Fanmade", "https://fanmade.example", "fanmade"));
            online.UseAccounts(new AccountStore(null, "test"), new AccountCenter(fixture.BaseUrl));
            online.UseProfiles(new ProfileCenter(fixture.BaseUrl));
            yield return TestData.Connect("don");
        }

        static RewardOverlay Overlay => RewardOverlay.Instance;

        [UnityTest]
        public IEnumerator RewardsComeOneAtATimeOverAScreenThatAllowsThem()
        {
            Assert.That(Overlay, Is.Not.Null, "The overlay is there from the start.");
            L10n.SetLanguage("zh");
            long title = fixture.GiveReward("don", Reward.TitleKind, new JObject
            {
                ["title"] = new JObject { ["id"] = "saitama", ["name"] = "さいたま2000を極めし者", ["style"] = "rainbow", ["description"] = "おに フルコンボ",
                    ["nameTranslations"] = new JObject { ["zh-Hans"] = "埼玉2000大师" } },
                ["service"] = "rule", ["courseTitle"] = "",
            });
            long dan = fixture.GiveReward("don", Reward.DanKind, new JObject { ["index"] = 22, ["verdict"] = 5, ["gold"] = true, ["combo"] = 1 });
            yield return Connect();

            // Signing in heard of them; on a screen that does not allow them (a test's own) they wait.
            yield return Waits.Until(() => RewardInbox.Shared.Queue.Count == 2, "The rewards were not fetched.");
            SceneManager.SetActiveScene(SceneManager.CreateScene("NotAMenu"));
            float until = Time.realtimeSinceStartup + (float)RewardOverlay.QuietSeconds + 1f;
            while (Time.realtimeSinceStartup < until) { Assert.That(Overlay.IsShowing, Is.False, "Shown where it may not be."); yield return null; }

            // Entry allows them, once it has settled.
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return Waits.Until(() => Overlay.IsShowing, "The rewards did not come up on Entry.");
            Assert.That((Overlay.Count, Overlay.Index, Overlay.Current.Id), Is.EqualTo((2, 0, title)));
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.True, "The screen under it takes no keys.");
            yield return Waits.Until(() => Overlay.IsReady, "The title did not finish coming in.");
            // The title's name in the song language, as nameplates show it (ProfileNameplate.From).
            string songLanguage = SettingManager.EnsureInstance().Settings.general.Language;
            Assert.That(Overlay.AllText, Does.Contain(L10n.Get("Online", "rewards.title")).And.Contain(Overlay.Current.TitleName(songLanguage))
                .And.Contain(L10n.Get("Online", "rewards.fromRule")).And.Contain("おに フルコンボ").And.Contain("1 / 2").And.Contain(L10n.Get("Online", "rewards.next")));
            var scene = TestCapture.SceneCanvas();
            TestCapture.Capture("RewardTitle.png", canvas: scene, over: Overlay.Canvas);
            TestCapture.Capture("RewardTitleTablet.png", 2048, 1536, TestCapture.StageIsCentred(Overlay.Stage), scene, Overlay.Canvas);
            TestCapture.Capture("RewardTitlePhone.png", 2622, 1206, TestCapture.StageIsCentred(Overlay.Stage), scene, Overlay.Canvas);

            // Don: the 段位 badge, which a hit while it comes in shows at once.
            Overlay.Don();
            Assert.That((Overlay.Index, Overlay.Current.Id, Overlay.IsReady), Is.EqualTo((1, dan, false)));
            Overlay.Don();
            Assert.That(Overlay.IsReady, Is.True, "A hit while it comes in shows it at once.");
            Assert.That(Overlay.AllText, Does.Contain(L10n.Get("Online", "rewards.dan")).And.Contain(DanText.RankName(22))
                .And.Contain(L10n.Get("Online", "rewards.close")));
            yield return null;
            TestCapture.Capture("RewardDan.png", canvas: TestCapture.SceneCanvas(), over: Overlay.Canvas);
            Assert.That(fixture.SeenRewards, Is.Empty, "Nothing is marked before it closes.");

            // Don on the last: it closes, the press stays with it, and both are marked seen.
            Overlay.Don();
            Assert.That(Overlay.IsShowing, Is.False);
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.True, "The closing press does not reach the screen.");
            yield return Waits.Until(() => fixture.SeenRewards.Count == 2, "They were not marked seen.");
            Assert.That(fixture.SeenRewards.OrderBy(id => id), Is.EqualTo(new[] { title, dan }));
            Assert.That(fixture.PendingRewards("don"), Is.Zero);
            yield return null;
            Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.False, "The screen has its keys back.");
            Assert.That(RewardInbox.Shared.Queue, Is.Empty);
        }

        // One title that answers at once, for putting a reward in the inbox at a chosen moment.
        sealed class OneTitle : IRewardSite
        {
            public readonly List<long> Seen = new List<long>();
            bool given;
            public Task<string> FetchAsync(CancellationToken cancel)
            {
                var list = new JArray();
                if (!given) list.Add(new JObject { ["id"] = 99, ["kind"] = Reward.TitleKind,
                    ["data"] = new JObject { ["title"] = new JObject { ["id"] = "late", ["name"] = "Late Title", ["style"] = "teal" }, ["service"] = "rule" } });
                return Task.FromResult(new JObject { ["rewards"] = list }.ToString());
            }
            public Task MarkSeenAsync(IReadOnlyList<long> ids, CancellationToken cancel) { given = true; Seen.AddRange(ids); return Task.CompletedTask; }
            public bool Current => true;
        }

        [UnityTest]
        public IEnumerator OnAResultItWaitsUntilTheResultHasComeIn()
        {
            var chart = new FanmadeFixture.Chart
            {
                Title = "Reward Song", Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja("Oni", 1, 240)),
                Audio = File.ReadAllBytes("Assets/OurTaiko/Audio/don.ogg"),
            };
            fixture.Charts.Add(chart);
            yield return Connect();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return Waits.Until(() => SceneSwitcher.Instance != null && !SceneSwitcher.Instance.IsInputBlocked);
            var online = OnlineManager.Instance;
            var song = online.Songs.Single(x => online.ChartOf(x).Id == chart.Id);
            SceneSwitcher.Instance.ShowResult(song, new SeatResult(new PlayResult
            {
                Title = "Reward Song", Course = "Oni", Difficulty = Difficulty.Oni, Level = 8, Score = 876540, Good = 3, Ok = 1, MaxCombo = 4, GaugePoints = 9000,
            }, null));
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);
            var result = Object.FindAnyObjectByType<ResultScene>();

            // A title arrives as the result starts to come in: it waits for the scores and the crown.
            var site = new OneTitle();
            RewardInbox.Shared.Heard("test\ndon", 0, 1, site);
            yield return Waits.Until(() => RewardInbox.Shared.Queue.Count == 1, "The title was not fetched.");
            Assert.That(result.IsSettled, Is.False, "The result has only begun.");
            while (!result.IsSettled)
            {
                Assert.That(Overlay.IsShowing, Is.False, "Came up over the result's own show.");
                yield return null;
            }
            float settled = Time.realtimeSinceStartup;
            yield return Waits.Until(() => Overlay.IsShowing, "It did not come up once the result had come in.");
            Assert.That(Time.realtimeSinceStartup - settled, Is.GreaterThanOrEqualTo((float)RewardOverlay.QuietSeconds - 0.05f), "It waits a moment after.");
            Assert.That(result.IsLeaving, Is.False);
            Overlay.Don(); Overlay.Don();
            Assert.That(Overlay.IsShowing, Is.False);
            Assert.That(site.Seen, Is.EqualTo(new long[] { 99 }));
        }

        // A part of the 3D Don: its picture (by the path the site described it with, under the root
        // the site names; here a mirror that has lost it, so it comes from the canonical root) and its name.
        [UnityTest]
        public IEnumerator APartOfTheDonComesWithItsPicture()
        {
            var png = new Texture2D(8, 8);
            for (int x = 0; x < 8; x++) for (int y = 0; y < 8; y++) png.SetPixel(x, y, x < 4 ? Color.red : Color.white);
            byte[] picture = png.EncodeToPNG();
            Object.Destroy(png);
            int mirrored = 0;
            fixture.FilesRoot = fixture.BaseUrl + "/mirror/";
            fixture.CustomRequest = context =>
            {
                if (context.Request.Url.AbsolutePath == "/mirror/3dchara/icons/cos/30.png") { mirrored++; context.Response.StatusCode = 404; context.Response.Close(); return true; }
                if (context.Request.Url.AbsolutePath != "/3dchara/icons/cos/30.png") return false;
                context.Response.ContentType = "image/png";
                context.Response.OutputStream.Write(picture, 0, picture.Length);
                context.Response.OutputStream.Close();
                return true;
            };
            long part = fixture.GiveReward("don", Reward.DonItemKind, new JObject
            {
                ["part"] = "cos", ["itemId"] = 30, ["service"] = "admin",
                ["name"] = new JObject { ["ja"] = "鏡もち" }, ["3dCharaPath"] = "3dchara/icons/cos/30.png",
            });
            yield return Connect();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return Waits.Until(() => Overlay.IsShowing, "The part did not come up.");
            Assert.That(Overlay.Current.Id, Is.EqualTo(part));
            yield return Waits.Until(() => Overlay.IsReady);
            Assert.That(Overlay.AllText, Does.Contain(L10n.Get("Online", "rewards.donItem")).And.Contain(L10n.Get("Online", "rewards.donPart.cos") + "・鏡もち")
                .And.Contain(L10n.Get("Online", "rewards.fromAdmin")));
            yield return Waits.Until(() => Overlay.Canvas.GetComponentsInChildren<Image>().Any(i => i.name == "Picture" && i.enabled && i.sprite != null),
                "The picture did not come.");
            Assert.That(mirrored, Is.EqualTo(1), "Asked of the mirror first.");
            TestCapture.Capture("RewardDonItem.png", canvas: TestCapture.SceneCanvas(), over: Overlay.Canvas);
            Overlay.Don();
            Assert.That(Overlay.IsShowing, Is.False);
            yield return Waits.Until(() => fixture.SeenRewards.Contains(part), "It was not marked seen.");
        }

        // A server that names no site (an old domain, another server) tells and gives its own.
        [UnityTest]
        public IEnumerator AServerWithoutASiteGivesItsOwn()
        {
            long title = fixture.GiveReward("don", Reward.TitleKind, new JObject { ["title"] = new JObject { ["id"] = "own", ["name"] = "Own Title", ["style"] = "wood" } });
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect("don");
            Assert.That(OnlineManager.Instance.Client.Endpoints.Single().Config.site, Is.Empty, "This server names no site.");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return Waits.Until(() => Overlay.IsShowing, "Nothing came from a server without a site.");
            Assert.That(Overlay.Current.Id, Is.EqualTo(title));
            Overlay.Don(); Overlay.Don();
            yield return Waits.Until(() => fixture.SeenRewards.Contains(title), "It was not marked seen there.");
        }

        // Signed out with rewards waiting: they go, unmarked, and nobody else sees them.
        [UnityTest]
        public IEnumerator SigningOutTakesThePlayersRewardsAway()
        {
            long title = fixture.GiveReward("don", Reward.TitleKind, new JObject { ["title"] = new JObject { ["id"] = "mine", ["name"] = "Mine", ["style"] = "wood" } });
            yield return Connect();
            yield return Waits.Until(() => RewardInbox.Shared.Queue.Count == 1, "The reward was not fetched.");
            var signOut = OnlineManager.Instance.SignOutAsync(0);
            while (!signOut.IsCompleted) yield return null;
            yield return null;
            Assert.That(RewardInbox.Shared.Queue, Is.Empty, "A signed-out player's rewards stayed.");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            float until = Time.realtimeSinceStartup + (float)RewardOverlay.QuietSeconds + 1.5f;
            while (Time.realtimeSinceStartup < until) { Assert.That(Overlay.IsShowing, Is.False); yield return null; }
            Assert.That(fixture.SeenRewards, Is.Empty, "Marked seen though nobody saw it.");
            Assert.That(fixture.PendingRewards("don"), Is.EqualTo(1), "Still theirs to see.");
        }

        [UnityTest]
        public IEnumerator AKindTheGameDoesNotKnowIsDeliveredUnseen()
        {
            long odd = fixture.GiveReward("don", "frame", new JObject { ["id"] = "gold-frame" });
            yield return Connect();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return Waits.Until(() => fixture.SeenRewards.Contains(odd), "An unknown kind was not marked seen.");
            Assert.That(Overlay.IsShowing, Is.False);
            Assert.That(RewardInbox.Shared.Queue, Is.Empty);
        }
    }
}
