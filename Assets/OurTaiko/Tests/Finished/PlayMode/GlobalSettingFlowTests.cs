using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class GlobalSettingFlowTests
    {
        static SettingManager Settings => SettingManager.EnsureInstance();

        [TearDown]
        public void RestoreDefaults() => Settings.UseUnsaved(new GameSettings());

        [UnityTest]
        public IEnumerator EntrySettingsBoardOpensTheSettingsAndDrumKeysChangeTheDrumPad()
        {
            Settings.UseUnsaved(new GameSettings());
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var entry = Object.FindFirstObjectByType<EntryScene>();
                Assert.That(entry.Modes.Length, Is.EqualTo(3));
                Assert.That(entry.Modes[2].Title, Is.EqualTo("ゲーム設定"));
                yield return new WaitForSecondsRealtime(0.2f);
                entry.Don();
                yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
                yield return new WaitForSecondsRealtime(1f);

                // 演奏ゲーム open at the centre, 練習モード closed one slot below (kanban_3: +50, +305).
                var boards = entry.Board.Boards;
                Assert.That(boards[0].Openness, Is.EqualTo(1).Within(1e-3));
                Assert.That(boards[1].Openness, Is.EqualTo(0).Within(1e-3));
                Assert.That(boards[1].Position, Is.EqualTo(new Vector2(50, 305)));
                Assert.That(boards[1].Root.gameObject.activeSelf, Is.True);

                // Right ka slides the list up; the practice board opens after the slide.
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
                yield return new WaitForSecondsRealtime(0.6f);
                TestCapture.Capture("EntrySettingsBoard.png");
                entry.Ka(1);
                Assert.That(entry.Flow.SelectedMode, Is.EqualTo(2), "Clamped at the bottom.");

                entry.Don();
                yield return WaitForScene(SceneSwitcher.SettingScene);
                var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
                var menu = scene.Menu;
                var view = scene.view;
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
                Assert.That(view.typeRows[0].label.text, Is.EqualTo("General"));
                scene.Ka(1);
                Assert.That(view.typeRows[1].label.text, Is.EqualTo("Play"));
                Assert.That(view.typeRows[2].label.text, Is.EqualTo("Display"));
                Assert.That(view.typeRows[3].label.text, Is.EqualTo("Sound"));
                Assert.That(view.typeRows[4].label.text, Is.EqualTo("Return"));
                Assert.That(view.itemRows[0].label.text, Is.EqualTo("Enable Drumpad for Single Player Mode"));
                Assert.That(view.itemRows[0].value.text, Is.EqualTo("Enabled"));
                Assert.That(view.typeRows[1].box.sprite, Is.SameAs(view.typeBoxSelected));
                AssertPopup(view, false);
                TestCapture.Capture("SettingsTypes.png");

                // Drum keys: ka wraps over Return, don enters Play, don opens the choices.
                scene.Ka(-2);
                Assert.That(menu.IsTypeReturn, Is.True);
                Assert.That(view.itemRows[0].root.gameObject.activeSelf, Is.False, "Return has no items.");
                scene.Ka(2);
                scene.Don();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
                Assert.That(view.itemRows[0].box.sprite, Is.SameAs(view.itemBoxSelected));
                AssertPopup(view, false);
                scene.Don();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Choice));
                AssertPopup(view, true);
                Assert.That(view.detailTitle.text, Is.EqualTo("Enable Drumpad for Single Player Mode"));
                Assert.That(view.choiceRows[0].label.text, Is.EqualTo("Enabled"));
                Assert.That(view.choiceRows[1].label.text, Is.EqualTo("Disabled"));
                scene.Ka(1);
                Assert.That(view.choiceRows[1].box.sprite, Is.SameAs(view.choiceOn));
                TestCapture.Capture("SettingsChoice.png");
                scene.Don();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.False, "Applied and saved through SettingManager.");
                Assert.That(view.itemRows[0].value.text, Is.EqualTo("Disabled"));
                AssertPopup(view, false);

                // Touch: a tap on the shade closes the popup unchanged; a choice tap applies at once.
                scene.Don();
                AssertPopup(view, true);
                view.shadeClick.Clicked();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items));
                AssertPopup(view, false);
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.False);
                scene.Don();
                view.choiceRows[0].click.Clicked();
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.True);
                AssertPopup(view, false);
                view.itemSwipe.Swiped(1);
                Assert.That(menu.IsItemReturn, Is.True);
                view.typeSwipe.Swiped(-2);
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
                Assert.That(menu.IsTypeReturn, Is.True);
                view.typeRows[1].click.Clicked();
                view.typeRows[1].click.Clicked();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Items), "Tapping the focused type confirms it.");
                view.itemRows[0].click.Clicked();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Choice), "The focused item opens on one tap.");
                view.choiceRows[1].click.Clicked();
                Assert.That(Settings.Settings.play.singlePlayerDrumPad, Is.False);

                // Items' Return goes back to the types; the types' Return leaves for Entry.
                scene.Ka(1);
                scene.Don();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));

                // Display › Target Frame Rate applies at once: 120 FPS by default, 60 or Unlimited.
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
                scene.Ka(1);
                scene.Don();
                Assert.That(view.itemRows[0].label.text, Is.EqualTo("Target Frame Rate"));
                Assert.That(view.itemRows[0].value.text, Is.EqualTo("120 FPS"));
                scene.Don();
                Assert.That(view.choiceRows[2].label.text, Is.EqualTo("Unlimited"));
                Assert.That(view.choiceRows[2].root.gameObject.activeSelf, Is.True);
                scene.Ka(1);
                TestCapture.Capture("SettingsFrameRate.png");
                scene.Don();
                Assert.That(Settings.Settings.display.targetFrameRate, Is.EqualTo(60));
                Assert.That(Application.targetFrameRate, Is.EqualTo(60));
                scene.Don();
                view.choiceRows[2].click.Clicked();
                Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
                Assert.That(QualitySettings.vSyncCount, Is.Zero);
                scene.Don();
                view.choiceRows[0].click.Clicked();
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));

                // Display › VSync: off by default, applied at once.
                scene.Ka(1);
                Assert.That(view.itemRows[1].label.text, Is.EqualTo("VSync"));
                Assert.That(view.itemRows[1].value.text, Is.EqualTo("Disabled"));
                scene.Don();
                view.choiceRows[0].click.Clicked();
                Assert.That(Settings.Settings.display.vSync, Is.True);
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
                Assert.That(view.itemRows[1].value.text, Is.EqualTo("Enabled"));
                scene.Don();
                view.choiceRows[1].click.Clicked();
                Assert.That(QualitySettings.vSyncCount, Is.Zero);
                scene.Ka(1);
                scene.Don();
                Assert.That(menu.Focus, Is.EqualTo(SettingsFocus.Types));
                scene.Ka(2);
                scene.Don();
                Assert.That(scene.HasLeft, Is.True);
                yield return WaitForScene(SceneSwitcher.EntryScene);
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
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
                yield return null;
                yield return null;
                var play = Object.FindFirstObjectByType<PlayScene>();
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

        // The choice popup and its shade show only while a setting's choices are open.
        static void AssertPopup(GlobalSettingView view, bool open)
        {
            Assert.That(view.detail.alpha, Is.EqualTo(open ? 1 : 0));
            Assert.That(view.detail.blocksRaycasts, Is.EqualTo(open));
            Assert.That(view.shade.gameObject.activeSelf, Is.EqualTo(open));
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

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
