using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace OurTaiko.Tests
{
    // TestScoreStore gives this assembly temporary scores, options and player information.
    public sealed class PauseMenuFlowTests
    {
        Keyboard keyboard;
        Mouse mouse;
        Touchscreen touchscreen;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        SongDefinition song;
        PlayScene play;
        DrumPad pad;
        int nextTouchId;

        [SetUp]
        public void SetUp()
        {
            originalBackground = InputSystem.settings.backgroundBehavior;
            originalEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("PauseTestKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("PauseTestMouse");
            touchscreen = InputSystem.AddDevice<Touchscreen>("PauseTestTouchscreen");
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
            touchscreen.MakeCurrent();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (play != null) Object.Destroy(play.gameObject);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            if (song != null)
            {
                Object.Destroy(song.chart);
                // The test uses an imported clip; it is not owned by the test.
                Object.Destroy(song);
            }
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (touchscreen != null && touchscreen.added) InputSystem.RemoveDevice(touchscreen);
            InputSystem.settings.backgroundBehavior = originalBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
        }

        [UnityTest]
        public IEnumerator PauseFreezesAudioAndNotesAndBlocksDrumsUntilFadeOutCompletes()
        {
            yield return StartPlay();
            Assert.That(play.music.IsAudioPlaying(), Is.True);
            Assert.That(pad.enabled, Is.True);
            play.TogglePause();
            double frozenTime = play.SongTime;
            double renderedTime = play.RenderedTime;
            Vector3 notePosition = play.NoteRoot(0).localPosition;
            Assert.That(play.IsPaused, Is.True, "Pause must freeze gameplay at the start of the fade.");
            Assert.That(pad.enabled, Is.False);
            Assert.That(play.music.IsAudioPlaying() || play.hitAudio.IsAudioPlaying(), Is.False);
            Assert.That(play.pausePanel.activeInHierarchy, Is.True);
            Assert.That(play.pauseMenu.group.alpha, Is.LessThan(0.01f));

            yield return new WaitForSecondsRealtime(0.16f);
            Assert.That(play.pauseMenu.group.alpha, Is.InRange(0.05f, 0.95f), "Opening must visibly fade over 0.5 seconds.");
            yield return Press(Key.K);
            var background = ScreenPoint((RectTransform)play.pausePanel.transform, new Vector2(0.05f, 0.85f));
            AssertMenuReceivesPointer(background);
            yield return Click(background);
            yield return Tap(background);
            yield return WaitUntil(() => play.pauseMenu.group.alpha >= 1, "Pause menu did not finish fading in.");
            Assert.That(play.SongTime, Is.EqualTo(frozenTime));
            Assert.That(play.RenderedTime, Is.EqualTo(renderedTime));
            Assert.That(play.NoteRoot(0).localPosition, Is.EqualTo(notePosition));
            Assert.That(play.Session.Rolls, Is.Zero, "Paused drum keys and background pointers must not hit the roll.");
            Assert.That(play.drumFlashes.Any(f => f.enabled), Is.False);
            Assert.That(play.hitAudio.IsAudioPlaying(), Is.False);
            Assert.That(pad.drum.localScale, Is.EqualTo(Vector3.one), "Disabled drum pads must not animate on pointer presses.");

            play.Resume();
            float closingAt = Time.realtimeSinceStartup;
            yield return null;
            Assert.That(play.pauseMenu.IsClosing, Is.True);
            Assert.That(play.IsPaused, Is.True);
            Assert.That(play.pauseMenu.group.blocksRaycasts, Is.True);
            Assert.That(pad.enabled, Is.False);
            yield return new WaitForSecondsRealtime(0.16f);
            Assert.That(play.pauseMenu.group.alpha, Is.InRange(0.05f, 0.95f), "Closing must also fade over 0.5 seconds.");
            yield return Press(Key.F);
            yield return Click(background);
            Assert.That(play.IsPaused, Is.True, "Gameplay remains frozen while the closing menu is visible.");
            Assert.That(play.SongTime, Is.EqualTo(frozenTime));
            Assert.That(play.Session.Rolls, Is.Zero);
            Assert.That(pad.enabled, Is.False);
            yield return WaitUntil(() => !play.IsPaused, "Resume did not complete.");
            Assert.That(Time.realtimeSinceStartup - closingAt, Is.GreaterThanOrEqualTo(0.48f));
            Assert.That(play.pausePanel.activeInHierarchy, Is.False);
            Assert.That(pad.enabled, Is.True);
            Assert.That(play.music.IsAudioPlaying(), Is.True);
            yield return Press(Key.F);
            Assert.That(play.Session.Rolls, Is.EqualTo(1), "The first new drum press after resume must be playable.");
        }

        [UnityTest]
        public IEnumerator TopmostMenuOwnsAllThreeActionsAndKeyboardNavigation()
        {
            yield return StartPlay();
            yield return Press(Key.Escape);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.GameScene), "Escape opens pause instead of leaving the song.");
            yield return WaitForOpen();
            var menu = play.pausePanel.transform;
            Assert.That(menu.GetSiblingIndex(), Is.EqualTo(menu.parent.childCount - 1), "The pause menu must draw over all gameplay UI.");
            var buttons = new[] { play.resumeButton, play.restartButton, play.backButton };
            var labels = new[] { "Resume", "Restart", "Back to Song Select" };
            float previousY = float.PositiveInfinity;
            for (int i = 0; i < buttons.Length; i++)
            {
                Assert.That(buttons[i].transform.IsChildOf(menu), Is.True, "Restart and Back belong inside the pause menu.");
                Assert.That(buttons[i].GetComponentInChildren<TMP_Text>().text, Is.EqualTo(labels[i]).IgnoreCase);
                Assert.That(buttons[i].navigation.mode, Is.EqualTo(Navigation.Mode.None), "InputManager owns navigation; uGUI must not move twice.");
                Vector2 point = ScreenPoint((RectTransform)buttons[i].transform);
                Assert.That(point.y, Is.LessThan(previousY), "Actions must be ordered Resume, Restart, Back to Song Select.");
                previousY = point.y;
                AssertMenuReceivesPointer(point, buttons[i]);
            }
            Assert.That(play.pauseMenu.SelectedIndex, Is.Zero);
            yield return Press(Key.DownArrow);
            Assert.That(play.pauseMenu.SelectedIndex, Is.EqualTo(1));
            yield return Press(Key.UpArrow);
            Assert.That(play.pauseMenu.SelectedIndex, Is.Zero);
            yield return Press(Key.K);
            Assert.That(play.pauseMenu.SelectedIndex, Is.EqualTo(1));
            yield return Press(Key.D);
            Assert.That(play.pauseMenu.SelectedIndex, Is.Zero);
            yield return Press(Key.RightArrow);
            Assert.That(play.pauseMenu.SelectedIndex, Is.EqualTo(1));
            yield return Press(Key.LeftArrow);
            Assert.That(play.pauseMenu.SelectedIndex, Is.Zero);
            Assert.That(play.Session.Rolls, Is.Zero, "Menu drum-key navigation must not judge gameplay notes.");
            yield return Press(Key.J);
            Assert.That(play.pauseMenu.IsClosing, Is.True);
            yield return WaitUntil(() => !play.IsPaused, "Keyboard confirmation did not resume.");
            Assert.That(play.Session.Rolls, Is.Zero, "The confirm press must not leak into gameplay.");
        }

        [UnityTest]
        public IEnumerator MouseAndTouchCanOpenAndResumeTheMenu()
        {
            yield return StartPlay();
            var pauseRect = (RectTransform)play.pauseButton.transform;
            var fpsRect = pauseRect.parent.Find("FpsPanel") as RectTransform;
            Assert.That(pauseRect.rect.width, Is.EqualTo(pauseRect.rect.height));
            Assert.That(pauseRect.anchoredPosition.x + pauseRect.rect.width, Is.LessThan(fpsRect.anchoredPosition.x));
            Assert.That(play.pauseButton.GetComponentInChildren<TMP_Text>(), Is.Null, "The pause trigger is an icon, not a text button.");
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenPoint(pauseRect) }, hits);
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(play.pauseButton), "The transparent centre of the circle must remain clickable.");
            hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenPoint(pauseRect, new Vector2(.02f, .02f)) }, hits);
            Assert.That(hits.Any(hit => hit.gameObject.GetComponentInParent<Button>() == play.pauseButton), Is.False, "The square corners are outside the round button.");
            TestCapture.Capture("CircularPauseButton.png");
            yield return Click(ScreenPoint((RectTransform)play.pauseButton.transform));
            yield return WaitForOpen();
            Assert.That(pad.enabled, Is.False);
            yield return Click(ScreenPoint((RectTransform)play.resumeButton.transform));
            Assert.That(play.pauseMenu.IsClosing, Is.True);
            yield return WaitUntil(() => !play.IsPaused, "Mouse click did not resume.");
            Assert.That(play.Session.Rolls, Is.Zero);

            yield return Tap(ScreenPoint((RectTransform)play.pauseButton.transform));
            yield return WaitForOpen();
            Assert.That(pad.enabled, Is.False);
            yield return Tap(ScreenPoint((RectTransform)play.resumeButton.transform));
            Assert.That(play.pauseMenu.IsClosing, Is.True);
            yield return WaitUntil(() => !play.IsPaused, "Touch tap did not resume.");
            Assert.That(play.Session.Rolls, Is.Zero, "Pointer menu clicks must never be drum hits.");
        }

        [UnityTest]
        public IEnumerator RestartAndBackWaitForTheFadeAndBackAlwaysReturnsToSongSelect()
        {
            yield return StartPlay();
            Assert.That(SceneSwitcher.Instance.ReturnScene, Is.EqualTo(SceneSwitcher.MenuScene),
                "This test deliberately starts at Entry to distinguish Song Select from the old return destination.");
            yield return Press(Key.Space);
            yield return WaitForOpen();
            yield return Press(Key.DownArrow);
            var oldPlay = play;
            yield return Press(Key.Enter);
            Assert.That(oldPlay.pauseMenu.IsClosing, Is.True);
            yield return new WaitForSecondsRealtime(0.16f);
            Assert.That(SceneSwitcher.Instance.IsSwitching, Is.False, "Restart must wait for the menu to fade away.");
            Assert.That(Object.FindFirstObjectByType<PlayScene>(), Is.SameAs(oldPlay));
            yield return WaitUntil(() => oldPlay == null, "Restart did not reload the play scene.", 15);
            yield return WaitForScene(SceneSwitcher.GameScene);
            play = Object.FindFirstObjectByType<PlayScene>();
            pad = Object.FindFirstObjectByType<DrumPad>();
            Assert.That(play.Session.Chart.Title, Is.EqualTo("Pause Menu Test"));
            Assert.That(play.Session.Rolls, Is.Zero);
            Assert.That(play.SongTime, Is.LessThan(0), "Restart begins a new countdown.");
            Assert.That(play.IsPaused, Is.False);

            yield return Press(Key.Space);
            yield return WaitForOpen();
            yield return Tap(ScreenPoint((RectTransform)play.backButton.transform));
            Assert.That(play.pauseMenu.IsClosing, Is.True);
            yield return new WaitForSecondsRealtime(0.16f);
            Assert.That(SceneSwitcher.Instance.IsSwitching, Is.False, "Back must wait for the menu to fade away.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.GameScene));
            Assert.That(pad.enabled, Is.False);
            yield return WaitForScene(SceneSwitcher.SongSelectScene);
            Assert.That(Object.FindFirstObjectByType<SongSelectScene>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator OpeningFrameInputCannotConfirmAndFocusLossCancelsResume()
        {
            yield return StartPlay();
            // Model the valid uGUI execution order: its Pause callback can run after InputManager
            // publishes this frame's keyboard events but before PlayScene.Update consumes them.
            var physicalKeys = new[] { Key.F, Key.Space, Key.Escape, Key.F1 };
            var logicalKeys = new[] { InputKey.LeftDon, InputKey.Pause, InputKey.Back, InputKey.Restart };
            for (int i = 0; i < physicalKeys.Length; i++)
            {
                var callback = play.gameObject.AddComponent<PauseClickBeforePlayUpdate>();
                callback.target = play;
                callback.trigger = logicalKeys[i];
                yield return Press(physicalKeys[i]);
                Assert.That(callback.Fired, Is.True);
                Object.Destroy(callback);
                Assert.That(play.IsPaused, Is.True);
                Assert.That(play.pauseMenu.IsClosing, Is.False,
                    $"The opening frame's {physicalKeys[i]} must not resume, restart, or leave the new menu.");
                Assert.That(SceneSwitcher.Instance.IsSwitching, Is.False);
                Assert.That(play.Session.Rolls, Is.Zero);
                yield return WaitForOpen();
                if (i + 1 < physicalKeys.Length)
                {
                    play.Resume();
                    yield return WaitUntil(() => !play.IsPaused, "Could not resume between opening-frame input cases.");
                }
            }
            double frozenTime = play.SongTime;

            yield return Press(Key.Space);
            Assert.That(play.pauseMenu.IsClosing, Is.True);
            yield return new WaitForSecondsRealtime(0.15f);
            play.SendMessage("OnApplicationFocus", false);
            Assert.That(play.IsPaused, Is.True);
            Assert.That(pad.enabled, Is.False);
            yield return WaitForOpen();
            Assert.That(play.SongTime, Is.EqualTo(frozenTime),
                "Losing focus during the fade must leave the song frozen and reopen the menu.");
            Assert.That(play.music.IsAudioPlaying() || play.hitAudio.IsAudioPlaying(), Is.False);
            Assert.That(pad.enabled, Is.False);
            Assert.That(play.pauseButton.interactable, Is.False);

            play.SendMessage("OnApplicationFocus", true);
            yield return Press(Key.Enter);
            Assert.That(play.pauseMenu.IsClosing, Is.True, "A new keyboard confirmation can resume after focus returns.");
            yield return WaitUntil(() => !play.IsPaused, "Resume stayed blocked after focus returned.");
            Assert.That(pad.enabled && play.pauseButton.interactable, Is.True);
            Assert.That(play.music.IsAudioPlaying(), Is.True);
            Assert.That(play.Session.Rolls, Is.Zero);
        }

        IEnumerator StartPlay()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "PauseMenuTest";
            // A long roll makes every accepted drum input observable and avoids natural song completion.
            song.chart = new TextAsset("TITLE:Pause Menu Test\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n#MEASURE 4/1\n5,\n0,\n8,\n#END");
            song.music = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            PlayOptions.Shared = new PlayOptions();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            SceneSwitcher.EnsureInstance().Play(song);
            yield return WaitForScene(SceneSwitcher.GameScene);
            play = Object.FindFirstObjectByType<PlayScene>();
            pad = Object.FindFirstObjectByType<DrumPad>();
            Assert.That(play.pauseMenu, Is.Not.Null, "The saved play scene must contain the pause menu.");
            Assert.That(pad, Is.Not.Null);
            yield return WaitUntil(() => play.SongTime >= 0.2, "Gameplay did not start.", 10);
            Assert.That(play.Session.Rolls, Is.Zero);
        }

        IEnumerator WaitForOpen()
        {
            yield return WaitUntil(() => play.IsPaused && play.pausePanel.activeInHierarchy
                && play.pauseMenu.group.alpha >= 1 && !play.pauseMenu.IsClosing, "Pause menu did not open.");
        }

        IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        IEnumerator Click(Vector2 position)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
        }

        IEnumerator Tap(Vector2 position)
        {
            int id = ++nextTouchId;
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = TouchPhase.Began, position = position, pressure = 1 });
            yield return null;
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = id, phase = TouchPhase.Ended, position = position });
            yield return null;
        }

        void AssertMenuReceivesPointer(Vector2 position, Button expected = null)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject.transform.IsChildOf(play.pausePanel.transform), Is.True,
                "The topmost pointer target must belong to the menu, including its background blocker.");
            if (expected != null) Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.SameAs(expected));
        }

        static Vector2 ScreenPoint(RectTransform rect, Vector2? relative = null)
        {
            var normalized = relative ?? new Vector2(0.5f, 0.5f);
            var local = rect.rect.min + Vector2.Scale(rect.rect.size, normalized);
            var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(local));
        }

        static IEnumerator WaitForScene(string scene)
        {
            yield return WaitUntil(() => SceneSwitcher.Instance != null && !SceneSwitcher.Instance.IsInputBlocked
                && SceneManager.GetActiveScene().name == scene, "Scene did not become ready: " + scene, 20);
            yield return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, string message, float seconds = 3)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), message);
                yield return null;
            }
        }
    }
    // Runs between GameLoop (-32000) and the default-order PlayScene, reproducing
    // a pointer callback that opens Pause in the same frame as a previously published keyboard shortcut.
    [DefaultExecutionOrder(-31000)]
    public sealed class PauseClickBeforePlayUpdate : MonoBehaviour
    {
        public PlayScene target;
        public InputKey trigger;
        public bool Fired { get; private set; }

        void Update()
        {
            if (!InputManager.GetKeyDown(trigger)) return;
            Fired = true;
            target.pauseButton.onClick.Invoke();
            enabled = false;
        }
    }

}
