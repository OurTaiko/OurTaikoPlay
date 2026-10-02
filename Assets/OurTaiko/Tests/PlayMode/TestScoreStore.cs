using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Keeps every PlayMode test away from the player's real scores.json and options.json.
    [SetUpFixture]
    public sealed class TestScoreStore
    {
        string path;

        [OneTimeSetUp]
        public void UseTemporaryStore()
        {
            path = Path.Combine(Application.temporaryCachePath, "playmode-scores.json");
            if (File.Exists(path)) File.Delete(path);
            ScoreStore.Shared = new ScoreStore(path);
            PlayOptions.Shared = new PlayOptions();
        }

        [OneTimeTearDown]
        public void RemoveTemporaryStore()
        {
            ScoreStore.Shared = null;
            PlayOptions.Shared = null;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
