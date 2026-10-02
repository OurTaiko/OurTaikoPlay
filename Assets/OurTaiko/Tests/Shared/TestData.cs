using System.IO;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Keeps PlayMode tests away from the player's real scores.json, options.json, player.json and settings.json.
    // Each test assembly calls this from its own [SetUpFixture].
    public static class TestData
    {
        static string ScorePath => Path.Combine(Application.temporaryCachePath, "playmode-scores.json");

        public static void Use()
        {
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
            ScoreStore.Shared = new ScoreStore(ScorePath);
            PlayOptions.Shared = new PlayOptions();
            PlayerInfoController.EnsureInstance().UseUnsaved(new PlayerInfo());
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
        }

        public static void Restore()
        {
            ScoreStore.Shared = null;
            PlayOptions.Shared = null;
            if (File.Exists(ScorePath)) File.Delete(ScorePath);
        }
    }
}
