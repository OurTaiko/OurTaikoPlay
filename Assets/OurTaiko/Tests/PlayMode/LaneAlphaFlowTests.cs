using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // #LANE_ALPHA in the real play scenes: the lane background, the branch route's tint and the
    // GOGO lane fade together; nothing else on the lane does.
    public sealed class LaneAlphaFlowTests
    {
        [UnityTest] public IEnumerator NormalPlayFadesTheLaneBackground() => Exercise(false);
        [UnityTest] public IEnumerator PracticeFadesTheLaneBackgroundWhileSeeking() => Exercise(true);

        IEnumerator Exercise(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            // BPM 240: 0 over 1–1.5 s, then (with GOGO) 40 % over 1.5–2 s.
            song.chart = new TextAsset("TITLE:Lane Alpha Test\nBPM:240\nCOURSE:Oni\nLEVEL:5\n#START\n1000,\n#LANE_ALPHA 0 500\n1000,\n"
                + "#GOGOSTART\n#LANE_ALPHA 40% -500\n1000,\n#GOGOEND\n1000,\n#END");
            try
            {
                SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.PracticeMode = practice;
                yield return TestPlay.Open(song, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 20;
                do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name !=
                    (practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene));
                var play = Object.FindAnyObjectByType<PlayScene>();
                while (play.Session == null) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                if (!play.IsPaused) play.TogglePause();
                play.enabled = false; // Keep the clock still while exercising the actual scene rendering path.
                play.pausePanel.SetActive(false);
                if (practice) play.practiceView.panel.SetActive(false);
                var render = typeof(PlayScene).GetMethod("RenderNotes", BindingFlags.NonPublic | BindingFlags.Instance);
                var lane = play.firstLane;
                var expected = new[] { lane.transform.Find("LaneBackground").gameObject, lane.branchLane.background.gameObject, lane.gogo.lane.gameObject };
                var times = new[] { 0.5, 1.25, 1.5, 1.75, 2.5, 1.25, 0.5 };
                var alphas = new[] { 1, 0.5f, 0, 0.2f, 0.4f, 0.5f, 1 };
                // Other groups on the lane (hidden counters…) keep whatever alpha they had at full opacity.
                System.Collections.Generic.Dictionary<CanvasGroup, float> others = null;
                for (int i = 0; i < times.Length; i++)
                {
                    render.Invoke(play, new object[] { times[i] });
                    foreach (var target in expected)
                        Assert.That(target.GetComponent<CanvasGroup>().alpha, Is.EqualTo(alphas[i]).Within(1e-5), $"{target.name} at {times[i]} s");
                    others ??= lane.GetComponentsInChildren<CanvasGroup>(true).Where(g => !expected.Contains(g.gameObject)).ToDictionary(g => g, g => g.alpha);
                    foreach (var other in others)
                        Assert.That(other.Key.alpha, Is.EqualTo(other.Value), $"{other.Key.name} is not the lane background ({times[i]} s).");
                    if (times[i] == 1.75)
                    {
                        yield return null;
                        TestCapture.Capture(practice ? "LaneAlphaPractice.png" : "LaneAlphaPlay.png");
                    }
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
        }
    }
}
