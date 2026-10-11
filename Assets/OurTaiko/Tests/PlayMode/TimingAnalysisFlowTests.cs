using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class TimingAnalysisFlowTests
    {
        [UnityTest]
        public IEnumerator ResultSupportsManualExitCyclicPagesAndReadOnlyChart()
        {
            Keyboard keyboard = null;
            var oldBackground = InputSystem.settings.backgroundBehavior;
            var oldEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                var switcher = SceneSwitcher.Instance;
                var rng = new System.Random(17);
                var samples = Enumerable.Range(0, 820).Select(i => new HitTiming(i,
                    Enumerable.Range(0, 12).Sum(_ => rng.NextDouble() * 12) - 66, Judgment.Good)).ToArray();
                var stats = new TimingStatistics(samples, 6, 25.025, 75.075, 108.442);
                var run = new PlayResult { Title = "Timing Analysis", Score = 1006540, Good = 780, Ok = 40, Bad = 6,
                    MaxCombo = 233, Rolls = 41, GaugePoints = 10000, IsClear = true, IsGaugeFull = true,
                    Timing = stats };
                switcher.ShowResult(run);
                yield return WaitForScene(SceneSwitcher.ResultScene);
                yield return new WaitForSecondsRealtime(.6f);
                var result = Object.FindFirstObjectByType<ResultScene>(); var view = result.view.analysis;
                Assert.That(view, Is.Not.Null);
                Assert.That(result.stage.Find("TouchArea"), Is.Null);
                Assert.That(view.histogram.raycastTarget, Is.False);
                Assert.That(view.swipeArea.Clicked, Is.Null);
                Assert.That(result.view.crown.transform.IsChildOf(view.scorePage.transform), Is.True);
                Assert.That(result.view.scoreRank.transform.IsChildOf(view.scorePage.transform), Is.True);
                Assert.That(view.actionLabel.text, Is.EqualTo("跳过演出"));
                int revision = ScoreStore.Shared.Revision;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); yield return null; yield return null;
                Assert.That(result.Sequence.Skipped, Is.False, "Enter is not an exit/skip shortcut.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return null; yield return null;
                Assert.That(result.Sequence.Skipped, Is.True);
                Assert.That(result.IsLeaving, Is.False, "The skip press never exits.");
                Assert.That(view.actionLabel.text, Is.EqualTo("返回选曲"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                yield return new WaitForSecondsRealtime(.25f);
                TestCapture.Capture("TimingResultScore.png");
                // Both Ka keys in a frame are one action, not two toggles that cancel out.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.K)); yield return null; yield return null;
                Assert.That(view.Details, Is.True);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                yield return new WaitForSecondsRealtime(.25f);
                Assert.That(view.scorePage.gameObject.activeSelf, Is.False);
                Assert.That(view.detailPage.gameObject.activeSelf, Is.True);
                Assert.That(view.counts.text, Is.EqualTo("有效击打 820    漏打 6"));
                Assert.That(view.histogram.AxisMaximum, Is.GreaterThanOrEqualTo(stats.Bins.Max()));
                TestCapture.Capture("TimingResultDetails.png");
                TestCapture.Capture("TimingResultDetails720.png", 1280, 720);

                Swipe(view.swipeArea, new Vector2(-180, 0)); yield return new WaitForSecondsRealtime(.2f);
                Assert.That(view.Details, Is.False, "Left swipe wraps details back to score.");
                Swipe(view.swipeArea, new Vector2(180, 0)); yield return new WaitForSecondsRealtime(.2f);
                Assert.That(view.Details, Is.True, "Right swipe wraps score back to details.");
                Swipe(view.swipeArea, new Vector2(10, 160)); yield return null;
                Assert.That(view.Details, Is.True, "Vertical gestures do not flip.");
                Swipe(view.swipeArea, new Vector2(8, 0)); yield return null;
                Assert.That(view.Details, Is.True, "A small movement does not flip.");
                Assert.That(result.IsLeaving, Is.False);
                view.previous.Clicked(); yield return new WaitForSecondsRealtime(.2f);
                Assert.That(view.Details, Is.False);
                view.detailDot.Clicked(); yield return new WaitForSecondsRealtime(.2f);
                Assert.That(view.Details, Is.True);
                Assert.That(ScoreStore.Shared.Revision, Is.EqualTo(revision), "Page changes never resubmit scores.");
                Assert.That(result.Sequence.ShouldAutoAdvance, Is.False);
                Swipe(view.action, new Vector2(180, 0)); yield return null;
                Assert.That(result.IsLeaving, Is.False, "Dragging the exit button is not a click.");
                Click(view.action); yield return null;
                Assert.That(result.IsLeaving, Is.True);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = oldBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor;
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest] public IEnumerator SinglePlayShowsSignedTiming() => CheckFeedback(false);
        [UnityTest] public IEnumerator PracticeShowsTimingAndClearsOnSeek() => CheckFeedback(true);
        [UnityTest]
        public IEnumerator EarlyPageSwitchSettlesRevealAndEmptyStatesRemainReadable()
        {
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                var run = new PlayResult { Title = "Timing Empty", Good = 0, Bad = 4,
                    Timing = new TimingStatistics(new HitTiming[0], 4, 25.025, 75.075, 108.442) };
                SceneSwitcher.Instance.ShowResult(run);
                yield return WaitForScene(SceneSwitcher.ResultScene);
                yield return new WaitForSecondsRealtime(.6f);
                var result = Object.FindFirstObjectByType<ResultScene>(); var view = result.view.analysis;
                Assert.That(result.Sequence.CanAdvance, Is.False);
                result.ChangePage(1); // This is also the Ka / swipe path while revealing.
                result.Don();
                Assert.That(result.IsLeaving, Is.False, "Two actions in one frame cannot skip and exit.");
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(result.Sequence.CanAdvance, Is.True);
                Assert.That(view.Details, Is.True);
                Assert.That(view.empty.text, Is.EqualTo("暂无有效击打"));
                Assert.That(view.mean.text, Is.EqualTo("—"));
                Assert.That(view.deviation.text, Is.EqualTo("—"));
                Assert.That(view.counts.text, Is.EqualTo("有效击打 0    漏打 4"));
                AssertTopRaycast(view.action);
                AssertTopRaycast(view.swipeArea);
                AssertTopRaycast(view.scoreDot);
                AssertTopRaycast(view.next);
                TestCapture.Capture("TimingEmpty720.png", 1280, 720);
                view.Bind(new PlayResult { AutoPlay = true });
                view.SetPage(true, 1);
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(view.empty.text, Is.EqualTo("自动演奏不统计手动偏差"));
                Assert.That(view.mean.text, Is.EqualTo("—"));
                Assert.That(view.histogram.AxisMaximum, Is.EqualTo(4));
                TestCapture.Capture("TimingAuto720.png", 1280, 720);
                result.Don();
                Assert.That(result.IsLeaving, Is.True, "Don exits directly after the reveal is settled.");
                yield return null;
            }
            finally { if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject); }
        }
        static void AssertTopRaycast(ResultPointerControl control)
        {
            Canvas.ForceUpdateCanvases();
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(Pointer(control), hits);
            Assert.That(hits.FirstOrDefault().gameObject, Is.SameAs(control.gameObject));
        }
        static IEnumerator CheckFeedback(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Timing Feedback\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n5008,\n0000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
                SceneSwitcher.Instance.PracticeMode = practice;
                SceneSwitcher.Instance.Play(song, "Oni", false);
                yield return WaitForScene(practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var feedback = play.timingFeedback;
                Assert.That(feedback, Is.Not.Null);
                var notes = play.Session.Chart.Notes;
                play.Session.Hit(false, notes[0].Time + play.Session.JudgeOffset - .02);
                Assert.That(feedback.label.text, Is.EqualTo("-20ms"));
                Assert.That(feedback.label.color, Is.EqualTo(TimingFeedbackView.EarlyColor));
                Assert.That(feedback.label.raycastTarget, Is.False);
                yield return null;
                TestCapture.Capture("TimingEarly-" + (practice ? "Practice" : "Play") + ".png");
                play.Session.Hit(false, notes[1].Time + play.Session.JudgeOffset + .05);
                Assert.That(feedback.label.text, Is.EqualTo("+50ms"));
                Assert.That(feedback.label.color, Is.EqualTo(TimingFeedbackView.LateColor));
                yield return null;
                TestCapture.Capture("TimingLate-" + (practice ? "Practice" : "Play") + ".png");
                TestCapture.Capture("TimingLate720-" + (practice ? "Practice" : "Play") + ".png", 1280, 720);
                play.Session.Advance(notes[2].Time + play.Session.JudgeOffset + .2, false);
                Assert.That(feedback.label.alpha, Is.Zero, "A miss must not display an invented offset.");
                play.Session.Hit(false, notes[3].Time + play.Session.JudgeOffset + .09);
                Assert.That(feedback.label.text, Is.EqualTo("+90ms"), "Hit Bad still has a measured offset.");
                if (practice)
                {
                    play.MovePractice(1);
                    Assert.That(feedback.label.alpha, Is.Zero);
                    Assert.That(play.Session.HitTimings, Is.Empty);
                }
                else
                {
                    yield return new WaitForSecondsRealtime(.5f);
                    Assert.That(feedback.label.alpha, Is.Zero);
                    play.Session.Hit(false, notes[4].Time + play.Session.JudgeOffset + .1);
                    Assert.That(feedback.label.alpha, Is.Zero, "Rolls cannot show note timing.");
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
        static void Click(ResultPointerControl control)
        {
            var data = Pointer(control); control.OnPointerDown(data); control.OnPointerUp(data); control.OnPointerClick(data);
        }
        static void Swipe(ResultPointerControl control, Vector2 delta)
        {
            var data = Pointer(control); control.OnPointerDown(data);
            var rect = (RectTransform)control.transform;
            data.position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint((Vector3)delta));
            control.OnBeginDrag(data); control.OnDrag(data); control.OnPointerUp(data); control.OnEndDrag(data); control.OnPointerClick(data);
        }
        static PointerEventData Pointer(ResultPointerControl control) => new PointerEventData(EventSystem.current)
        {
            pointerId = 3, button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null, control.transform.position),
        };
        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsInputBlocked || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
