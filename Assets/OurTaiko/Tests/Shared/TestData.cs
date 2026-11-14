using System;
using System.Collections;
using System.IO;
using System.Linq;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Keeps PlayMode tests away from the player's real scores.sqlite3, options.json, player.json, settings.json,
    // accounts, online cache and songs folder; tests start with no online server and the test songs
    // (TestSongs) as the only local songs.
    // Each test assembly calls this from its own [SetUpFixture].
    public static class TestData
    {
        static string ScorePath => Path.Combine(Application.temporaryCachePath, "playmode-scores.sqlite3");
        public static string OnlineCache => Path.Combine(Application.temporaryCachePath, "playmode-fanmade");

        // With OURTAIKO_TEST_MUTE set, a test run makes no sound: the output runs as usual, at
        // volume zero. For a run on a machine somebody is working at. A test that measures how
        // loud something is cannot pass under it.
        public static void MuteIfAsked()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("OURTAIKO_TEST_MUTE"))) return;
            // By name: this assembly does not refer to the audio library.
            var bass = System.Type.GetType("ManagedBass.Bass, ManagedBass");
            foreach (string volume in new[] { "GlobalStreamVolume", "GlobalSampleVolume", "GlobalMusicVolume" })
                bass?.GetProperty(volume)?.SetValue(null, 0);
        }

        public static void Use()
        {
            MuteIfAsked();
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            ScoreStore.Shared = new ScoreStore(ScorePath);
            DanRecordStore.Shared = new DanRecordStore(ScorePath);
            PlayOptions.Shared = new PlayOptions();
            PlayerInfoController.EnsureInstance().UseUnsaved(new PlayerInfo());
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            UseServers();
            TestSongs.Install();
        }

        // The fixtures stand in for the built-in servers (none: no server at all), over a fresh
        // temporary cache and with nobody signed in. The first fixture is also the account center
        // and OurTaikoProfile. Nothing is requested until Connect.
        public static void UseServers(params FanmadeFixture[] fixtures)
        {
            var manager = OnlineManager.EnsureInstance();
            // A plate an account gave 1P goes with the account.
            bool signedIn = manager.Accounts != null && manager.Accounts.Seats.Any(seat => seat.IsSignedIn);
            manager.Disconnect();
            SongSelectManager.EnsureInstance().Reset();
            if (Directory.Exists(OnlineCache)) Directory.Delete(OnlineCache, true);
            manager.UseUnsaved(OnlineCache, fixtures.Select(fixture => (fixture.Name, fixture.BaseUrl)).ToArray());
            if (fixtures.Length > 0)
            {
                manager.UseAccounts(new AccountStore(null, "test"), new AccountCenter(fixtures[0].BaseUrl));
                manager.UseProfiles(new ProfileCenter(fixtures[0].BaseUrl));
            }
            if (signedIn)
            {
                PlayerInfoController.EnsureInstance().UseUnsaved(new PlayerInfo());
                PlayerInfoController.EnsureInstance().SetSecond(null);
            }
        }

        // Loads the servers' song lists as the game does at startup; with a user, 1P then signs in
        // with that OurTaiko account. Throws when a server did not answer or refused the account.
        public static IEnumerator Connect(string user = null, string password = "katsu")
        {
            var manager = OnlineManager.Instance;
            var load = manager.LoadSongListAsync();
            while (!load.IsCompleted) yield return null;
            load.GetAwaiter().GetResult();
            if (manager.Client.Endpoints.Any(e => !e.IsConnected)) throw new InvalidOperationException("Song list: " + manager.SongListSummary);
            if (user == null) yield break;
            var signIn = manager.SignInAsync(0, user, password);
            while (!signIn.IsCompleted) yield return null;
            signIn.GetAwaiter().GetResult();
            if (manager.Client.Endpoints.Any(e => !e.Seats[0].IsAuthenticated)) throw new InvalidOperationException("Sign-in: " + manager.LoginSummary);
        }

        public static void Restore()
        {
            ScoreStore.Shared = null;
            DanRecordStore.Shared = null;
            PlayOptions.Shared = null;
            TestSongs.Uninstall();
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            if (Directory.Exists(OnlineCache)) Directory.Delete(OnlineCache, true);
        }
    }
}
