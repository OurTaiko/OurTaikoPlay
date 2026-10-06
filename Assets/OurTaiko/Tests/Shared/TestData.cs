using System.IO;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Keeps PlayMode tests away from the player's real scores.sqlite3, options.json, player.json, settings.json,
    // servers.json, online cache and songs folder; tests start with no online server (ServerLogin passes straight
    // through) and the test songs (TestSongs) as the only local songs.
    // Each test assembly calls this from its own [SetUpFixture].
    public static class TestData
    {
        static string ScorePath => Path.Combine(Application.temporaryCachePath, "playmode-scores.sqlite3");
        public static string OnlineCache => Path.Combine(Application.temporaryCachePath, "playmode-fanmade");

        public static void Use()
        {
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            ScoreStore.Shared = new ScoreStore(ScorePath);
            PlayOptions.Shared = new PlayOptions();
            PlayerInfoController.EnsureInstance().UseUnsaved(new PlayerInfo());
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            UseServers(new Online.ServerList());
            TestSongs.Install();
        }

        // Unsaved servers over a fresh temporary cache.
        public static void UseServers(Online.ServerList servers)
        {
            var manager = Online.OnlineManager.EnsureInstance();
            manager.Disconnect();
            SongSelectManager.EnsureInstance().Reset();
            if (Directory.Exists(OnlineCache)) Directory.Delete(OnlineCache, true);
            manager.UseUnsaved(servers, OnlineCache);
        }

        public static void Restore()
        {
            ScoreStore.Shared = null;
            PlayOptions.Shared = null;
            TestSongs.Uninstall();
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            if (Directory.Exists(OnlineCache)) Directory.Delete(OnlineCache, true);
        }
    }
}
