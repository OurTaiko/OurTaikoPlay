using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Issue #5: 2P joins with its own don on Entry or SongSelect, shows its nameplate, and picks its
    // own course and options before both seats play.
    public sealed class TwoPlayerMenuFlowTests
    {
        PlayOptions originalFirst, originalSecond;

        [SetUp]
        public void SetUp()
        {
            originalFirst = PlayOptions.Shared; originalSecond = PlayOptions.Second;
            PlayOptions.Shared = new PlayOptions(); PlayOptions.Second = new PlayOptions();
        }

        [TearDown]
        public void TearDown()
        {
            PlayOptions.Shared = originalFirst; PlayOptions.Second = originalSecond;
            if (SceneSwitcher.Instance != null) SceneSwitcher.Instance.TwoPlayers = false;
        }

        [UnityTest]
        public IEnumerator SecondPlayerJoinsOnEntry()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            Assert.That(SceneSwitcher.Instance.TwoPlayers, Is.False);
            Assert.That(entry.view.nameplateGroup2P.alpha, Is.Zero);

            entry.Join2P();
            Assert.That(entry.Joined2P, Is.True);
            Assert.That(entry.voice.clip, Is.SameAs(entry.entryStart2P));
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(entry.view.nameplateGroup2P.alpha, Is.EqualTo(1));
            Assert.That(entry.view.nameplate2P.Info.name, Is.EqualTo("2P"));
            Assert.That(entry.view.nameplate2P.badge.sprite, Is.SameAs(entry.view.nameplate2P.secondBadge));

            entry.Don();
            yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 4);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(entry.Coins.BubbleAlpha, Is.Zero, "No 2P invite once 2P is in.");
            TestCapture.Capture("EntryTwoPlayers.png");
            entry.Don();
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.SongSelectScene);
            Assert.That(SceneSwitcher.Instance.TwoPlayers, Is.True);
            var select = Object.FindAnyObjectByType<SongSelectScene>();
            Assert.That(select.view.nameplateGroup2P.alpha, Is.EqualTo(1), "2P's plate is up when it joined on Entry.");
        }

        [UnityTest]
        public IEnumerator SecondPlayerJoinsOnSongSelectAndPicksItsOwnCourse()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.Instance;
            switcher.LastDifficulty = switcher.LastDifficulty2P = -1;
            switcher.SwitchScene(SceneSwitcher.SongSelectScene);
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (switcher.IsInputBlocked || SceneManager.GetActiveScene().name != SceneSwitcher.SongSelectScene);
            var select = Object.FindAnyObjectByType<SongSelectScene>();
            var manager = select.Manager;
            Assert.That(manager.TwoPlayers, Is.False);
            Assert.That(select.view.nameplateGroup2P.alpha, Is.Zero);

            manager.JoinSecond();
            Assert.That(manager.TwoPlayers, Is.True);
            Assert.That(select.voice.clip, Is.SameAs(select.secondJoined));
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(select.view.nameplateGroup2P.alpha, Is.EqualTo(1));

            manager.OpenFolderAt(0); manager.Navigate(2);
            Assert.That(manager.FocusedSong.name, Is.EqualTo(TestSongs.TripleHelix));
            yield return new WaitForSecondsRealtime(0.6f);
            manager.Confirm(1);
            Assert.That(manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect), "2P's don picks the song too.");
            Assert.That(manager.CursorOf(1), Is.Not.Null);
            yield return WaitUntil(() => select.CourseFade >= 1, 3);

            // 1P walks to Easy, 2P to Hard; each moves only its own cursor.
            manager.Right(); manager.Right();
            Assert.That(manager.Cursor.Selected, Is.EqualTo(Difficulty.Easy));
            Assert.That(manager.CursorOf(1).Selected, Is.EqualTo(Difficulty.Back));
            for (int i = 0; i < 4; i++) manager.Right(1);
            Assert.That(manager.CursorOf(1).Selected, Is.EqualTo(Difficulty.Hard));
            Assert.That(manager.Cursor.Selected, Is.EqualTo(Difficulty.Easy));

            // 2P opens its own options on the right; オート is 2P's alone.
            for (int i = 0; i < 3; i++) manager.Left(1);
            Assert.That(manager.CursorOf(1).Selected, Is.EqualTo(Difficulty.Modifier));
            yield return null;
            // On a button 2P shows the small button glow, as 1P does, not the course frame.
            Assert.That(select.view.glow2P.enabled && !select.view.frame2P.enabled, Is.True,
                $"glow2P {select.view.glow2P.enabled} active {select.view.glow2P.gameObject.activeInHierarchy} frame2P {select.view.frame2P.enabled} glow {select.view.glow.enabled}");
            Assert.That(Centre(select.view.glow2P).x, Is.EqualTo(Centre(select.view.option).x).Within(0.01f), "The glow sits on オプション.");
            TestCapture.Capture("SongSelectTwoPlayerButtonGlow.png");
            manager.Confirm(1);
            Assert.That(manager.IsOptionPanelOpenFor(1) && !manager.IsOptionPanelOpen, Is.True);
            Assert.That(select.voice.clip, Is.SameAs(select.voiceOptions2P));
            manager.Right(1);
            Assert.That(PlayOptions.Second.auto && !PlayOptions.Shared.auto, Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            TestCapture.Capture("SongSelectTwoPlayerOptions.png");
            for (int i = 0; i < OptionMenu.Rows.Length; i++) manager.Confirm(1);
            yield return WaitUntil(() => !manager.IsOptionPanelOpenFor(1), 2);
            for (int i = 0; i < 3; i++) manager.Right(1);
            Assert.That(manager.CursorOf(1).Selected, Is.EqualTo(Difficulty.Hard));
            yield return null;
            TestCapture.Capture("SongSelectTwoPlayerCourses.png");

            // 1P decides first and waits; the song starts once 2P has decided too.
            manager.Confirm();
            Assert.That(manager.IsReady(0) && !manager.IsReady(1), Is.True);
            Assert.That(manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            manager.Right();
            Assert.That(manager.Cursor.Selected, Is.EqualTo(Difficulty.Easy), "A decided seat no longer moves.");
            manager.Confirm(1);
            Assert.That(manager.Phase, Is.EqualTo(SongSelectManager.State.Decided));
            deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (switcher.SelectedSong == null || SceneManager.GetActiveScene().name == SceneSwitcher.SongSelectScene);
            Assert.That(SongInfo.DifficultyOf(switcher.SelectedCourse), Is.EqualTo(Difficulty.Easy));
            Assert.That(SongInfo.DifficultyOf(switcher.SelectedCourse2P), Is.EqualTo(Difficulty.Hard));
            Assert.That(switcher.AutoPlay2P, Is.True);
            Assert.That(switcher.AutoPlay, Is.False);
            Assert.That(switcher.SelectedPlayScene, Is.EqualTo(SceneSwitcher.DoublePlayScene));
        }

        static Vector3 Centre(UnityEngine.UI.Graphic graphic)
        {
            var corners = new Vector3[4];
            graphic.rectTransform.GetWorldCorners(corners);
            return (corners[0] + corners[2]) / 2;
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
