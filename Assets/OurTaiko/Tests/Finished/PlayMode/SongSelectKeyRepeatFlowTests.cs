using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The arrow keys on song select, after CStageSongSelect: held, an arrow keeps stepping the wheel
    // once per slide; while the wheel slides a fresh press is ignored; on the option panel up/down
    // walk the rows.
    public sealed class SongSelectKeyRepeatFlowTests
    {
        Keyboard keyboard;
        InputSettings.BackgroundBehavior background;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [SetUp]
        public void Setup()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            InputManager.ResetBindings();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Press();
            yield return null;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        void Press(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

        IEnumerator OpenSongSelect(System.Action<SongSelectScene> body)
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var select = Object.FindAnyObjectByType<SongSelectScene>();
            float deadline = Time.realtimeSinceStartup + 20;
            while (SceneSwitcher.Instance.IsInputBlocked) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
            select.Manager.OpenFolderAt(0);
            yield return new WaitForSecondsRealtime(0.6f);
            body(select);
        }

        [UnityTest]
        public IEnumerator HeldArrowStepsOncePerSlideAndAPressDuringASlideIsIgnored()
        {
            SongSelectScene select = null;
            yield return OpenSongSelect(s => select = s);
            var manager = select.Manager;
            int start = manager.Focused;

            // Held for about half a second: the press, then one step per 120 ms slide.
            Press(Key.RightArrow);
            yield return new WaitForSecondsRealtime(0.5f);
            Press();
            yield return null;
            int steps = manager.Focused - start;
            Assert.That(steps, Is.InRange(3, 5), $"held 500 ms moved {steps}");
            yield return new WaitForSecondsRealtime(0.6f);

            // Two presses 40 ms apart: the second lands mid-slide and is dropped.
            start = manager.Focused;
            Press(Key.LeftArrow); yield return null; Press(); yield return null;
            Assert.That(manager.Focused - start, Is.EqualTo(-1));
            float until = Time.realtimeSinceStartup + 0.04f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Press(Key.LeftArrow); yield return null; Press(); yield return null;
            Assert.That(manager.Focused - start, Is.EqualTo(-1), "The press during the slide is ignored.");
            yield return new WaitForSecondsRealtime(0.3f);
            Press(Key.LeftArrow); yield return null; Press(); yield return null;
            Assert.That(manager.Focused - start, Is.EqualTo(-2), "Once the slide is over it moves again.");
        }

        [UnityTest]
        public IEnumerator UpAndDownWalkTheOptionRows()
        {
            SongSelectScene select = null;
            yield return OpenSongSelect(s => select = s);
            var manager = select.Manager;
            manager.Navigate(2);
            Assert.That(manager.FocusedSong, Is.Not.Null);
            yield return new WaitForSecondsRealtime(0.6f);
            manager.Confirm();
            Assert.That(manager.Phase, Is.EqualTo(SongSelectManager.State.CourseSelect));
            float deadline = Time.realtimeSinceStartup + 3;
            while (select.CourseFade < 1) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }

            // Choosing a course the arrows do not repeat: a held arrow moves once.
            Assert.That(manager.Cursor.Selected, Is.EqualTo(Difficulty.Back));
            Press(Key.RightArrow);
            yield return new WaitForSecondsRealtime(0.4f);
            Press(); yield return null;
            Assert.That(manager.Cursor.Selected, Is.EqualTo(Difficulty.Modifier));
            manager.Confirm();
            Assert.That(manager.IsOptionPanelOpen, Is.True);
            Assert.That(manager.OptionMenu.Index, Is.Zero);

            Press(Key.DownArrow); yield return null; Press(); yield return null;
            Assert.That(manager.OptionMenu.Index, Is.EqualTo(1));
            Press(Key.UpArrow); yield return null; Press(); yield return null;
            Press(Key.UpArrow); yield return null; Press(); yield return null;
            Assert.That(manager.OptionMenu.Index, Is.EqualTo(OptionMenu.Rows.Length - 1), "Up from the first row wraps to the last.");
            Assert.That(manager.IsOptionPanelOpen, Is.True, "Moving rows never closes the panel.");
        }
    }
}
