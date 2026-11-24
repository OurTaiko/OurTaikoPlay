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
        public IEnumerator SongSelectShowsChipAndInviteWithoutATimer()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var select = Object.FindAnyObjectByType<SongSelectScene>();
            var overlays = select.coursePanel.parent.Find("GlobalOverlays");
            Assert.That(overlays, Is.Not.Null);
            // draw_overlays comes after the player's course and option panels.
            Assert.That(overlays.GetSiblingIndex(), Is.EqualTo(select.coursePanel.GetSiblingIndex() + 1));
            Assert.That(select.Coins.FreePlay, Is.Null, "Song select shows no credit line.");
            Assert.That(select.Coins.QrChip.enabled, Is.True);
            Assert.That(select.Coins.HasInvite, Is.True);

            Assert.That(overlays.Find("Timer"), Is.Null, "Song select shows no timer.");
            yield return new WaitForSecondsRealtime(1.3f);
            Assert.That(select.Coins.BubbleAlpha, Is.GreaterThan(0), "The 2P invite shows while songs played < 2.");
            TestCapture.Capture("OverlaySongSelect.png");
            select.Manager.OpenFolderAt(0); select.Manager.Navigate(2);
            select.Manager.Confirm();
            yield return WaitUntil(() => select.CourseFade >= 1);
            yield return new WaitForSecondsRealtime(1.2f);
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
            var result = Object.FindAnyObjectByType<ResultScene>();
            Assert.That(result.Coins, Is.Not.Null);
            Assert.That(result.Coins.FreePlay.text, Is.EqualTo(L10n.Get("Menu", "overlay.freePlay")));
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
