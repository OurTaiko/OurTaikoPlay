using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // InputManager.KeyboardSource (iOS: the GameController keyboard, issue #100): while it is active
    // every key-down it reports is a press, however short, and Unity's Keyboard is not read.
    public sealed class KeyboardSourceFlowTests
    {
        sealed class FakeSource : IKeyboardSource
        {
            public bool Active { get; set; } = true;
            public double Now => 100;
            public readonly List<KeyChange> Queued = new List<KeyChange>();
            public void Drain(List<KeyChange> into) { into.AddRange(Queued); Queued.Clear(); }
        }

        Keyboard keyboard;
        FakeSource source;
        IKeyboardSource original;
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
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            keyboard = InputSystem.AddDevice<Keyboard>("SourceTestKeyboard");
            keyboard.MakeCurrent();
            original = InputManager.KeyboardSource;
            source = new FakeSource();
            InputManager.KeyboardSource = source;
        }

        [TearDown]
        public void TearDown()
        {
            InputManager.KeyboardSource = original;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            var settings = InputSystem.settings;
            settings.backgroundBehavior = originalBackground;
            settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
        }

        [UnityTest]
        public IEnumerator EveryKeyDownIsAPressAndUnitysKeyboardWaits()
        {
            var empty = SceneManager.CreateScene("KeyboardSource");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != empty) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            yield return null;

            // Down, up and down again between two frames: two presses, stamped on the Input System clock.
            source.Queued.Add(new KeyChange(Key.F, true, 99.990));
            source.Queued.Add(new KeyChange(Key.F, false, 99.995));
            source.Queued.Add(new KeyChange(Key.J, true, 99.996));
            source.Queued.Add(new KeyChange(Key.F, true, 99.998));
            yield return null;
            Assert.That(InputManager.PressesThisFrame.Select(p => p.Key),
                Is.EqualTo(new[] { InputKey.LeftDon, InputKey.RightDon, InputKey.LeftDon }));
            var times = InputManager.PressesThisFrame.Select(p => p.Time).ToArray();
            Assert.That(times[2] - times[0], Is.EqualTo(0.008).Within(1e-9));
            Assert.That(InputManager.GetKey(InputKey.LeftDon) && InputManager.GetKey(InputKey.RightDon), Is.True);

            // Held keys stay held without new presses; a release lets go.
            yield return null;
            Assert.That(InputManager.PressesThisFrame, Is.Empty);
            Assert.That(InputManager.GetKey(InputKey.LeftDon), Is.True);
            source.Queued.Add(new KeyChange(Key.F, false, 100.1));
            source.Queued.Add(new KeyChange(Key.J, false, 100.1));
            yield return null;
            Assert.That(InputManager.GetKey(InputKey.LeftDon), Is.False);

            // Unity's Keyboard is not read while the source is active, so nothing counts twice.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return null;
            Assert.That(InputManager.PressesThisFrame, Is.Empty);
            Assert.That(InputManager.GetKey(InputKey.LeftKa), Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;

            // With no keyboard on the source, Unity's Keyboard is read and stale changes are dropped.
            source.Active = false;
            source.Queued.Add(new KeyChange(Key.K, true, 100.2));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return null;
            Assert.That(InputManager.PressesThisFrame.Select(p => p.Key), Is.EqualTo(new[] { InputKey.LeftKa }));
            Assert.That(source.Queued, Is.Empty);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
    }
}
