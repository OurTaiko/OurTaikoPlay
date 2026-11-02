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
        public IEnumerator OffsetsAdjustWithDrumKeysAndTouchAndSaveMilliseconds()
        {
            var settings = SettingManager.EnsureInstance();
            string path = Path.Combine(Application.temporaryCachePath, "offset-test-" + Guid.NewGuid(), "settings.json");
            settings.Load(path);
            var background = InputSystem.settings.backgroundBehavior;
            var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // Added only now: a device added while the unfocused Editor still ignored background input starts disabled (batchmode).
            var keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                // Keys are ignored while the switcher's transition still covers the scene.
                yield return Wait(() => !SceneSwitcher.Instance.IsSwitching && !SceneSwitcher.Instance.IsCovered);
                var overlay = SettingsOverlay.Open();
                var nav = overlay.Navigator;
                yield return null;
                // Right ka twice reaches Play, don opens it on Offset A, don starts adjusting it.
                yield return Press(keyboard, Key.K); yield return Press(keyboard, Key.K);
                Assert.That(nav.Page.Title, Is.EqualTo("Play"));
                yield return Press(keyboard, Key.F);
                Assert.That(nav.Row.Label, Is.EqualTo("Offset A (Audio)"));
                yield return Press(keyboard, Key.F);
                Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Adjust));
                yield return Press(keyboard, Key.D);
                Assert.That(nav.Row.Format(nav.AdjustValue), Is.EqualTo("-1 ms"));
                Assert.That(settings.Settings.play.audioOffsetMs, Is.Zero);
                yield return Press(keyboard, Key.F);
                Assert.That(settings.Settings.play.audioOffsetMs, Is.EqualTo(-1));
                // The arrows adjust a number directly; the stepper buttons do too.
                overlay.Move(1);
                Assert.That(nav.Row.Label, Is.EqualTo("Offset B (Judgment)"));
                yield return Press(keyboard, Key.RightArrow); yield return Press(keyboard, Key.RightArrow); yield return Press(keyboard, Key.LeftArrow);
                Assert.That(settings.Settings.play.judgeOffsetMs, Is.EqualTo(1));
                overlay.View.SetRow(1, nav.Row.Move(nav.Row.Get(nav.Settings), 1));
                overlay.View.SetRow(1, nav.Row.Move(nav.Row.Get(nav.Settings), -1));
                settings.Load(path);
                Assert.That(settings.Settings.play.audioOffsetMs, Is.EqualTo(-1));
                Assert.That(settings.Settings.play.judgeOffsetMs, Is.EqualTo(1));
                overlay.Confirm(); overlay.Move(1); overlay.Back();
                Assert.That(settings.Settings.play.judgeOffsetMs, Is.EqualTo(1), "Back does not save an adjustment.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
                if (SettingsOverlay.Instance != null) Object.Destroy(SettingsOverlay.Instance.gameObject);
                settings.UseUnsaved(new GameSettings());
                Directory.Delete(Path.GetDirectoryName(path), true);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest, Category("SpriteRendering")] public IEnumerator SinglePlayPositiveOffsets() => CheckPlay(false, 120, 200);
        [UnityTest, Category("SpriteRendering")] public IEnumerator SinglePlayNegativeOffsets() => CheckPlay(false, -120, -200);
        [UnityTest, Category("SpriteRendering")] public IEnumerator PracticePositiveOffsets() => CheckPlay(true, 120, 200);
        [UnityTest, Category("SpriteRendering")] public IEnumerator PracticeNegativeOffsets() => CheckPlay(true, -120, -200);

        static IEnumerator CheckPlay(bool practice, int a, int b)
        {
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings
                { play = new PlaySettings { audioOffsetMs = a, judgeOffsetMs = b, inputDelayMs = 0 } });
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
                var play = Object.FindAnyObjectByType<PlayScene>();
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
                }
                // A and B are real milliseconds: in chart time they shrink with the practice speed.
                double rate = practice ? .8 : 1, audio = a / 1000.0 * rate, judge = b / 1000.0 * rate;
                Assert.That(play.Session.JudgeOffset, Is.EqualTo(judge).Within(1e-12));
                double hitAt = 1 + audio + judge;
                yield return Wait(() => play.SongTime >= hitAt);
                Assert.That(play.SongTime - hitAt, Is.LessThan(PlaySession.GoodWindow * rate));
                double songTime = play.SongTime;
                Assert.That(play.music.IsAudioPlaying(), Is.True);
                Assert.That(play.music.AudioPosition(), Is.EqualTo(songTime).Within(.15), "A/B must not desynchronize the music clock.");
                // Judging and drawing use the play clock's whole millisecond.
                Assert.That(play.RenderedTime, Is.EqualTo(TjaTime.Truncate(songTime - audio)).Within(1e-6), "A moves notes; B leaves visual time unchanged.");
                Assert.That(play.Session.Bad, Is.Zero, "A delayed window must not time out early.");
                play.Hit(false, false);
                Assert.That(play.Session.Good, Is.EqualTo(1));
                Assert.That(play.Record.Inputs[0].Ms, Is.EqualTo(TjaTime.Ms(songTime - audio) - judge * 1000).Within(1e-6));
                Assert.That(play.Record.AudioOffsetMs, Is.EqualTo(a + b));
                Assert.That(play.Record.VisualOffsetMs, Is.EqualTo(-b));
                Assert.That(play.hitFace.IsPlaying, Is.True);
                if (practice)
                {
                    play.TogglePause(); yield return null;
                    Assert.That(play.Session.Good, Is.Zero);
                    Assert.That(play.Session.JudgeOffset, Is.EqualTo(judge).Within(1e-12));
                    Assert.That(play.RenderedTime, Is.EqualTo(songTime - audio).Within(.05));
                }
            }
            finally
            {
                var play = Object.FindAnyObjectByType<PlayScene>();
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
            var point = RectTransformUtility.WorldToScreenPoint(rect.GetComponentInParent<Canvas>().rootCanvas.worldCamera, rect.TransformPoint(rect.rect.center));
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
