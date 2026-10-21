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

        IEnumerator OpenAccounts(System.Action<GlobalSettingScene> use)
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
            yield return null;
            var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
            scene.Ka(4);
            Assert.That(scene.Menu.CurrentType.Label, Is.EqualTo("OurTaiko Accounts"));
            scene.Don();
            use(scene);
        }

        // Selects item `index` of the open type from wherever the focus is among the items.
        static void Select(GlobalSettingScene scene, int index) => scene.Ka(index - scene.Menu.ItemIndex);

        static IEnumerator Type(GlobalSettingScene scene, int index, string text)
        {
            Select(scene, index);
            scene.Don();
            Assert.That(scene.Menu.EditingText, Is.True);
            yield return null;
            scene.view.textField.text = text;
            scene.view.textField.onSubmit.Invoke(text);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachSeatSignsInWithAPasswordThatIsNeverSaved()
        {
            GlobalSettingScene scene = null;
            yield return OpenAccounts(s => scene = s);
            var view = scene.view;
            Assert.That(Enumerable.Range(0, 4).Select(i => view.itemRows[i].label.text),
                Is.EqualTo(new[] { "1P Username", "1P Password", "1P Sign Out", "2P Username" }));
            TestCapture.Capture("SettingsAccounts.png");
            Assert.That(view.itemRows[2].value.text, Is.EqualTo("Not signed in"));

            // The song list first: it names the account center the server takes accounts from.
            Select(scene, 6);
            Assert.That(scene.Menu.CurrentItem.Label, Is.EqualTo("Refresh Song List"));
            scene.Don();
            yield return WaitUntil(() => scene.Menu.CurrentItem.Status == "Done");
            Assert.That(online.Songs.Count, Is.EqualTo(1));

            // A password without a username asks for the username.
            yield return Type(scene, 1, "katsu");
            Assert.That(view.outputStatus.text, Is.EqualTo("Enter 1P's username first."));
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/account/login"));

            yield return Type(scene, 0, "don");
            Assert.That(view.itemRows[0].value.text, Is.EqualTo("don"));
            Select(scene, 1);
            scene.Don();
            Assert.That(view.textField.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
            Assert.That(view.textField.text, Is.Empty);
            yield return null;
            view.textField.text = "katsu";
            TestCapture.Capture("SettingsAccountField.png");
            view.textField.onSubmit.Invoke("katsu");
            yield return WaitUntil(() => online.Accounts.Seats[0].IsSignedIn && !online.IsBusy && scene.Menu.Types[4].Items[2].Status.StartsWith("Signed in"));
            yield return null;
            Assert.That(view.outputStatus.text, Does.StartWith("1P signed in as DON."));
            Assert.That(view.itemRows[1].value.text, Is.EqualTo("Type to sign in"), "The password row keeps nothing.");
            Assert.That(view.itemRows[2].value.text, Is.EqualTo("Signed in: DON"));
            Assert.That(PlayerInfoController.Instance.Info.name, Is.EqualTo("DON"), "The nickname goes on the nameplate.");
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/login"), "No password goes to the game server.");
            Assert.That(online.Client.Endpoints.Single().IsAuthenticated, Is.True);
            var listed = online.ChartOf(online.Songs[0]);
            Assert.That(online.Client.Best(listed, (int)Difficulty.Oni)?.Score, Is.EqualTo(1002540));
            Assert.That(online.Client.Playlists.Select(p => p.Title), Is.EqualTo(new[] { "Mine" }));
            string saved = File.ReadAllText(SettingManager.Instance.FilePath);
            Assert.That(saved, Does.Not.Contain("katsu"), "The password is not in settings.json.");
            Assert.That(GameSettings.FromJson(saved).accounts.p1Username, Is.EqualTo("don"));
            TestCapture.Capture("SettingsAccountButtons.png");

            // A wrong password says so and keeps the sign-in there is.
            yield return Type(scene, 1, "wrong");
            yield return WaitUntil(() => view.outputStatus.text.Contains("wrong username or password"));
            Assert.That(online.Accounts.Seats[0].IsSignedIn, Is.True);

            // 2P signs in with an account of its own; its plate shows its nickname.
            yield return Type(scene, 3, "ka");
            yield return Type(scene, 4, "don");
            yield return WaitUntil(() => online.Accounts.Seats[1].IsSignedIn && !online.IsBusy && scene.Menu.Types[4].Items[5].Status.StartsWith("Signed in"));
            Assert.That(PlayerInfoController.Instance.SecondInfo.name, Is.EqualTo("KA"));
            Assert.That(online.Client.Endpoints.Single().Seats[1].IsAuthenticated, Is.True);
            Assert.That(online.Client.Best(listed, (int)Difficulty.Oni, 1), Is.Null, "2P has none of 1P's scores.");

            // Signing 1P out forgets it here and at the account center; 2P stays.
            Select(scene, 2);
            scene.Don();
            yield return WaitUntil(() => !online.Accounts.Seats[0].IsSignedIn && scene.Menu.Types[4].Items[2].Status == "Not signed in");
            Assert.That(view.outputStatus.text, Is.EqualTo("1P signed out."));
            Assert.That(online.Client.Best(listed, (int)Difficulty.Oni), Is.Null, "A guest has no scores.");
            Assert.That(online.Client.Playlists, Is.Empty, "A guest has no playlists of its own.");
            Assert.That(PlayerInfoController.Instance.Info.name, Is.EqualTo(PlayerInfo.DefaultName));
            Assert.That(fixture.AccountTokens.Values, Is.EqualTo(new[] { "ka" }));
            Assert.That(online.Client.Endpoints.Single().Seats[1].IsAuthenticated, Is.True);
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
            var entry = Object.FindFirstObjectByType<EntryScene>();
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
