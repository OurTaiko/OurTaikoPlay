using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class HitFeedbackClockTests
    {
        [UnityTest]
        public IEnumerator LongHitsDoNotRestartJudgmentTextInSinglePlay() => CheckLongHitText(false);

        [UnityTest]
        public IEnumerator LongHitsDoNotRestartJudgmentTextInPractice() => CheckLongHitText(true);

        static IEnumerator CheckLongHitText(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            // Each long note starts 125 ms after a normal note, while its text is still fading.
            song.chart = new TextAsset("TITLE:Long Hit Text\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:12,100\n#START\n"
                + "1700000000000008,\n1500000000000008,\n1600000000000008,\n1900000000000008,\n1000,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                var switcher = SceneSwitcher.Instance;
                switcher.PracticeMode = practice;
                switcher.Play(song, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 30;
                string target = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                while (switcher.IsInputBlocked || SceneManager.GetActiveScene().name != target)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (practice)
                {
                    yield return null;
                    play.ConfirmPractice(); yield return null; play.ConfirmPractice();
                    Assert.That(play.IsPaused, Is.False);
                }
                double lastNormal = double.NegativeInfinity;
                int normalHits = 0;
                var observedLongNotes = new System.Collections.Generic.HashSet<NoteKind>();
                play.Session.Judged += (index, result) =>
                {
                    if (result != Judgment.Roll) { lastNormal = GameTimeline.FrameTime; normalHits++; }
                    else observedLongNotes.Add(play.Session.Chart.Notes[index].Kind);
                };
                float fadeSeconds = play.judgment.GetComponent<ClipSampler>().clip.length;
                while (normalHits < 5 || GameTimeline.FrameTime - lastNormal <= fadeSeconds + .05)
                {
                    yield return new WaitForEndOfFrame();
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    double elapsed = GameTimeline.FrameTime - lastNormal;
                    float expectedAlpha = Mathf.Clamp01(1 - (float)(elapsed / fadeSeconds));
                    Assert.That(play.judgment.color.a, Is.EqualTo(expectedAlpha).Within(.025f),
                        $"Long-note hits must let the previous normal judgment fade naturally ({elapsed:F3}s since normal hit).");
                }
                Assert.That(observedLongNotes, Is.EquivalentTo(new[] { NoteKind.Balloon, NoteKind.Roll, NoteKind.BigRoll, NoteKind.Kusudama }));
                Assert.That(play.Session.Rolls, Is.GreaterThan(0), "Roll counting still works.");
                Assert.That(play.Session.LongHits[1], Is.EqualTo(12), "Balloon hits and its final pop do not restart the text.");
                Assert.That(play.Session.Resolved[1], Is.True);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator HitFaceSurvivesJudgmentFrameWithNativeClock()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Native Clock Feedback\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1010,\n3010,\n0000,\n#END");
            var settings = SettingManager.EnsureInstance();
            settings.UseUnsaved(new GameSettings());
            var apply = AudioEngine.EnsureInstance().ApplyPendingSettingsAsync();
            while (!apply.IsCompleted) yield return null;
            Assert.That(apply.IsFaulted, Is.False);
            Assert.That(AudioEngine.Instance.Native, Is.True);
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song, "Oni", true);
                float deadline = Time.realtimeSinceStartup + 20;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                int judged = 0, observed = 0;
                play.Session.Judged += (_, result) => { if (result == Judgment.Good) judged++; };
                while (observed < 3)
                {
                    yield return new WaitForEndOfFrame();
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    if (judged == observed) continue;
                    Assert.That(play.hitFace.IsPlaying && play.hitFace.image.enabled, Is.True,
                        "The face must remain visible on the actual judgment frame without manually sampling its clock");
                    Assert.That(play.hitRing.IsPlaying && play.hitRing.image.enabled, Is.True);
                    Assert.That(play.hitFace.image.color.a, Is.GreaterThan(0));
                    observed = judged;
                    if (observed == 1) TestCapture.Capture("HitFaceNativeClock.png");
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
