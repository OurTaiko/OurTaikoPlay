using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OurTaiko.Online;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Settings › OurTaiko Accounts against a built-in fixture server that is also the account
    // center: each seat's username is saved, its password signs in at once and is never saved,
    // and a sign-out row per seat. Entry says when a remembered sign-in has expired.
    public sealed class AccountSettingsFlowTests
    {
        string directory;
        FanmadeFixture fixture;
        FanmadeFixture.Chart chart;
        OnlineManager online;

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Application.temporaryCachePath, "accounts-" + System.Guid.NewGuid());
            SettingManager.EnsureInstance().Load(Path.Combine(directory, "settings.json"));
            fixture = new FanmadeFixture();
            fixture.Users["ka"] = "don";
            chart = new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()) };
            fixture.Charts.Add(chart);
            fixture.AddAccountScore("don", chart, "Oni", 1002540, 1);
            fixture.AccountPlaylists = new Newtonsoft.Json.Linq.JArray
            {
                new Newtonsoft.Json.Linq.JObject { ["id"] = "mine", ["title"] = "Mine", ["genre"] = "GAME", ["kind"] = "owned", ["owner"] = "DON",
                    ["chartIds"] = new Newtonsoft.Json.Linq.JArray(chart.Id) },
            };
            online = OnlineManager.EnsureInstance();
            TestData.UseServers(new ServerList());
            online.UseUnsaved(new ServerList(), TestData.OnlineCache, ("Built-in", fixture.BaseUrl));
            online.UseAccounts(new AccountStore(null, "test"), new AccountCenter(fixture.BaseUrl));
            online.UseProfiles(new ProfileCenter(fixture.BaseUrl));
            fixture.Nameplates[FanmadeFixture.UserId("don")] = new Newtonsoft.Json.Linq.JObject
            {
                ["title"] = new Newtonsoft.Json.Linq.JObject { ["id"] = "master", ["name"] = "太鼓の達人", ["nameTranslations"] = new Newtonsoft.Json.Linq.JObject { ["en"] = "Taiko Master" }, ["style"] = "gold" },
                ["dan"] = new Newtonsoft.Json.Linq.JObject { ["index"] = 20, ["verdict"] = 5, ["gold"] = true },
            };
            fixture.Nameplates[FanmadeFixture.UserId("ka")] = new Newtonsoft.Json.Linq.JObject
            {
                ["title"] = new Newtonsoft.Json.Linq.JObject { ["id"] = "rainbow", ["name"] = "虹", ["nameTranslations"] = new Newtonsoft.Json.Linq.JObject(), ["style"] = "rainbow" },
                ["dan"] = null,
            };
        }

        [TearDown]
        public void Cleanup()
        {
            PlayerInfoController.Instance.UseUnsaved(new PlayerInfo());
            PlayerInfoController.Instance.SetSecond(null);
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            TestData.UseServers(new ServerList());
            fixture.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        IEnumerator OpenAccounts(System.Action<SettingsOverlay> use)
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var overlay = SettingsOverlay.Open();
            overlay.Move(5);
            Assert.That(overlay.Navigator.Page.Title, Is.EqualTo("Accounts"));
            overlay.Confirm();
            use(overlay);
        }

        // Focuses row `index` of the open page from wherever the focus is among its rows.
        static void Select(SettingsOverlay overlay, int index) => overlay.Move(index - overlay.Navigator.RowIndex);

        static IEnumerator Type(SettingsOverlay overlay, int index, string text)
        {
            Select(overlay, index);
            overlay.Confirm();
            Assert.That(overlay.Navigator.Zone, Is.EqualTo(SettingsZone.Typing));
            yield return null;
            overlay.View.Field.text = text;
            overlay.View.Field.onSubmit.Invoke(text);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachSeatSignsInWithAPasswordThatIsNeverSaved()
        {
            SettingsOverlay overlay = null;
            yield return OpenAccounts(o => overlay = o);
            var nav = overlay.Navigator;
            var rows = nav.Page.Rows;
            Assert.That(rows.Take(4).Select(r => r.Label), Is.EqualTo(new[] { "Username", "Password", "Sign Out", "Username" }));
            Assert.That(nav.Page.Groups.Select(g => g.Title), Is.EqualTo(new[] { "1P", "2P", "Servers" }));
            Assert.That(rows[2].Display(nav.Settings), Is.EqualTo("Not signed in"));

            // The song list first: it names the account center the server takes accounts from.
            Select(overlay, 6);
            Assert.That(nav.Row.Label, Is.EqualTo("Refresh Song List"));
            overlay.Confirm();
            yield return WaitUntil(() => nav.Row.Status == "Done");
            Assert.That(online.Songs.Count, Is.EqualTo(1));

            // A password without a username asks for the username.
            yield return Type(overlay, 1, "katsu");
            Assert.That(overlay.Status, Is.EqualTo("Enter 1P's username first."));
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/account/login"));

            yield return Type(overlay, 0, "don");
            Assert.That(rows[0].Display(nav.Settings), Is.EqualTo("don"));
            Select(overlay, 1);
            overlay.Confirm();
            yield return null;
            Assert.That(overlay.View.Field.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
            Assert.That(overlay.View.Field.text, Is.Empty);
            overlay.View.Field.text = "katsu";
            overlay.View.Field.onSubmit.Invoke("katsu");
            yield return WaitUntil(() => online.Accounts.Seats[0].IsSignedIn && !online.IsBusy && rows[2].Status.StartsWith("Signed in"));
            yield return null;
            Assert.That(overlay.Status, Does.StartWith("1P signed in as DON."));
            Assert.That(rows[1].Display(nav.Settings), Is.Empty, "The password row keeps nothing.");
            Assert.That(rows[2].Display(nav.Settings), Is.EqualTo("Signed in: DON"));
            var plate = PlayerInfoController.Instance.Info;
            Assert.That(plate.name, Is.EqualTo("DON"), "The nickname goes on the nameplate.");
            Assert.That(plate.title, Is.EqualTo("Taiko Master"), "The title Profile says 1P wears, in the song language (English by default).");
            Assert.That(plate.titleBackground, Is.EqualTo(4));
            Assert.That(plate.rainbow, Is.False);
            Assert.That(plate.dan, Is.EqualTo(20));
            Assert.That(plate.gold, Is.True);
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/login"), "No password goes to the game server.");
            Assert.That(online.Client.Endpoints.Single().IsAuthenticated, Is.True);
            var listed = online.ChartOf(online.Songs[0]);
            Assert.That(online.Client.Best(listed, (int)Difficulty.Oni)?.Score, Is.EqualTo(1002540));
            Assert.That(online.Client.Playlists.Select(p => p.Title), Is.EqualTo(new[] { "Mine" }));
            string saved = File.ReadAllText(SettingManager.Instance.FilePath);
            Assert.That(saved, Does.Not.Contain("katsu"), "The password is not in settings.json.");
            Assert.That(GameSettings.FromJson(saved).accounts.p1Username, Is.EqualTo("don"));

            // A wrong password says so and keeps the sign-in there is.
            yield return Type(overlay, 1, "wrong");
            yield return WaitUntil(() => overlay.Status.Contains("wrong username or password"));
            Assert.That(online.Accounts.Seats[0].IsSignedIn, Is.True);

            // 2P signs in with an account of its own; its plate shows its nickname.
            yield return Type(overlay, 3, "ka");
            yield return Type(overlay, 4, "don");
            yield return WaitUntil(() => online.Accounts.Seats[1].IsSignedIn && !online.IsBusy && rows[5].Status.StartsWith("Signed in"));
            var second = PlayerInfoController.Instance.SecondInfo;
            Assert.That(second.name, Is.EqualTo("KA"));
            Assert.That(second.title, Is.EqualTo("虹"));
            Assert.That(second.rainbow, Is.True);
            Assert.That(second.dan, Is.EqualTo(-1), "No 段位 shown.");
            Assert.That(online.Client.Endpoints.Single().Seats[1].IsAuthenticated, Is.True);
            Assert.That(online.Client.Best(listed, (int)Difficulty.Oni, 1), Is.Null, "2P has none of 1P's scores.");

            // Signing 1P out forgets it here and at the account center; 2P stays.
            Select(overlay, 2);
            overlay.Confirm();
            yield return WaitUntil(() => !online.Accounts.Seats[0].IsSignedIn && rows[2].Status == "Not signed in");
            Assert.That(overlay.Status, Is.EqualTo("1P signed out."));
            Assert.That(online.Client.Best(listed, (int)Difficulty.Oni), Is.Null, "A guest has no scores.");
            Assert.That(online.Client.Playlists, Is.Empty, "A guest has no playlists of its own.");
            Assert.That(PlayerInfoController.Instance.Info.name, Is.EqualTo(PlayerInfo.DefaultName));
            Assert.That(PlayerInfoController.Instance.Info.IsCoin, Is.True, "Signed out: the guest plate.");
            Assert.That(fixture.AccountTokens.Values, Is.EqualTo(new[] { "ka" }));
            Assert.That(online.Client.Endpoints.Single().Seats[1].IsAuthenticated, Is.True);
            Object.Destroy(overlay.gameObject);
        }

        // A sign-in the account center no longer knows (expired, signed out elsewhere) is dropped
        // and Entry says so; one it cannot reach is kept for later.
        [UnityTest]
        public IEnumerator EntrySaysWhenARememberedSignInHasExpired()
        {
            var load = online.LoadSongListAsync();
            yield return WaitUntil(() => load.IsCompleted);
            var signIn = online.SignInAsync(0, "don", "katsu");
            yield return WaitUntil(() => signIn.IsCompleted && !online.IsBusy);
            Assert.That(signIn.IsFaulted, Is.False, signIn.Exception?.ToString());
            Assert.That(online.Accounts.Seats[0].IsSignedIn, Is.True);

            // Unreachable: no expiry, the account stays.
            online.UseAccounts(online.Accounts, new AccountCenter("http://127.0.0.1:9"));
            var offline = online.LoginAsync();
            yield return WaitUntil(() => offline.IsCompleted);
            Assert.That(online.Accounts.Seats[0].IsSignedIn, Is.True);
            Assert.That(online.TakeExpired(0), Is.False);

            online.UseAccounts(online.Accounts, new AccountCenter(fixture.BaseUrl));
            fixture.AccountTokens.Clear();
            var check = online.LoginAsync();
            yield return WaitUntil(() => check.IsCompleted);
            Assert.That(online.Accounts.Seats[0].IsSignedIn, Is.False);
            Assert.That(online.Client.Endpoints.Single().IsAuthenticated, Is.False);

            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            Assert.That(entry.AccountNotice, Is.EqualTo("1P's OurTaiko sign-in has expired. Sign in again in the game settings."));
            yield return null;
            TestCapture.Capture("EntryAccountExpired.png");
            Assert.That(online.TakeExpired(0), Is.False, "Entry says it once.");
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds = 10)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True);
        }
    }
}
