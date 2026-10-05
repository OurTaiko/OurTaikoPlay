using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class PracticeFlowTests
    {
        Keyboard keyboard;
        Mouse mouse;
        SongDefinition song;
        PlayScene play;
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
            mouse = InputSystem.AddDevice<Mouse>(); mouse.MakeCurrent();
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (play != null) Object.Destroy(play.gameObject);
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            if (song != null) { Object.Destroy(song.chart); Object.Destroy(song); }
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }
        [UnityTest]
        public IEnumerator FirstAndSelectedMeasureBothHavePreparation()
        {
            yield return StartPractice(AudioBackend.Unity, true);
            yield return Click(play.practiceView.confirm);
            yield return Click(play.practiceView.confirm);
            Assert.That(play.SongTime, Is.LessThan(-1.8));
            Assert.That(play.Session.Good + play.Session.Bad + play.Session.Rolls, Is.Zero);
            yield return Wait(() => play.SongTime >= 0);
            yield return Press(Key.F);
            Assert.That(play.Session.Good + play.Session.Ok, Is.GreaterThan(0), "The note at the very first bar must be hittable.");
            play.TogglePause(); yield return null;
            play.MovePractice(1); yield return new WaitForSecondsRealtime(0.25f);
            double target = play.Practice.Target;
            yield return Click(play.practiceView.confirm);
            yield return Press(Key.D); yield return Press(Key.D);
            Assert.That(play.Practice.Speed, Is.EqualTo(0.8));
            yield return Click(play.practiceView.confirm);
            Assert.That(play.SongTime, Is.LessThan(target - 1.4));
            yield return new WaitForSecondsRealtime(1);
            Assert.That(play.SongTime, Is.LessThan(target));
            Assert.That(play.Session.Bad, Is.Zero, "Preparation must not count skipped notes as misses.");
        }
        [UnityTest] public IEnumerator NativePlaybackAndTwoLayerPause() => ExercisePlayback(AudioBackend.Bass);
        [UnityTest] public IEnumerator UnityPlaybackAndTwoLayerPause() => ExercisePlayback(AudioBackend.Unity);

        IEnumerator ExercisePlayback(AudioBackend backend)
        {
            yield return StartPractice(backend, true);
            Assert.That(play.practiceView.heading.text, Is.EqualTo("小节进度"));
            Assert.That(play.BarRoot(0).anchoredPosition.x, Is.EqualTo(120).Within(0.1));
            double before = play.RenderedTime;
            yield return Press(Key.K);
            Assert.That(play.Practice.Target, Is.EqualTo(2));
            Assert.That(play.RenderedTime, Is.InRange(before, 1.99), "Measure skip must animate, not teleport.");
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(play.BarRoot(1).anchoredPosition.x, Is.EqualTo(120).Within(0.1));
            Assert.That(play.Session.Bad, Is.Zero);
            TestCapture.Capture("PracticeMeasures-" + backend + ".png");
            TestCapture.Capture("PracticeMeasures720-" + backend + ".png", 1280, 720);
            yield return Click(play.practiceView.previous);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(play.Practice.Target, Is.EqualTo(0));
            Assert.That(play.BarRoot(0).anchoredPosition.x, Is.EqualTo(120).Within(0.1));
            yield return Click(play.practiceView.next);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(play.Practice.Target, Is.EqualTo(2));
            yield return Press(Key.F);
            Assert.That(play.ChoosingPracticeSpeed, Is.True);
            yield return Press(Key.D); yield return Press(Key.D);
            Assert.That(play.Practice.Speed, Is.EqualTo(0.8));
            TestCapture.Capture("PracticeSpeed-" + backend + ".png");
            yield return Press(Key.J);
            Assert.That(play.IsPaused, Is.False);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(play.music.IsAudioPlaying(), Is.True);
            double songStart = play.SongTime, audioStart = play.music.AudioPosition();
            double realStart = GameTimeline.AudioNow;
            yield return new WaitForSecondsRealtime(0.65f);
            double elapsed = GameTimeline.AudioNow - realStart;
            Assert.That(play.SongTime - songStart, Is.EqualTo(elapsed * 0.8).Within(0.08));
            Assert.That(play.music.AudioPosition() - audioStart, Is.EqualTo(elapsed * 0.8).Within(0.12));
            Assert.That(play.music.AudioPosition(), Is.EqualTo(play.SongTime).Within(0.15));
            play.pauseButton.onClick.Invoke(); yield return null;
            Assert.That(play.IsPaused && !play.pausePanel.activeSelf, Is.True);
            Assert.That(play.Session.Good + play.Session.Ok + play.Session.Bad + play.Session.Rolls, Is.Zero);
            Assert.That(play.music.IsAudioPlaying(), Is.False);
            yield return Click(play.practiceView.confirm);
            for (int i = 0; i < 4; i++) yield return Press(Key.K);
            Assert.That(play.Practice.Speed, Is.EqualTo(1.2));
            yield return Click(play.practiceView.confirm);
            yield return new WaitForSecondsRealtime(2.3f);
            songStart = play.SongTime; audioStart = play.music.AudioPosition(); realStart = GameTimeline.AudioNow;
            yield return new WaitForSecondsRealtime(0.5f);
            elapsed = GameTimeline.AudioNow - realStart;
            Assert.That(play.SongTime - songStart, Is.EqualTo(elapsed * 1.2).Within(0.08));
            Assert.That(play.music.AudioPosition() - audioStart, Is.EqualTo(elapsed * 1.2).Within(0.12));
            play.TogglePause(); yield return null;
            // Back and Restart cannot escape/reload from the first pause layer.
            play.Back(); play.Restart(); yield return null;
            Assert.That(SceneSwitcher.Instance.IsSwitching, Is.False);
            play.pauseButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.That(play.pausePanel.activeSelf, Is.True);
            Assert.That(play.drumPad.enabled, Is.False);
            play.resumeButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.That(play.IsPaused && !play.pausePanel.activeSelf, Is.True);
            Assert.That(play.drumPad.enabled, Is.True);
            Assert.That(play.music.IsAudioPlaying(), Is.False);
            play.pauseButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.55f);
            play.restartButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.55f);
            Assert.That(play.Practice.Target, Is.EqualTo(0));
            Assert.That(play.IsPaused, Is.True);
            play.pauseButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.55f);
            play.backButton.onClick.Invoke();
            yield return Wait(() => SceneManager.GetActiveScene().name == SceneSwitcher.SongSelectScene && !SceneSwitcher.Instance.IsInputBlocked);
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True, "Another song must still enter practice.");
            Assert.That(SceneSwitcher.Instance.LastResult, Is.Null);
        }
        [UnityTest]
        public IEnumerator FinishReturnsToFirstMeasureWithoutResultAndEntryHasIndependentRoute()
        {
            yield return StartPractice(AudioBackend.Unity, false);
            // Jump near the end, then run the complete end-of-chart path.
            play.MovePractice(1); yield return null; play.MovePractice(1);
            yield return new WaitForSecondsRealtime(0.25f);
            play.ConfirmPractice(); yield return null;
            play.ConfirmPractice();
            yield return Wait(() => play.IsPaused);
            Assert.That(play.Practice.Target, Is.EqualTo(0));
            Assert.That(play.IsFinished, Is.False);
            Assert.That(play.ChoosingPracticeSpeed, Is.False);
            Assert.That(play.Session.Good + play.Session.Bad, Is.Zero);
            Assert.That(SceneSwitcher.Instance.LastResult, Is.Null);
            Assert.That(SceneSwitcher.Instance.SongsPlayed, Is.Zero);
            Assert.That(ScoreStore.Shared.Get(song.name, Difficulty.Oni), Is.Null);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.PracticeScene));
        }
        [UnityTest]
        public IEnumerator DirectEditorSceneLaunchRetainsPracticeSelection()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.PracticeScene);
            play = Object.FindFirstObjectByType<PlayScene>();
            yield return Wait(() => play.Practice != null && play.IsPaused);
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True);
            Assert.That(SceneSwitcher.Instance.SelectedPlayScene, Is.EqualTo(SceneSwitcher.PracticeScene));
        }
        IEnumerator StartPractice(AudioBackend backend, bool withAudio)
        {
            var settings = new GameSettings(); settings.audio.backend = backend;
            SettingManager.EnsureInstance().UseUnsaved(settings);
            var apply = AudioEngine.EnsureInstance().ApplyPendingSettingsAsync();
            while (!apply.IsCompleted) yield return null;
            Assert.That(apply.IsFaulted, Is.False);
            Assert.That(AudioEngine.Instance.Backend, Is.EqualTo(backend));
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            Assert.That(entry.view.boards.Count(b => b.practice), Is.EqualTo(1));
            Assert.That(entry.view.boards[1].practice, Is.True);
            Assert.That(entry.view.boards[1].scene, Is.EqualTo(SceneSwitcher.ServerLoginScene));
            entry.Don(); yield return new WaitForSecondsRealtime(1.5f);
            entry.Ka(1); yield return new WaitForSecondsRealtime(0.3f); entry.Don();
            yield return Wait(() => SceneManager.GetActiveScene().name == SceneSwitcher.SongSelectScene && !SceneSwitcher.Instance.IsInputBlocked);
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True);
            song = ScriptableObject.CreateInstance<SongDefinition>(); song.name = "Practice Test";
            song.chart = new TextAsset("TITLE:Practice Test\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n1111,\n1111,\n#END");
            if (withAudio) song.music = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            SceneSwitcher.Instance.Play(song, "Oni", false);
            yield return Wait(() => SceneManager.GetActiveScene().name == SceneSwitcher.PracticeScene && !SceneSwitcher.Instance.IsInputBlocked);
            play = Object.FindFirstObjectByType<PlayScene>();
            yield return Wait(() => play.Practice != null && play.IsPaused);
            yield return null;
        }
        static IEnumerator Wait(Func<bool> predicate)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 25;
            while (!predicate()) { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
        }
        IEnumerator Click(UnityEngine.UI.Button button)
        {
            var rect = (RectTransform)button.transform;
            var screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }); yield return null;
            yield return null;
        }
        IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
    }
}
