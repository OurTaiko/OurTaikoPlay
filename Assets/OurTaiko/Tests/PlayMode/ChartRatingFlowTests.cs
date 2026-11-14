using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The result screen's "how was this chart?" prompt (ChartRatingPrompt, ResultScene.Rating.cs)
    // against the fixture server: when it is asked, its three answers and the vote each one sends.
    // ゴーストバトル's and マルチ演奏's results ask it in their own flow tests.
    public sealed class ChartRatingFlowTests
    {
        FanmadeFixture fixture;
        FanmadeFixture.Chart chart;
        string language;

        [SetUp]
        public void SetUp()
        {
            language = L10n.Language;
            fixture = new FanmadeFixture { Votes = true };
            chart = new FanmadeFixture.Chart
            {
                Title = "Rated Song", Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja("Oni", 1, 240)),
                Audio = File.ReadAllBytes("Assets/OurTaiko/Audio/don.ogg"),
            };
            fixture.Charts.Add(chart);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("ChartRatingTearDown"));
            yield return SceneManager.UnloadSceneAsync(previous);
            fixture.Dispose();
            TestData.UseServers();
            L10n.SetLanguage(language);
        }

        // Don presses until the result is left or asks its question. True when it asked.
        public static IEnumerator Leave(ResultScene result, float seconds = 20)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!result.IsLeaving && !(result.Rating?.IsOpen ?? false))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The result neither left nor asked.");
                if (result.RatingKnown) result.Don();
                yield return null;
            }
        }

        IEnumerator Result(SongDefinition song, bool autoPlay = false, PlayResult second = null)
        {
            yield return Waits.Until(() => !SceneSwitcher.Instance.IsInputBlocked);
            SceneSwitcher.Instance.ShowResult(new PlayResult
            {
                Title = "Rated Song", Course = "Oni", Difficulty = Difficulty.Oni, Level = 8, Score = 876540, Good = 3, Ok = 1, MaxCombo = 4,
                GaugePoints = 9000, AutoPlay = autoPlay,
            }, song, null, second);
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);
            yield return null;
        }

        static ResultScene Scene => Object.FindAnyObjectByType<ResultScene>();
        string Vote => "don/" + chart.Id;

        [UnityTest]
        public IEnumerator LeavingAResultAsksOnceAndSendsTheAnswer()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect("don");
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var online = OnlineManager.Instance;
            var song = online.Songs.Single(s => online.ChartOf(s).Id == chart.Id);

            // The first leave asks: liked it is focused, then not now, then did not like it.
            L10n.SetLanguage("zh");
            yield return Result(song);
            var result = Scene;
            yield return Leave(result);
            Assert.That(result.IsLeaving, Is.False);
            var prompt = result.Rating;
            Assert.That(prompt.Focused, Is.EqualTo(ChartRatingPrompt.Good), "The focus starts on liked it.");
            Assert.That(prompt.AllText, Does.Contain(L10n.Get("Result", "rating.title")).And.Contain("Rated Song"));
            string[] answers = { L10n.Get("Result", "rating.good"), L10n.Get("Result", "rating.skip"), L10n.Get("Result", "rating.bad") };
            Assert.That(prompt.AllText.Split('\n').Where(answers.Contains), Is.EqualTo(answers), "Left to right.");
            yield return null;
            TestCapture.Capture("ResultRating.png");
            TestCapture.Capture("ResultRatingTablet.png", 2048, 1536);
            TestCapture.Capture("ResultRatingPhone.png", 2622, 1206);
            result.Don(); result.ChangePage(1);
            Assert.That(prompt.IsOpen && !result.IsLeaving, Is.True, "The screen under the prompt takes nothing.");
            prompt.Move(1);
            Assert.That(prompt.Focused, Is.EqualTo(ChartRatingPrompt.Skip));
            prompt.Move(1);
            Assert.That(prompt.Focused, Is.EqualTo(ChartRatingPrompt.Bad));
            yield return null;
            TestCapture.Capture("ResultRatingBad.png");
            prompt.Move(-2);
            prompt.Press();
            Assert.That(prompt.IsOpen, Is.False);
            Assert.That(result.IsLeaving, Is.True, "An answer leaves the result.");
            yield return Waits.Until(() => fixture.ChartVotes.TryGetValue(Vote, out int value) && value == 1, "The vote did not arrive.");
            yield return Waits.Until(() => SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);

            // A chart the player has voted on is not asked about again.
            yield return Result(song);
            yield return Leave(Scene);
            Assert.That(Scene.Rating, Is.Null);
            yield return Waits.Until(() => SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);

            // Not now sends nothing, and the next play asks again; did not like it is the down vote.
            fixture.ChartVotes.Clear();
            int puts = fixture.Requests.Count(r => r.StartsWith("PUT "));
            yield return Result(song);
            yield return Leave(Scene);
            Scene.Rating.Choose(ChartRatingPrompt.Skip);
            Assert.That(Scene.IsLeaving, Is.True);
            yield return Waits.Until(() => SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);
            Assert.That(fixture.Requests.Count(r => r.StartsWith("PUT ")), Is.EqualTo(puts));
            yield return Result(song);
            yield return Leave(Scene);
            Scene.Rating.Choose(ChartRatingPrompt.Bad);
            yield return Waits.Until(() => fixture.ChartVotes.TryGetValue(Vote, out int value) && value == -1, "The down vote did not arrive.");
            yield return Waits.Until(() => SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);

            // Not asked: auto play, two players at one screen, a server without the call.
            fixture.ChartVotes.Clear();
            yield return Result(song, autoPlay: true);
            yield return Leave(Scene);
            Assert.That(Scene.Rating, Is.Null, "Auto play.");
            yield return Waits.Until(() => SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene && !SceneSwitcher.Instance.IsSwitching);
            fixture.Votes = false;
            yield return Result(song);
            yield return Leave(Scene);
            Assert.That(Scene.Rating, Is.Null, "A server without votes.");
        }

        [UnityTest]
        public IEnumerator AGuestIsNotAsked()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestData.UseServers(fixture);
            yield return TestData.Connect();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var online = OnlineManager.Instance;
            var song = online.Songs.Single(s => online.ChartOf(s).Id == chart.Id);
            yield return Result(song);
            var result = Scene;
            yield return Leave(result);
            Assert.That(result.Rating, Is.Null, "Without an account there is no vote to give.");
            Assert.That(result.IsLeaving, Is.True);
            Assert.That(fixture.Requests.Where(r => r.EndsWith("/vote")), Is.Empty, "The server is not asked either.");
        }
    }
}
