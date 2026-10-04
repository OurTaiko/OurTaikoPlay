using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class OffsetFlowTests
    {
        [UnityTest]
        public IEnumerator NumericPopupUsesDrumKeysAndTouchAndSavesMilliseconds()
        {
            var settings = SettingManager.EnsureInstance();
            string path = Path.Combine(Application.temporaryCachePath, "offset-test-" + Guid.NewGuid(), "settings.json");
            settings.Load(path);
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene); yield return null;
                var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
                var view = scene.view;
                yield return Press(keyboard, Key.K); yield return Press(keyboard, Key.F);
                Assert.That(scene.Menu.ItemCount, Is.EqualTo(4));
                yield return Press(keyboard, Key.K); yield return Press(keyboard, Key.J);
                Assert.That(view.detailTitle.text, Is.EqualTo("Offset A (Audio)"));
                yield return Press(keyboard, Key.D);
                Assert.That(view.choiceRows[0].label.text, Is.EqualTo("-1 ms"));
                Assert.That(settings.Settings.play.audioOffsetMs, Is.Zero);
                TestCapture.Capture("OffsetASettings.png");
                TestCapture.Capture("OffsetASettings720.png", 1280, 720);
                yield return Press(keyboard, Key.F);
                Assert.That(settings.Settings.play.audioOffsetMs, Is.EqualTo(-1));
                Assert.That(view.itemRows[1].value.text, Is.EqualTo("-1 ms"));
                scene.Ka(1); scene.Don();
                Assert.That(view.detailTitle.text, Is.EqualTo("Offset B (Judgment)"));
                Click(view.nextChoice); Click(view.nextChoice); Click(view.previousChoice);
                Assert.That(view.choiceRows[0].label.text, Is.EqualTo("+1 ms"));
                Click(view.choiceRows[0].click);
                settings.Load(path);
                Assert.That(settings.Settings.play.audioOffsetMs, Is.EqualTo(-1));
                Assert.That(settings.Settings.play.judgeOffsetMs, Is.EqualTo(1));
                scene.Don(); scene.Ka(1); view.shadeClick.Clicked();
                Assert.That(settings.Settings.play.judgeOffsetMs, Is.EqualTo(1), "Cancel does not save staged edits.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
                settings.UseUnsaved(new GameSettings());
                Directory.Delete(Path.GetDirectoryName(path), true);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest] public IEnumerator SinglePlayPositiveOffsets() => CheckPlay(false, 120, 200);
        [UnityTest] public IEnumerator SinglePlayNegativeOffsets() => CheckPlay(false, -120, -200);
        [UnityTest] public IEnumerator PracticePositiveOffsets() => CheckPlay(true, 120, 200);
        [UnityTest] public IEnumerator PracticeNegativeOffsets() => CheckPlay(true, -120, -200);

        static IEnumerator CheckPlay(bool practice, int a, int b)
        {
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings
                { play = new PlaySettings { audioOffsetMs = a, judgeOffsetMs = b } });
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:A B offset\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n0010,\n0010,\n#END");
            song.music = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
                var switcher = SceneSwitcher.Instance; switcher.PracticeMode = practice;
                switcher.Play(song, "Oni", false);
                yield return Wait(() => !switcher.IsInputBlocked && SceneManager.GetActiveScene().name == switcher.SelectedPlayScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                yield return Wait(() => play.Session != null && (!practice || play.Practice != null));
                yield return null;
                Assert.That(play.Session.JudgeOffset, Is.EqualTo(b / 1000.0));
                if (practice)
                {
                    Assert.That(play.BarRoot(0).anchoredPosition.x, Is.EqualTo(120).Within(.1));
                    Assert.That(play.SongTime, Is.EqualTo(a / 1000.0).Within(1e-6), "B must not move the bar cursor or audio seek.");
                    play.MovePractice(1); yield return new WaitForSecondsRealtime(.25f);
                    Assert.That(play.BarRoot(1).anchoredPosition.x, Is.EqualTo(120).Within(.1));
                    Assert.That(play.SongTime, Is.EqualTo(2 + a / 1000.0).Within(1e-6));
                    play.MovePractice(-1); yield return new WaitForSecondsRealtime(.25f);
                    play.ConfirmPractice(); yield return null;
                    play.MovePractice(-1); play.MovePractice(-1);
                    play.ConfirmPractice(); yield return null;
                    Assert.That(play.Practice.Speed, Is.EqualTo(.8));
                    Assert.That(play.Session.JudgeOffset, Is.EqualTo(b / 1000.0));
                }
                double hitAt = 1 + (a + b) / 1000.0;
                yield return Wait(() => play.SongTime >= hitAt);
                Assert.That(play.SongTime - hitAt, Is.LessThan(PlaySession.GoodWindow));
                double songTime = play.SongTime;
                Assert.That(play.music.IsAudioPlaying(), Is.True);
                Assert.That(play.music.AudioPosition(), Is.EqualTo(songTime).Within(.15), "A/B must not desynchronize the music clock.");
                Assert.That(play.RenderedTime, Is.EqualTo(songTime - a / 1000.0).Within(1e-6), "A moves notes; B leaves visual time unchanged.");
                Assert.That(play.Session.Bad, Is.Zero, "A delayed window must not time out early.");
                play.Hit(false, false);
                Assert.That(play.Session.Good, Is.EqualTo(1));
                Assert.That(play.Record.Inputs[0].Ms, Is.EqualTo((songTime - (a + b) / 1000.0) * 1000).Within(1e-6));
                Assert.That(play.Record.AudioOffsetMs, Is.EqualTo(a + b));
                Assert.That(play.Record.VisualOffsetMs, Is.EqualTo(-b));
                Assert.That(play.hitFace.IsPlaying, Is.True);
                if (practice)
                {
                    play.TogglePause(); yield return null;
                    Assert.That(play.Session.Good, Is.Zero);
                    Assert.That(play.Session.JudgeOffset, Is.EqualTo(b / 1000.0));
                    Assert.That(play.RenderedTime, Is.EqualTo(songTime - a / 1000.0).Within(.05));
                }
            }
            finally
            {
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (play != null) Object.Destroy(play.gameObject);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
                SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            }
        }

        static void Click(PointerRelay target)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)target.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.SameAs(target.gameObject));
            ExecuteEvents.Execute(target.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
        }
        static IEnumerator Press(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        static IEnumerator Wait(Func<bool> condition)
        {
            double end = Time.realtimeSinceStartupAsDouble + 25;
            while (!condition()) { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(end)); yield return null; }
        }
    }
}
