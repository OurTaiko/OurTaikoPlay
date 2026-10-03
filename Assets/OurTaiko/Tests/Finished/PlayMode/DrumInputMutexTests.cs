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
    // Taiko input mutex: InputManager keeps the order of presses within a frame,
    // and the play scene judges only the earliest drum press of each frame.
    public sealed class DrumInputMutexTests
    {
        Keyboard keyboard;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        double clock;

        [SetUp]
        public void SetUp()
        {
            // Keyboard events must reach the player loop even when the Game view is unfocused.
            var settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("MutexTestKeyboard");
            keyboard.MakeCurrent();
            clock = InputState.currentTime;
        }

        [TearDown]
        public void TearDown()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            var settings = InputSystem.settings;
            settings.backgroundBehavior = originalBackground;
            settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
        }

        // Queues one keyboard state event per step, each later than the last, all within one frame.
        void Press(params Key[] order)
        {
            for (int i = 1; i <= order.Length; i++)
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(order.Take(i).ToArray()), clock += 0.001);
        }
        void ReleaseAll() => InputSystem.QueueStateEvent(keyboard, new KeyboardState(), clock += 0.001);

        [UnityTest]
        public IEnumerator PressesThisFrameKeepTheOrderTheyHappened()
        {
            var empty = SceneManager.CreateScene("DrumInputOrder");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != empty) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            yield return null;

            // Reverse of the binding order, so a sorted-by-key list would fail.
            Press(Key.K, Key.J, Key.D, Key.F);
            yield return null;
            Assert.That(InputManager.PressesThisFrame.Select(p => p.Key),
                Is.EqualTo(new[] { InputKey.RightKa, InputKey.RightDon, InputKey.LeftKa, InputKey.LeftDon }));
            Assert.That(InputManager.PressesThisFrame.Select(p => p.Time), Is.Ordered);
            Assert.That(InputManager.GetKeyDown(InputKey.LeftDon) && InputManager.GetKeyDown(InputKey.RightKa), Is.True);

            // Held keys are not new presses on the next frame.
            yield return null;
            Assert.That(InputManager.PressesThisFrame, Is.Empty);
            Assert.That(InputManager.GetKey(InputKey.LeftDon), Is.True);

            ReleaseAll();
            yield return null;
            Press(Key.F, Key.D);
            yield return null;
            Assert.That(InputManager.PressesThisFrame.Select(p => p.Key), Is.EqualTo(new[] { InputKey.LeftDon, InputKey.LeftKa }));
            ReleaseAll();
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaySceneJudgesOnlyTheFirstDrumPressOfAFrame()
        {
            // One drumroll from 0 s to 4 s: every accepted press of either colour adds one roll hit.
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Input Mutex\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n5,\n0,\n8,\n#END");
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
                Assert.That(play.Session.Rolls, Is.Zero);
                Assert.That(play.drumFlashes.Any(f => f.enabled), Is.False);

                // Four drum keys in one frame: only the earliest (left ka) is judged.
                Press(Key.D, Key.F, Key.J, Key.K);
                yield return null;
                Assert.That(InputManager.PressesThisFrame.Count(p => p.Key.IsDrum()), Is.EqualTo(4));
                Assert.That(play.Session.Rolls, Is.EqualTo(1), "Later presses in the same frame must be dropped.");
                Assert.That(play.Record.Inputs.Count, Is.EqualTo(1));
                Assert.That(play.Record.Inputs[0].Ms, Is.EqualTo((play.SongTime - song.audioOffsetMs / 1000.0) * 1000).Within(0.000001),
                    "The event timestamp only orders presses; judgment uses this frame's song time.");
                // drumFlashes: left don, right don, left ka, right ka.
                Assert.That(play.drumFlashes.Select(f => f.enabled), Is.EqualTo(new[] { false, false, true, false }));

                // Holding the keys is not another hit.
                yield return null;
                Assert.That(play.Session.Rolls, Is.EqualTo(1));

                // Presses in separate frames are each judged.
                ReleaseAll(); yield return null;
                Press(Key.J); yield return null;
                Assert.That(play.Session.Rolls, Is.EqualTo(2));
                ReleaseAll(); yield return null;
                Press(Key.F); yield return null;
                Assert.That(play.Session.Rolls, Is.EqualTo(3));

                // A different first key in a later frame: right don wins over the left ka after it.
                ReleaseAll(); yield return null;
                Press(Key.J, Key.D); yield return null;
                Assert.That(play.Session.Rolls, Is.EqualTo(4));
                Assert.That(play.drumFlashes[1].enabled, Is.True);
                Assert.That(play.SongTime, Is.LessThan(4), "All presses must land inside the drumroll.");
                ReleaseAll(); yield return null;
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
