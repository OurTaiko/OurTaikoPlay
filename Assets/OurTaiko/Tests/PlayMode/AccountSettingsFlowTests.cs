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
    // Settings › OurTaiko Accounts: the popup's text field, saved to settings.json on Enter.
    public sealed class AccountSettingsFlowTests
    {
        string directory;

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Application.temporaryCachePath, "accounts-" + System.Guid.NewGuid());
            SettingManager.EnsureInstance().Load(Path.Combine(directory, "settings.json"));
        }

        [TearDown]
        public void Cleanup()
        {
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [UnityTest]
        public IEnumerator TheAccountIsTypedInThePopupAndSavedOnSubmit()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
            yield return null;
            var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
            var view = scene.view;
            Assert.That(view.textField.gameObject.activeSelf, Is.False);
            scene.Ka(4);
            Assert.That(scene.Menu.CurrentType.Label, Is.EqualTo("OurTaiko Accounts"));
            TestCapture.Capture("SettingsAccounts.png");
            scene.Don();
            Assert.That(view.itemRows[0].label.text, Is.EqualTo("1P Username"));
            Assert.That(view.itemRows[1].label.text, Is.EqualTo("1P Password"));
            Assert.That(view.itemRows[2].label.text, Is.EqualTo("Refresh Song List"), "2P is not listed yet.");
            Assert.That(view.itemRows[3].label.text, Is.EqualTo("Refresh Login Status"));

            scene.Don();
            Assert.That(scene.Menu.EditingText, Is.True);
            Assert.That(view.textField.gameObject.activeInHierarchy, Is.True);
            Assert.That(view.choiceRows[0].root.gameObject.activeSelf, Is.False);
            Assert.That(view.textField.contentType, Is.EqualTo(TMP_InputField.ContentType.Standard));
            yield return null;
            view.textField.text = "don";
            TestCapture.Capture("SettingsAccountField.png");
            view.textField.onSubmit.Invoke(view.textField.text);
            Assert.That(scene.Menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(view.textField.gameObject.activeSelf, Is.False);
            Assert.That(view.itemRows[0].value.text, Is.EqualTo("don"));
            yield return null;

            scene.Ka(1); scene.Don();
            Assert.That(view.textField.contentType, Is.EqualTo(TMP_InputField.ContentType.Password));
            Assert.That(view.textField.text, Is.Empty);
            yield return null;
            view.textField.text = "katsu";
            view.textField.onSubmit.Invoke(view.textField.text);
            Assert.That(view.itemRows[1].value.text, Is.EqualTo("*****"));
            yield return null;

            // Ending the edit without Enter keeps the saved value.
            scene.Don();
            view.textField.text = "other";
            view.textField.onEndEdit.Invoke(view.textField.text);
            Assert.That(scene.Menu.Focus, Is.EqualTo(SettingsFocus.Items));
            Assert.That(view.itemRows[1].value.text, Is.EqualTo("*****"));

            var saved = GameSettings.FromJson(File.ReadAllText(SettingManager.Instance.FilePath));
            Assert.That(saved.accounts.p1Username, Is.EqualTo("don"));
            Assert.That(saved.accounts.p1Password, Is.EqualTo("katsu"));
        }

        // The two buttons against a built-in fixture server: the login alone asks for no song list,
        // puts the nickname on the nameplate, and the song list is a request of its own.
        [UnityTest]
        public IEnumerator TheButtonsRefreshTheLoginAndTheSongListSeparately()
        {
            var fixture = new FanmadeFixture();
            var chart = new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()) };
            fixture.Charts.Add(chart);
            fixture.AddAccountScore("don", chart, "Oni", 1002540, 1);
            fixture.AccountPlaylists = new Newtonsoft.Json.Linq.JArray
            {
                new Newtonsoft.Json.Linq.JObject { ["id"] = "mine", ["title"] = "Mine", ["genre"] = "GAME", ["kind"] = "owned", ["owner"] = "DON",
                    ["chartIds"] = new Newtonsoft.Json.Linq.JArray(chart.Id) },
            };
            var online = OnlineManager.EnsureInstance();
            try
            {
                var settings = new GameSettings();
                settings.accounts.p1Username = "don"; settings.accounts.p1Password = "katsu";
                SettingManager.EnsureInstance().UseUnsaved(settings);
                TestData.UseServers(new ServerList());
                online.UseUnsaved(new ServerList(), TestData.OnlineCache, ("Built-in", fixture.BaseUrl));
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
                yield return null;
                var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
                var view = scene.view;
                scene.Ka(4); scene.Don();

                scene.Ka(3);
                Assert.That(scene.Menu.CurrentItem.Label, Is.EqualTo("Refresh Login Status"));
                scene.Don();
                Assert.That(scene.Menu.CurrentItem.Status, Is.EqualTo("Working…"));
                yield return WaitUntil(() => scene.Menu.CurrentItem.Status == "Done");
                Assert.That(view.outputStatus.text, Is.EqualTo("Built-in: signed in as DON"));
                Assert.That(PlayerInfoController.Instance.Info.name, Is.EqualTo("DON"), "The nickname goes on the nameplate.");
                Assert.That(online.Client.Endpoints.Single().IsAuthenticated, Is.True);
                Assert.That(fixture.Requests, Has.None.EqualTo("GET /api/v1/game/catalog"), "Signing in loads no song list.");
                Assert.That(fixture.Requests.Count(r => r == "GET /api/v1/game/scores"), Is.EqualTo(1), "The scores come with the login.");
                Assert.That(online.Songs, Is.Empty);

                scene.Ka(-1);
                scene.Don();
                yield return WaitUntil(() => scene.Menu.CurrentItem.Status == "Done");
                Assert.That(view.outputStatus.text, Is.EqualTo("Built-in: 1 songs"));
                Assert.That(online.Songs.Count, Is.EqualTo(1));
                Assert.That(fixture.Requests.Count(r => r == "POST /api/v1/game/login"), Is.EqualTo(1), "The song list did not sign in again.");
                var listed = online.ChartOf(online.Songs[0]);
                Assert.That(online.Client.Best(listed, (int)Difficulty.Oni)?.Score, Is.EqualTo(1002540), "Loading the song list keeps the scores.");
                Assert.That(online.Client.Playlists.Select(p => p.Title), Is.EqualTo(new[] { "Mine" }), "The account's playlists, received at login, show once the songs are there.");
                Assert.That(online.Folders.Select(f => f.Title), Does.Contain("Mine"));
                Assert.That(fixture.Requests.Count(r => r == "GET /api/v1/game/scores"), Is.EqualTo(1), "The song list asks for no scores.");
                TestCapture.Capture("SettingsAccountButtons.png");

                // A refused account keeps the song list and says so.
                scene.Menu.Settings.accounts.p1Password = "wrong";
                SettingManager.Instance.UseUnsaved(scene.Menu.Settings);
                scene.Ka(1);
                scene.Don();
                yield return WaitUntil(() => scene.Menu.CurrentItem.Status == "Done");
                Assert.That(view.outputStatus.text, Does.Contain("wrong username or password"));
                Assert.That(online.Client.Endpoints.Single().IsAuthenticated, Is.False);
                Assert.That(online.Songs.Count, Is.EqualTo(1));

                // Signing in once the song list is there brings the scores by themselves.
                scene.Menu.Settings.accounts.p1Username = ""; scene.Menu.Settings.accounts.p1Password = "";
                SettingManager.Instance.UseUnsaved(scene.Menu.Settings);
                scene.Don();
                yield return WaitUntil(() => scene.Menu.CurrentItem.Status == "Done");
                Assert.That(online.Client.Best(listed, (int)Difficulty.Oni), Is.Null, "A guest has no scores.");
                Assert.That(online.Client.Playlists, Is.Empty, "A guest has no playlists of its own.");
                int lists = fixture.Requests.Count(r => r == "GET /api/v1/game/catalog");
                scene.Menu.Settings.accounts.p1Username = "don"; scene.Menu.Settings.accounts.p1Password = "katsu";
                SettingManager.Instance.UseUnsaved(scene.Menu.Settings);
                scene.Don();
                yield return WaitUntil(() => scene.Menu.CurrentItem.Status == "Done");
                Assert.That(online.Client.Best(listed, (int)Difficulty.Oni)?.Score, Is.EqualTo(1002540), "The history returns with the login.");
                Assert.That(online.Client.Playlists.Select(p => p.Title), Is.EqualTo(new[] { "Mine" }));
                Assert.That(fixture.Requests.Count(r => r == "GET /api/v1/game/catalog"), Is.EqualTo(lists), "No song list was loaded for it.");
            }
            finally
            {
                PlayerInfoController.Instance.UseUnsaved(new PlayerInfo());
                TestData.UseServers(new ServerList());
                fixture.Dispose();
            }
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds = 10)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True);
        }
    }
}
