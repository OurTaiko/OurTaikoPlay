using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SettingsBoardFlowTests
    {
        static SettingManager Settings => SettingManager.EnsureInstance();

        [TearDown]
        public void RestoreDefaults()
        {
            if (SettingsOverlay.Instance != null) Object.Destroy(SettingsOverlay.Instance.gameObject);
            Settings.UseUnsaved(new GameSettings());
        }

        [UnityTest]
        public IEnumerator EntrySettingsBoardOpensTheOverlayAndChangesApplyAtOnce()
        {
            Settings.UseUnsaved(new GameSettings());
            // The engine started with the player's settings file; closing would reload Entry to apply them.
            var apply = AudioEngine.EnsureInstance().ApplyPendingSettingsAsync();
            yield return WaitUntil(() => apply.IsCompleted, 10);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var entry = Object.FindAnyObjectByType<EntryScene>();
                Assert.That(entry.Modes.Length, Is.EqualTo(4));
                Assert.That(entry.Modes[3].Title, Is.EqualTo("ゲーム設定"));
                yield return new WaitForSecondsRealtime(0.2f);
                entry.Don();
                yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
                yield return new WaitForSecondsRealtime(1f);

                // 演奏ゲーム open at the centre, 段位道場 closed one slot below (kanban_3: +50, +305).
                var boards = entry.Board.Boards;
                Assert.That(boards[0].Openness, Is.EqualTo(1).Within(1e-3));
                Assert.That(boards[1].Openness, Is.EqualTo(0).Within(1e-3));
                Assert.That(boards[1].Position, Is.EqualTo(new Vector2(50, 305)));
                Assert.That(boards[1].Root.gameObject.activeSelf, Is.True);

                // Right ka slides the list up; the 段位道場 board opens after the slide.
                entry.Ka(1);
                Assert.That(entry.Flow.SelectedMode, Is.EqualTo(1));
                yield return new WaitForSecondsRealtime(0.08f);
                Assert.That(boards[1].Position.y, Is.InRange(1f, 304f), "Mid-slide.");
                Assert.That(boards[1].Openness, Is.EqualTo(0).Within(1e-3), "Opens only after the slide.");
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(boards[1].Position, Is.EqualTo(Vector2.zero));
                Assert.That(boards[0].Position, Is.EqualTo(new Vector2(-50, -305)));
                Assert.That(boards[1].Openness, Is.EqualTo(1).Within(1e-3));
                Assert.That(boards[0].Openness, Is.EqualTo(0).Within(1e-3));
                entry.Ka(1);
                entry.Ka(1);
                yield return new WaitForSecondsRealtime(0.6f);
                TestCapture.Capture("EntrySettingsBoard.png");
                entry.Ka(1);
                Assert.That(entry.Flow.SelectedMode, Is.EqualTo(3), "Clamped at the bottom.");

                entry.Don();
                Assert.That(SettingsOverlay.IsOpen, Is.True, "ゲーム設定 opens the overlay over Entry.");
                var overlay = SettingsOverlay.Instance;
                var nav = overlay.Navigator;
                var view = overlay.View;
                Assert.That(nav.Pages.Select(p => p.Title), Is.EqualTo(new[] { "General", "Controls", "Play", "Display", "Sound", "Accounts" }));
                yield return null;

                // Touch: tapping a category selects it, tapping it again opens it; tapping a row
                // focuses it and tapping it again confirms it. A row's own switch flips it at once.
                view.TapPage(2);
                Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rail));
                Assert.That(nav.Page.Title, Is.EqualTo("Play"));
                view.TapPage(2);
                Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
                view.TapRow(4);
                Assert.That(nav.Row.Id, Is.EqualTo("drum-pad"));
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.True);
                view.TapRow(4);
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.False, "Applied and saved through SettingManager.");
                view.SetRow(4, 1);
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.True);
                view.SetRow(0, 5);
                Assert.That(Settings.Settings.play.audioOffsetMs, Is.EqualTo(5), "A stepper button changes the number at once.");

                // Display › Frame Rate applies at once: 120 by default, 60 or Unlimited.
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
                view.TapPage(3);
                view.TapPage(3);
                Assert.That(nav.Row.Label, Is.EqualTo("Frame Rate"));
                overlay.Side(1);
                Assert.That(Settings.Settings.display.targetFrameRate, Is.EqualTo(60));
                Assert.That(Application.targetFrameRate, Is.EqualTo(60));
                view.SetRow(0, 2);
                Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
                Assert.That(QualitySettings.vSyncCount, Is.Zero);
                view.SetRow(0, 0);
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));

                // Display › VSync: off by default, applied at once.
                overlay.Move(1);
                Assert.That(nav.Row.Label, Is.EqualTo("VSync"));
                overlay.Confirm();
                Assert.That(Settings.Settings.display.vSync, Is.True);
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
                overlay.Confirm();
                Assert.That(QualitySettings.vSyncCount, Is.Zero);

                // Back twice closes the overlay; Entry is still on its mode list.
                overlay.Back();
                overlay.Back();
                Assert.That(SettingsOverlay.IsOpen, Is.False);
                yield return null;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.EntryScene));
                Assert.That(entry.Flow.SelectedMode, Is.EqualTo(3));
                Assert.That(SceneSwitcher.Instance.IsInputBlocked, Is.False);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator DrumPadSettingEnablesOrHidesTheTouchDrum([Values(true, false)] bool enabled)
        {
            Settings.UseUnsaved(new GameSettings { play = new PlaySettings { singlePlayerDrumPad = enabled } });
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                TestSongs.Select();
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
                yield return null;
                yield return null;
                var play = Object.FindAnyObjectByType<PlayScene>();
                Assert.That(play.drumPad, Is.Not.Null);
                Assert.That(play.drumPad.gameObject.activeSelf, Is.EqualTo(enabled));
                Assert.That(play.drumPad.isActiveAndEnabled, Is.EqualTo(enabled));
                if (!enabled) TestCapture.Capture("SinglePlayNoDrumPad.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
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
