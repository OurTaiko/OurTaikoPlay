using System.IO;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Keeps PlayMode tests away from the player's real scores.json, options.json, player.json, settings.json,
    // servers.json and online cache; tests start with no online server (ServerLogin passes straight through).
    // Each test assembly calls this from its own [SetUpFixture].
    public static class TestData
    {
        static string ScorePath => Path.Combine(Application.temporaryCachePath, "playmode-scores.json");
        public static string OnlineCache => Path.Combine(Application.temporaryCachePath, "playmode-fanmade");

        public static void Use()
        {
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            ScoreStore.Shared = new ScoreStore(ScorePath);
            PlayOptions.Shared = new PlayOptions();
            PlayerInfoController.EnsureInstance().UseUnsaved(new PlayerInfo());
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            UseServers(new Online.ServerList());
        }

        // Unsaved servers over a fresh temporary cache.
        public static void UseServers(Online.ServerList servers)
        {
            Online.OnlineManager.EnsureInstance().UseUnsaved(servers, OnlineCache);
            if (Directory.Exists(OnlineCache)) Directory.Delete(OnlineCache, true);
        }

        public static void Restore()
        {
            ScoreStore.Shared = null;
            PlayOptions.Shared = null;
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            if (Directory.Exists(OnlineCache)) Directory.Delete(OnlineCache, true);
        }
    }
}
