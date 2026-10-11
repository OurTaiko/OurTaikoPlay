using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class TimingCalibrationFlowTests
    {
        [UnityTest] public IEnumerator AudioButtonSavesRecommendationWithoutChangingJudgment() => CheckApply(true);
        [UnityTest] public IEnumerator JudgmentButtonSavesRecommendationWithoutChangingAudio() => CheckApply(false);

        static PlayResult Run(bool auto = false, bool empty = false)
        {
            var stats = new TimingStatistics(empty ? Array.Empty<HitTiming>() : new[] {
                new HitTiming(0, -20.2, Judgment.Good), new HitTiming(1, -40, Judgment.Ok)
            }, 0, 25.025, 75.075, 108.442);
            return new PlayResult { Title = "Timing Calibration", Good = 1, Ok = 1, AutoPlay = auto,
                Timing = stats, Calibration = new TimingCalibration(stats, 12, -4) };
        }
        static IEnumerator Open(PlayResult run)
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            SceneSwitcher.Instance.ShowResult(run);
            float deadline = Time.realtimeSinceStartup + 15;
            while (SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene || SceneSwitcher.Instance.IsInputBlocked)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.6f);
            Object.FindFirstObjectByType<ResultScene>().ShowPage(true);
            yield return new WaitForSecondsRealtime(.25f);
        }
        static void Click(ResultPointerControl control)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)control.transform;
            var data = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left, pointerId = 10
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.EqualTo(control.gameObject), "The swipe surface must not cover calibration buttons.");
            control.OnPointerDown(data); control.OnPointerUp(data); control.OnPointerClick(data);
        }
        static IEnumerator CheckApply(bool audio)
        {
            var manager = SettingManager.EnsureInstance();
            string dir = Path.Combine(Application.temporaryCachePath, "timing-calibration-" + Guid.NewGuid());
            manager.Load(Path.Combine(dir, "settings.json"));
            var settings = manager.Settings.Clone(); settings.play.audioOffsetMs = 12; settings.play.judgeOffsetMs = -4;
            manager.Set(settings);
            try
            {
                var run = Run(); yield return Open(run);
                var scene = Object.FindFirstObjectByType<ResultScene>(); var view = scene.view.analysis;
                Assert.That(view.Details, Is.True);
                Assert.That(view.audioRecommendation.text, Is.EqualTo("+12 ms → -3 ms"));
                Assert.That(view.judgeRecommendation.text, Is.EqualTo("-4 ms → -19 ms"));
                long revision = ScoreStore.Shared.Revision;
                TestCapture.Capture("TimingCalibration.png");
                TestCapture.Capture("TimingCalibration720.png", 1280, 720);
                Click(audio ? view.adjustAudio : view.adjustJudge);
                yield return null;
                Assert.That(scene.IsLeaving, Is.False);
                var saved = GameSettings.FromJson(File.ReadAllText(manager.FilePath));
                Assert.That(saved.play.audioOffsetMs, Is.EqualTo(audio ? -3 : 12));
                Assert.That(saved.play.judgeOffsetMs, Is.EqualTo(audio ? -4 : -19));
                Assert.That(view.calibrationStatus.text, Does.Contain("已保存 " + (audio ? "A" : "B")));
                Click(audio ? view.adjustAudio : view.adjustJudge); yield return null;
                Assert.That(manager.Settings.play.audioOffsetMs, Is.EqualTo(saved.play.audioOffsetMs));
                Assert.That(manager.Settings.play.judgeOffsetMs, Is.EqualTo(saved.play.judgeOffsetMs));
                Click(audio ? view.adjustJudge : view.adjustAudio); yield return null;
                Assert.That(manager.Settings.play.audioOffsetMs, Is.EqualTo(-3));
                Assert.That(manager.Settings.play.judgeOffsetMs, Is.EqualTo(-19));
                saved = GameSettings.FromJson(File.ReadAllText(manager.FilePath));
                Assert.That(saved.play.audioOffsetMs, Is.EqualTo(-3));
                Assert.That(saved.play.judgeOffsetMs, Is.EqualTo(-19));
                view.Bind(run); yield return null;
                scene.ShowPage(true); yield return new WaitForSecondsRealtime(.25f);
                Assert.That(view.calibrationStatus.text, Does.Contain("已保存"), "Rebinding the same result cannot apply twice.");
                Click(view.adjustAudio); yield return null; Click(view.adjustJudge); yield return null;
                Assert.That(manager.Settings.play.audioOffsetMs, Is.EqualTo(-3));
                Assert.That(manager.Settings.play.judgeOffsetMs, Is.EqualTo(-19));
                Assert.That(ScoreStore.Shared.Revision, Is.EqualTo(revision));
                TestCapture.Capture("TimingCalibrationApplied.png");
            }
            finally
            {
                manager.UseUnsaved(new GameSettings());
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AutoplayEmptyAndStaleRunsCannotChangeOffsets()
        {
            var manager = SettingManager.EnsureInstance();
            var settings = manager.Settings.Clone(); settings.play.audioOffsetMs = 12; settings.play.judgeOffsetMs = -4;
            manager.UseUnsaved(settings);
            try
            {
                yield return Open(Run());
                var scene = Object.FindFirstObjectByType<ResultScene>(); var view = scene.view.analysis;
                foreach (var run in new[] { Run(auto: true), Run(empty: true), Run() })
                {
                    settings.play.audioOffsetMs = 13; manager.UseUnsaved(settings);
                    view.Bind(run); yield return null;
                    scene.ShowPage(true); yield return new WaitForSecondsRealtime(.25f);
                    Assert.That(view.ApplyOffset(true), Is.False);
                    Assert.That(view.ApplyOffset(false), Is.False);
                    Assert.That(manager.Settings.play.audioOffsetMs, Is.EqualTo(13));
                    Assert.That(manager.Settings.play.judgeOffsetMs, Is.EqualTo(-4));
                    Assert.That(view.calibrationStatus.text, Does.Contain(run.AutoPlay ? "自动演奏" : run.Timing.Count == 0 ? "暂无有效击打" : "已变化"));
                }
            }
            finally
            {
                manager.UseUnsaved(new GameSettings());
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }
    }
}
