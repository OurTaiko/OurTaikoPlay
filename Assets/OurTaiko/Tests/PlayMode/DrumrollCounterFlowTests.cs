using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class DrumrollCounterFlowTests
    {
        SongDefinition song;
        PlayScene play;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            if (song != null) { Object.Destroy(song.chart); Object.Destroy(song); }
            yield return null;
        }

        IEnumerator StartPlay(bool practice, bool auto)
        {
            song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Drumroll Counter\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:3,3\n#START\n"
                + "5008,\n6008,\n5008,\n7008,\n9008,\n1000,\n0000,\n#END");
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            SceneSwitcher.Instance.PracticeMode = practice;
            SceneSwitcher.Instance.Play(song, "Oni", auto);
            yield return WaitForPlay(practice);
            play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.drumrollCounter, Is.Not.Null);
            Assert.That(play.drumrollCounter.digitSprites, Is.EqualTo(play.balloonCounter.digitSprites),
                "The fan and balloon must share the same digit sprites.");
            var digit = play.drumrollCounter.digitSprites[0];
            Assert.That(digit.rect.size, Is.EqualTo(new Vector2(96, 112)));
            Assert.That(digit.texture.mipmapCount, Is.GreaterThan(1));
            Assert.That(digit.texture.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(play.drumrollCounter.IsVisible, Is.False);
            Assert.That(play.drumrollCounter.gameObject.activeSelf, Is.False);
        }

        static IEnumerator WaitForPlay(bool practice)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            string target = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
            while (SceneSwitcher.Instance.IsInputBlocked || SceneManager.GetActiveScene().name != target)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ManualRollsCountPerNoteAndRestartCleanly() => CheckManual(false);

        [UnityTest]
        public IEnumerator PracticeRollsCountPerNoteAndSeekClearsDisplay() => CheckManual(true);

        IEnumerator CheckManual(bool practice)
        {
            yield return StartPlay(practice, false);
            if (!play.IsPaused) play.TogglePause();
            play.pausePanel.SetActive(false);
            var view = play.drumrollCounter;
            var session = play.Session;
            double now = play.SongTime - (song.audioOffsetMs + (double)SettingManager.EnsureInstance().Settings.play.audioOffsetMs) / 1000;
            // Drive the actual judgment event with both drum colours; no UI-only counting path.
            for (int i = 0; i < 14; i++)
                Assert.That(session.Hit(i % 2 == 0, .1), Is.EqualTo(Judgment.Roll));
            Assert.That(view.Count, Is.EqualTo(14));
            Assert.That(view.NoteIndex, Is.EqualTo(0));
            Assert.That(view.visuals.alpha, Is.EqualTo(1));
            AssertDigits(view, "14");
            view.ShowTime(now + .05);
            var digit = view.number.GetChild(0).GetComponent<RectTransform>();
            Assert.That(digit.sizeDelta.y, Is.EqualTo(124).Within(.01));
            Assert.That(digit.anchoredPosition.y, Is.EqualTo(12).Within(.01));
            view.ShowTime(now + .2);
            Assert.That(digit.sizeDelta.y, Is.EqualTo(112).Within(.01));
            view.ShowTime(now + 2.532 + .083);
            Assert.That(view.visuals.alpha, Is.EqualTo(.5f).Within(.01));
            // A hit during fade restores opacity and restarts the animation.
            session.Hit(false, .2);
            Assert.That(view.Count, Is.EqualTo(15));
            Assert.That(view.visuals.alpha, Is.EqualTo(1));

            // A different long note starts at one, then crosses the two/three-digit boundary.
            session.Hit(true, 2.1);
            Assert.That(view.Count, Is.EqualTo(1));
            Assert.That(view.NoteIndex, Is.EqualTo(1));
            for (int i = 1; i < 100; i++) session.Hit(false, 2.1);
            Assert.That(view.Count, Is.EqualTo(100));
            AssertDigits(view, "100");
            session.Hit(false, 4.1);
            Assert.That(view.Count, Is.EqualTo(1));
            AssertDigits(view, "1");
            // Balloons, kusudama and regular notes must never replace the roll's count.
            session.Hit(false, 6.1);
            var balloonDigit = play.balloonCounter.number.GetChild(0).GetComponent<RectTransform>();
            play.balloonCounter.ShowTime(now + .2);
            Assert.That(balloonDigit.sizeDelta, Is.EqualTo(new Vector2(77, 90)),
                "Sharing a higher-resolution sheet must not enlarge balloon digits.");
            session.Hit(false, 8.1);
            session.Hit(false, 10);
            Assert.That(view.NoteIndex, Is.EqualTo(2));
            Assert.That(view.Count, Is.EqualTo(1));

            // The song clock is paused: wall time must not advance this feedback.
            float alpha = view.visuals.alpha;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(view.visuals.alpha, Is.EqualTo(alpha));
            if (practice)
            {
                play.MovePractice(1);
                Assert.That(view.IsVisible, Is.False);
                Assert.That(view.Count, Is.Zero);
            }
            else
            {
                view.ShowTime(now + 2.7);
                Assert.That(view.IsVisible, Is.False);
                Assert.That(view.gameObject.activeSelf, Is.False);
                // Restart while visible, to catch stale state carried to the new scene.
                view.RecordHit(2, 9, now);
                SceneSwitcher.Instance.Restart();
                yield return WaitForPlay(false);
                Assert.That(Object.FindFirstObjectByType<PlayScene>().drumrollCounter.Count, Is.Zero);
                Assert.That(Object.FindFirstObjectByType<PlayScene>().drumrollCounter.IsVisible, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator AutoplayUpdatesBothRollSizesInTheRenderedScene()
        {
            yield return StartPlay(false, true);
            var view = play.drumrollCounter;
            bool small = false, big = false;
            float deadline = Time.realtimeSinceStartup + 20;
            while (!small || !big)
            {
                yield return new WaitForEndOfFrame();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                if (!view.IsVisible) continue;
                Assert.That(view.Count, Is.EqualTo(play.Session.LongHits[view.NoteIndex]));
                Assert.That(view.visuals.alpha, Is.EqualTo(1));
                if (view.Count < 14) continue;
                if (view.NoteIndex == 0 && !small)
                {
                    TestCapture.Capture("DrumrollCounterSmall.png");
                    small = true;
                }
                else if (view.NoteIndex == 1 && !big)
                {
                    TestCapture.Capture("DrumrollCounterBig.png");
                    TestCapture.Capture("DrumrollCounter720.png", 1280, 720);
                    big = true;
                }
            }
        }

        static void AssertDigits(DrumrollCounterView view, string expected)
        {
            var digits = view.number.GetComponentsInChildren<UnityEngine.UI.Image>();
            Assert.That(digits.Select(d => d.sprite),
                Is.EqualTo(expected.Select(c => view.digitSprites[c - '0'])));
            Assert.That(digits.All(d => !d.raycastTarget), Is.True);
            Assert.That(view.bubble.raycastTarget || view.visuals.blocksRaycasts, Is.False);
        }
    }
}
