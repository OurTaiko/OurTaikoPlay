using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Arcade-like input delay: a drum press is published, judged and shown only after the delay.
    public sealed class InputDelayFlowTests
    {
        Keyboard keyboard;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;

        [SetUp]
        public void SetUp()
        {
            var settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("DelayTestKeyboard");
            keyboard.MakeCurrent();
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
        }

        [TearDown]
        public void TearDown()
        {
            InputManager.DrumDelay = 0;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            var settings = InputSystem.settings;
            settings.backgroundBehavior = originalBackground;
            settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
        }

        [UnityTest]
        public IEnumerator DrumPressesWaitOutTheDelayAndOtherKeysDoNot()
        {
            var empty = SceneManager.CreateScene("InputDelay");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != empty) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            yield return null;

            const double delay = 0.2;
            InputManager.DrumDelay = delay;
            double pressed = InputState.currentTime;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.Enter), pressed);
            yield return null;
            Assert.That(InputManager.PressesThisFrame.Select(p => p.Key), Is.EqualTo(new[] { InputKey.Confirm }));
            Assert.That(InputManager.GetKeyDown(InputKey.LeftDon), Is.False);
            Assert.That(InputManager.GetKey(InputKey.LeftDon), Is.True, "Held state is not delayed.");

            int published = 0;
            float deadline = Time.realtimeSinceStartup + 5;
            while (published == 0)
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                published = InputManager.PressesThisFrame.Count(p => p.Key == InputKey.LeftDon);
            }
            Assert.That(published, Is.EqualTo(1));
            Assert.That(InputManager.GetKeyDown(InputKey.LeftDon), Is.True);
            Assert.That(InputManager.PressesThisFrame[0].Time, Is.EqualTo(pressed), "The press keeps the time it happened.");
            Assert.That(InputState.currentTime - pressed, Is.GreaterThanOrEqualTo(delay));
            yield return null;
            Assert.That(InputManager.PressesThisFrame, Is.Empty, "A delayed press is published once.");

            // A waiting press can be dropped.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J), InputState.currentTime); yield return null;
            InputManager.DropDelayed();
            InputManager.DrumDelay = 0;
            yield return null;
            Assert.That(InputManager.PressesThisFrame, Is.Empty);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }

        [UnityTest]
        public IEnumerator PlaySceneJudgesAHitAfterTheConfiguredDelay()
        {
            Assert.That(new GameSettings().play.inputDelayMs, Is.EqualTo(5));
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings { play = new PlaySettings { inputDelayMs = 100 } });
            // One drumroll from 0 s to 4 s: every accepted press adds one roll hit.
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Input Delay\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n5,\n0,\n8,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song);
                float deadline = Time.realtimeSinceStartup + 20;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                deadline = Time.realtimeSinceStartup + 10;
                while (play.SongTime < 0.3)
                {
                    if (play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                Assert.That(InputManager.DrumDelay, Is.EqualTo(0.1).Within(1e-9));

                double pressed = InputState.currentTime, pressedSong = play.SongTime;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F), pressed);
                yield return null;
                Assert.That(play.Session.Rolls, Is.Zero, "The hit is still waiting.");
                Assert.That(play.drumFlashes.Any(f => f.enabled), Is.False);
                deadline = Time.realtimeSinceStartup + 5;
                while (play.Session.Rolls == 0)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                Assert.That(InputState.currentTime - pressed, Is.GreaterThanOrEqualTo(0.1));
                Assert.That(play.Session.Rolls, Is.EqualTo(1));
                Assert.That(play.drumFlashes[0].enabled, Is.True);
                Assert.That(play.Record.Inputs[0].Ms, Is.EqualTo(play.SongTime * 1000).Within(0.000001),
                    "Judgment uses the song time of the frame that publishes the hit.");
                Assert.That(play.SongTime - pressedSong, Is.GreaterThanOrEqualTo(0.09));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;

                // Pausing drops a hit that is still waiting.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J), InputState.currentTime);
                yield return null;
                play.TogglePause();
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(play.Session.Rolls, Is.EqualTo(1));
                Assert.That(play.IsPaused, Is.True);

                Object.Destroy(play.gameObject);
                yield return null;
                Assert.That(InputManager.DrumDelay, Is.Zero, "The delay ends with the play scene.");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
