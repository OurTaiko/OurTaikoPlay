using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
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
            yield return StartPractice(AudioBackend.BassSimple, true);
            yield return Click(play.practiceView.confirm);
            yield return Click(play.practiceView.confirm);
            yield return Wait(() => !play.IsPaused);
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
            yield return Wait(() => !play.IsPaused);
            Assert.That(play.SongTime, Is.LessThan(target - 1.4));
            yield return new WaitForSecondsRealtime(1);
            Assert.That(play.SongTime, Is.LessThan(target));
            Assert.That(play.Session.Bad, Is.Zero, "Preparation must not count skipped notes as misses.");
        }
        [UnityTest, Category("SpriteRendering")] public IEnumerator NativePlaybackAndTwoLayerPause() => ExercisePlayback(AudioBackend.BassSimple);

        IEnumerator ExercisePlayback(AudioBackend backend)
        {
            yield return StartPractice(backend, true);
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Measure), "A chart without branches has no branch page.");
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
            Assert.That(play.practiceView.panel.activeSelf, Is.False);
            Assert.That(play.IsPaused, Is.True, "The lane scrolls back to the preparation start before playback.");
            Assert.That(play.RenderedTime, Is.InRange(0.4, 2), "Starting must animate like a measure skip, not teleport.");
            play.MovePractice(-1); play.ConfirmPractice();
            Assert.That(play.Practice.Target, Is.EqualTo(2), "The scroll back accepts no menu input.");
            yield return Wait(() => !play.IsPaused);
            Assert.That(play.RenderedTime, Is.EqualTo(0.4).Within(0.05));
            yield return null;
            for (int i = 0; i < 4; i++)
                Assert.That(play.NoteRoot(i), Is.Null, "Notes before the chosen measure stay hidden during the preparation.");
            Assert.That(play.NoteRoot(4), Is.Not.Null);
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
            yield return StartPractice(AudioBackend.BassSimple, false);
            // Jump near the end, then run the complete end-of-chart path.
            play.MovePractice(1); yield return null; play.MovePractice(1);
            yield return new WaitForSecondsRealtime(0.25f);
            play.ConfirmPractice(); yield return null;
            play.ConfirmPractice();
            yield return Wait(() => !play.IsPaused);
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
        [UnityTest, Category("SpriteRendering")]
        public IEnumerator FinishedPracticePreservesTimingReviewUntilTheNextAttempt()
        {
            yield return StartPractice(AudioBackend.BassSimple, false, Plain.Replace("#START", "#START\n#SCROLL 0.3"));
            yield return Press(Key.F); yield return Press(Key.F);
            yield return Wait(() => !play.IsPaused);
            var completed = play.Session;
            completed.Hit(false, completed.Chart.Notes[0].Time + .012);
            var settingsManager = SettingManager.EnsureInstance();
            var settings = settingsManager.Settings.Clone();
            settings.play.practiceTimingReview = false;
            settingsManager.Set(settings);
            // Let the remaining notes miss and exercise Finish(), rather than manually pausing.
            yield return Wait(() => play.IsPaused);
            Assert.That(play.Practice.Target, Is.EqualTo(play.Practice.First));
            Assert.That(play.Session, Is.Not.SameAs(completed));
            Assert.That(play.Session.HitTimings, Is.Empty, "The browsing session must not inherit judgments.");
            Assert.That(SceneSwitcher.Instance.LastResult, Is.Null);
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Any(t => t.name == "PracticeTiming" && t.gameObject.activeInHierarchy), Is.False);
            settings.play.practiceTimingReview = true;
            settingsManager.Set(settings);
            yield return new WaitForSecondsRealtime(.15f);
            Canvas.ForceUpdateCanvases();
            var hit = play.NoteRoot(0).Find("PracticeTiming").GetComponent<TMP_Text>();
            var missed = play.NoteRoot(1).Find("PracticeTiming").GetComponent<TMP_Text>();
            Assert.That(hit.gameObject.activeInHierarchy, Is.True, "Song end must preserve the completed attempt's review.");
            Assert.That(hit.text, Is.EqualTo("+12"));
            Assert.That(missed.gameObject.activeInHierarchy, Is.True);
            Assert.That(missed.text, Is.EqualTo("×"));
            Assert.That(missed.color, Is.EqualTo((Color)new Color32(200, 200, 200, 255)));
            TestCapture.Capture("PracticeTimingReviewAfterFinish.png");
            yield return Press(Key.F); yield return Press(Key.F);
            yield return Wait(() => !play.IsPaused);
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => t.name == "PracticeTiming").All(t => !t.gameObject.activeInHierarchy), Is.True,
                "Starting another attempt must clear the completed attempt's review.");
            yield return Press(Key.Space);
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => t.name == "PracticeTiming").All(t => !t.gameObject.activeInHierarchy), Is.True,
                "Pausing a fresh attempt must not restore the completed attempt's review.");
        }
        [UnityTest]
        public IEnumerator DirectEditorSceneLaunchRetainsPracticeSelection()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            TestSongs.Select();
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.PracticeScene);
            play = Object.FindAnyObjectByType<PlayScene>();
            yield return Wait(() => play.Practice != null && play.IsPaused);
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True);
            Assert.That(SceneSwitcher.Instance.SelectedPlayScene, Is.EqualTo(SceneSwitcher.PracticeScene));
        }
        const string Plain = "TITLE:Practice Test\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n1111,\n1111,\n#END";
        [UnityTest, Category("SpriteRendering")]
        public IEnumerator PausedPracticeShowsNoteTimingAboveAndBelowDenseNotes()
        {
            yield return StartPractice(AudioBackend.BassSimple, false,
                "TITLE:Timing Review\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n#SCROLL 0.3\n1111111111111111,\n1111,\n1111,\n#END");
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Any(t => t.name == "PracticeTiming" && t.gameObject.activeInHierarchy), Is.False);
            yield return Press(Key.F); yield return Press(Key.F);
            yield return Wait(() => !play.IsPaused);
            var attempt = play.Session;
            var hitIndices = Enumerable.Range(0, 12).Where(i => i != 3 && i != 8).ToArray();
            foreach (int i in hitIndices)
                attempt.Hit(false, attempt.Chart.Notes[i].Time + (i % 2 == 0 ? .012 : -.008));
            Assert.That(attempt.HitTimings.Count, Is.EqualTo(hitIndices.Length));
            yield return Press(Key.Space);
            Assert.That(play.IsPaused, Is.True);
            Assert.That(play.pausePanel.activeSelf, Is.False, "Space opens the practice measure browser.");
            Assert.That(play.Session.HitTimings.Count, Is.Zero, "Review must not carry judgments into the next attempt.");
            // The synthetic judgments were seeded during preparation; browse to their measure.
            var browsingSession = play.Session;
            play.MovePractice(1);
            yield return new WaitForSecondsRealtime(.3f);
            Canvas.ForceUpdateCanvases();
            Assert.That(play.Session, Is.SameAs(browsingSession), "Measure browsing must not rebuild chart-sized judgment state.");
            var labels = hitIndices.Select(i => play.NoteRoot(i).Find("PracticeTiming").GetComponent<TMP_Text>()).ToArray();
            for (int i = 0; i < labels.Length; i++)
            {
                Assert.That(labels[i].gameObject.activeInHierarchy, Is.True);
                Assert.That(labels[i].text, Is.EqualTo(hitIndices[i] % 2 == 0 ? "+12" : "-8"));
                Assert.That(labels[i].color, Is.EqualTo(hitIndices[i] % 2 == 0 ? TimingFeedbackView.LateColor : TimingFeedbackView.EarlyColor));
                Assert.That(labels[i].rectTransform.anchoredPosition.x, Is.Zero, "Labels stay attached to their own note.");
                Assert.That(labels[i].rectTransform.anchoredPosition.y, Is.EqualTo(hitIndices[i] % 2 == 0 ? 72 : -72),
                    "Dense labels alternate above/below by note order, including missed notes.");
                Assert.That(labels[i], Is.TypeOf<TextMeshPro>(), "Timing shares the note's world-space sorting group.");
            }
            foreach (int missed in new[] { 3, 8 })
            {
                var label = play.NoteRoot(missed).Find("PracticeTiming").GetComponent<TMP_Text>();
                Assert.That(label.gameObject.activeInHierarchy, Is.True);
                Assert.That(label.text, Is.EqualTo("×"));
                Assert.That(label.color, Is.EqualTo((Color)new Color32(200, 200, 200, 255)));
                Assert.That(label.rectTransform.anchoredPosition.y, Is.EqualTo(missed % 2 == 0 ? 72 : -72));
            }
            Assert.That(play.NoteRoot(12).Find("PracticeTiming")?.gameObject.activeSelf ?? false, Is.False,
                "An unhit note has no timing value.");
            TestCapture.Capture("PracticeTimingReview.png");
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(labels.All(t => t.gameObject.activeInHierarchy), Is.True, "Review does not fade like live hit feedback.");
            play.MovePractice(1); yield return new WaitForSecondsRealtime(.25f);
            play.MovePractice(-1); yield return new WaitForSecondsRealtime(.25f);
            Assert.That(play.NoteRoot(0).Find("PracticeTiming").GetComponent<TMP_Text>().text, Is.EqualTo("+12"),
                "Browsing measures must preserve the last attempt's review.");
            Assert.That(play.NoteRoot(0).Find("PracticeTiming").GetComponent<TMP_Text>().rectTransform.anchoredPosition.y,
                Is.EqualTo(72), "Returning to an earlier measure preserves the above/below assignment.");
            Assert.That(play.NoteRoot(1).Find("PracticeTiming").GetComponent<TMP_Text>().rectTransform.anchoredPosition.y,
                Is.EqualTo(-72));
            yield return Press(Key.F); yield return Press(Key.F);
            yield return Wait(() => !play.IsPaused);
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => t.name == "PracticeTiming").All(t => !t.gameObject.activeInHierarchy), Is.True,
                "Starting the next attempt hides the old review, including pooled notes.");
            yield return Press(Key.Space);
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => t.name == "PracticeTiming").All(t => !t.gameObject.activeInHierarchy), Is.True,
                "Pausing a new attempt with no hits must not show the previous attempt's values.");
        }
        [UnityTest, Category("SpriteRendering")]
        public IEnumerator TimingReviewCanBeToggledDuringPauseWithoutChangingTheAttempt()
        {
            yield return StartPractice(AudioBackend.BassSimple, false,
                Plain.Replace("#START", "#START\n#SCROLL 0.3"), timingReview: false);
            yield return Press(Key.F); yield return Press(Key.F);
            yield return Wait(() => !play.IsPaused);
            var attempt = play.Session;
            attempt.Hit(false, attempt.Chart.Notes[0].Time + .012);
            attempt.Hit(false, attempt.Chart.Notes[2].Time + .012);
            int score = attempt.Score, timings = attempt.HitTimings.Count, bad = attempt.Bad;
            yield return Press(Key.Space);
            var browsing = play.Session;
            double target = play.Practice.Target;
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Any(t => t.name == "PracticeTiming"), Is.False,
                "Starting with review disabled must not create timing text components.");
            var manager = SettingManager.EnsureInstance();
            var settings = manager.Settings.Clone();
            settings.play.practiceTimingReview = true;
            manager.Set(settings);
            yield return new WaitForSecondsRealtime(.15f);
            var hit = play.NoteRoot(0).Find("PracticeTiming").GetComponent<TMP_Text>();
            var missed = play.NoteRoot(1).Find("PracticeTiming").GetComponent<TMP_Text>();
            Assert.That(hit.gameObject.activeInHierarchy, Is.True);
            Assert.That(hit.text, Is.EqualTo("+12"));
            Assert.That(missed.gameObject.activeInHierarchy, Is.True);
            Assert.That(missed.text, Is.EqualTo("×"));
            TestCapture.Capture("PracticeTimingReviewEnabled.png");
            settings.play.practiceTimingReview = false;
            manager.Set(settings);
            yield return null; yield return null;
            Assert.That(hit.gameObject.activeInHierarchy || missed.gameObject.activeInHierarchy, Is.False,
                "The unchanged paused frame must redraw when the display toggle changes.");
            TestCapture.Capture("PracticeTimingReviewDisabled.png");
            settings.play.practiceTimingReview = true;
            manager.Set(settings);
            yield return null; yield return null;
            Assert.That(hit.gameObject.activeInHierarchy && missed.gameObject.activeInHierarchy, Is.True);
            Assert.That(play.Session, Is.SameAs(browsing));
            Assert.That(play.Practice.Target, Is.EqualTo(target));
            Assert.That((attempt.Score, attempt.HitTimings.Count, attempt.Bad), Is.EqualTo((score, timings, bad)),
                "Toggling review only changes visibility, not the recorded attempt.");
            play.TogglePause();
            yield return new WaitForSecondsRealtime(PauseMenuView.FadeDuration + .05f);
            play.Restart();
            yield return Wait(() => play.Session != browsing && !play.pausePanel.activeSelf);
            Assert.That(play.Practice.Target, Is.EqualTo(play.Practice.First));
            Assert.That(play.firstLane.noteLayer.GetComponentsInChildren<TMP_Text>(true)
                .Where(t => t.name == "PracticeTiming").All(t => !t.gameObject.activeInHierarchy), Is.True,
                "A manual restart must discard the old timing offsets and missed-note marks.");
        }

        // Misses would select 普通 at both branches; practice must keep the menu's route instead.
        const string Branched = "TITLE:Practice Test\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n1111,\n#BRANCHSTART p,10,20\n#N\n1111,\n#E\n2222,\n#M\n3333,\n#BRANCHEND\n#BRANCHSTART p,10,20\n#N\n1111,\n#E\n2222,\n#M\n#MEASURE 2/4\n33,\n33,\n#MEASURE 4/4\n#BRANCHEND\n1111,\n#END";
        [UnityTest]
        public IEnumerator BranchPageComesFirstAndFixesTheRoute()
        {
            yield return StartPractice(AudioBackend.BassSimple, false, Branched);
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Branch));
            Assert.That(play.practiceView.heading.text, Is.EqualTo("谱面分支"));
            Assert.That(play.practiceView.value.text, Is.EqualTo("普通譜面"));
            int bars = play.Practice.Count;
            yield return Press(Key.D);
            Assert.That(play.PracticeBranch, Is.EqualTo(BranchRoute.Normal), "The route list stops at 普通.");
            yield return Press(Key.K); yield return Press(Key.K); yield return Press(Key.K);
            Assert.That(play.PracticeBranch, Is.EqualTo(BranchRoute.Master), "The route list stops at 達人.");
            Assert.That(play.practiceView.value.text, Is.EqualTo("達人譜面"));
            Assert.That(play.Practice.Count, Is.EqualTo(bars + 1), "達人's two half measures replace one bar line list entry.");
            yield return Click(play.practiceView.previous);
            Assert.That(play.PracticeBranch, Is.EqualTo(BranchRoute.Expert));
            Assert.That(play.Practice.Count, Is.EqualTo(bars));
            var notes = play.Session.Chart.Notes;
            Assert.That(notes.Where(n => n.BranchId >= 0).All(n => play.Session.IsPracticePreviewActive(n) == (n.Route == BranchRoute.Expert)), Is.True);
            yield return Press(Key.F);
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Measure));
            Assert.That(play.practiceView.heading.text, Is.EqualTo("小节进度"));
            yield return Press(Key.K);
            Assert.That(play.Practice.Target, Is.EqualTo(1));
            yield return new WaitForSecondsRealtime(0.25f);
            yield return Press(Key.F);
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Speed));
            yield return Press(Key.F);
            yield return Wait(() => !play.IsPaused);
            yield return Wait(() => play.Session.BranchHistory.Count == 2);
            Assert.That(play.Session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Expert, BranchRoute.Expert }));
            Assert.That(play.Session.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
            // A new pause starts again on the branch page and keeps the chosen route.
            play.TogglePause(); yield return null;
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Branch));
            Assert.That(play.PracticeBranch, Is.EqualTo(BranchRoute.Expert));
        }
        // A normal play refuses an s branch and an omitted #M; practice plays them on the menu's route.
        [UnityTest]
        public IEnumerator PracticeOpensScoreBranchesWithOmittedRoutes()
        {
            yield return StartPractice(AudioBackend.BassSimple, false,
                "TITLE:Practice Test\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n#BRANCHSTART s,100,200\n#N\n1111,\n#E\n2222,\n#BRANCHEND\n1111,\n#END");
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Branch));
            yield return Press(Key.K); yield return Press(Key.K);
            Assert.That(play.PracticeBranch, Is.EqualTo(BranchRoute.Master));
            var notes = play.Session.Chart.Notes;
            Assert.That(notes.Where(n => n.BranchId >= 0).All(n => play.Session.IsPracticePreviewActive(n) == (n.Route == BranchRoute.Expert)), Is.True,
                "達人 falls back to the authored 玄人 notes.");
            yield return Press(Key.F); yield return Press(Key.F); yield return Press(Key.F);
            yield return Wait(() => play.Session.BranchHistory.Count == 1);
            Assert.That(play.Session.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
        }
        IEnumerator StartPractice(AudioBackend backend, bool withAudio, string chart = Plain, bool timingReview = true)
        {
            var settings = new GameSettings(); settings.audio.backend = backend; settings.play.inputDelayMs = 0;
            settings.play.practiceTimingReview = timingReview;
            SettingManager.EnsureInstance().UseUnsaved(settings);
            var apply = AudioEngine.EnsureInstance().ApplyPendingSettingsAsync();
            while (!apply.IsCompleted) yield return null;
            Assert.That(apply.IsFaulted, Is.False);
            Assert.That(AudioEngine.Instance.Backend, Is.EqualTo(backend));
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene); yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            Assert.That(entry.view.boards.Count(b => b.practice), Is.EqualTo(1));
            Assert.That(entry.view.boards[2].practice, Is.True, "練習モード follows 演奏ゲーム and 段位道場.");
            Assert.That(entry.view.boards[2].scene, Is.EqualTo(SceneSwitcher.ServerLoginScene));
            yield return Wait(() => entry.Flow.CanJoin(entry.Now) && !SceneSwitcher.Instance.IsInputBlocked);
            entry.Don(); yield return Wait(() => entry.Flow.IsModeReady(entry.Now));
            entry.Ka(2); yield return new WaitForSecondsRealtime(0.3f); entry.Don();
            yield return Wait(() => SceneManager.GetActiveScene().name == SceneSwitcher.SongSelectScene && !SceneSwitcher.Instance.IsInputBlocked);
            Assert.That(SceneSwitcher.Instance.PracticeMode, Is.True);
            song = ScriptableObject.CreateInstance<SongDefinition>(); song.name = "Practice Test";
            song.chart = new TextAsset(chart);
            if (withAudio) song.music = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/entry/bgm.ogg");
            SceneSwitcher.Instance.Play(song, "Oni", false);
            yield return Wait(() => SceneManager.GetActiveScene().name == SceneSwitcher.PracticeScene && !SceneSwitcher.Instance.IsInputBlocked);
            play = Object.FindAnyObjectByType<PlayScene>();
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
            var screen = RectTransformUtility.WorldToScreenPoint(rect.GetComponentInParent<Canvas>().rootCanvas.worldCamera, rect.TransformPoint(rect.rect.center));
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
