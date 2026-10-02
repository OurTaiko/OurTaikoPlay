using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class GlobalOverlayFlowTests
    {
        [UnityTest]
        public IEnumerator SongSelectShowsPlaceholderTimersChipAndInvite()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            var overlays = select.coursePanel.parent.Find("GlobalOverlays");
            Assert.That(overlays, Is.Not.Null);
            // draw_overlays comes after the player's course and option panels.
            Assert.That(overlays.GetSiblingIndex(), Is.EqualTo(select.coursePanel.GetSiblingIndex() + 1));
            Assert.That(select.Coins.FreePlay, Is.Null, "Song select shows no credit line.");
            Assert.That(select.Coins.QrChip.enabled, Is.True);
            Assert.That(select.Coins.HasInvite, Is.True);

            // Placeholders: 100 on the list, 60 in course select, and neither counts down.
            Assert.That(select.TimerView.Seconds, Is.EqualTo(100));
            Assert.That(overlays.Find("Timer/Digit2").GetComponent<UnityEngine.UI.Image>().enabled, Is.True, "Three digits.");
            yield return new WaitForSecondsRealtime(1.3f);
            Assert.That(select.TimerView.Seconds, Is.EqualTo(100));
            Assert.That(select.Coins.BubbleAlpha, Is.GreaterThan(0), "The 2P invite shows while songs played < 2.");
            TestCapture.Capture("OverlaySongSelect.png");
            select.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1);
            Assert.That(select.TimerView.Seconds, Is.EqualTo(60));
            Assert.That(overlays.Find("Timer/Digit2").GetComponent<UnityEngine.UI.Image>().enabled, Is.False);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(select.TimerView.Seconds, Is.EqualTo(60));
            TestCapture.Capture("OverlayCourseSelect.png");
            Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        [UnityTest]
        public IEnumerator ResultShowsTheCreditLineOverTheWipe()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.ResultScene);
            yield return null;
            var result = Object.FindFirstObjectByType<ResultScene>();
            Assert.That(result.Coins, Is.Not.Null);
            Assert.That(result.Coins.FreePlay.text, Is.EqualTo("フリープレイ"));
            Assert.That(result.Coins.QrChip, Is.Null);
            Assert.That(result.Coins.HasInvite, Is.False);
            var line = result.Coins.FreePlay.transform.parent;
            Assert.That(line.GetSiblingIndex(), Is.GreaterThan(result.stage.Find("FadeIn").GetSiblingIndex()));
            yield return new WaitForSecondsRealtime(1.5f);
            TestCapture.Capture("OverlayResult.png");
            Object.Destroy(SceneSwitcher.Instance.gameObject);
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds = 10)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }
    }
}
