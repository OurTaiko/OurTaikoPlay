using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class EntryFlowTests
    {
        [UnityTest]
        public IEnumerator CreditJoinModeSelectAndSongSelect()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            Assert.That(SceneSwitcher.MenuScene, Is.EqualTo(SceneSwitcher.EntryScene));
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.bgm.isPlaying, Is.True);

            // Credit screen: both rows fade in and blink; the timer stands at 60; no nameplate yet.
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(entry.Flow.State, Is.EqualTo(EntryFlow.Phase.SelectSide));
            Assert.That(entry.Credit.Alpha, Is.EqualTo(1).Within(1e-3));
            Assert.That(entry.Board.Root.gameObject.activeSelf, Is.False);
            Assert.That(entry.Timer.Seconds, Is.EqualTo(60));
            Assert.That(entry.Nameplate.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(entry.Coins.BubbleAlpha, Is.Zero, "No 2P invite while the credit rows are up.");
            Assert.That(entry.Coins.FreePlay.text, Is.EqualTo("フリープレイ"));
            Assert.That(entry.Guide.Image.color.a, Is.EqualTo(1));
            TestCapture.Capture("EntryCredit.png");

            // 1P joins: the joined row flashes, the nameplate fades in, the rows fade out.
            entry.Don();
            Assert.That(entry.Flow.State, Is.EqualTo(EntryFlow.Phase.SelectMode));
            Assert.That(entry.voice.clip, Is.SameAs(entry.entryStart));
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(entry.Credit.FlashAlpha + entry.Credit.Alpha, Is.GreaterThan(0));
            Assert.That(entry.Nameplate.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
            entry.Don();
            Assert.That(entry.Flow.IsSelected, Is.False, "The board takes no decide before it is up.");
            yield return WaitUntil(() => !entry.Credit.IsVisible, 3);
            Assert.That(entry.Flow.IsModeReady(entry.Now), Is.False, "The rows clear before the board opens.");
            yield return WaitUntil(() => entry.Coins.BubbleAlpha > 0, 2);

            // The board opens on mode_board `in`; select_mode is announced once the join voice ends.
            yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
            Assert.That(entry.Board.Root.gameObject.activeSelf, Is.True);
            yield return new WaitForSecondsRealtime(1f);
            Assert.That(entry.Board.Openness, Is.EqualTo(1).Within(1e-3));
            Assert.That(entry.Board.Fade, Is.EqualTo(1).Within(1e-3));
            Assert.That(entry.Timer.Seconds, Is.EqualTo(60), "The timer is a placeholder and never counts down.");
            TestCapture.Capture("EntryModeSelect.png");

            // Rim hits change nothing; a face hit plays `choose`, fades the board and opens SongSelect.
            entry.Ka();
            Assert.That(entry.Flow.IsSelected, Is.False);
            entry.Don();
            Assert.That(entry.Flow.IsSelected, Is.True);
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(entry.Board.ChooseFlash, Is.GreaterThan(0));
            Assert.That(entry.Board.Fade, Is.LessThan(1));
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.SongSelectScene);
            Assert.That(SceneSwitcher.LastScene, Is.EqualTo(SceneSwitcher.EntryScene));
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
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
