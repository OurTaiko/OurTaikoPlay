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
        public IEnumerator AutoStrikesAlternateAcrossAllLongNotes() => CheckAutoHands(false);

        [UnityTest]
        public IEnumerator PracticeAutoStrikesAlternateAndResetOnSeek() => CheckAutoHands(true);

        static IEnumerator CheckAutoHands(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Auto Hands\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:3,4\n#START\n"
                + "0000,\n1508,\n2608,\n3708,\n4908,\n2000,\n#END");
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
                if (!play.IsPaused) play.TogglePause();
                yield return null;

                void ClearFlashes()
                {
                    foreach (var flash in play.drumFlashes)
                    {
                        var sampler = flash.GetComponent<ClipSampler>();
                        sampler.Sample(sampler.clip.length);
                    }
                }

                int strikes = 0;
                var longKinds = new System.Collections.Generic.HashSet<NoteKind>();
                void Observe(int index, Judgment result)
                {
                    var note = play.Session.Chart.Notes[index];
                    int expectedFlash = (note.IsKa ? 2 : 0) + (strikes % 2 == 0 ? 1 : 0);
                    for (int i = 0; i < play.drumFlashes.Length; i++)
                        Assert.That(play.drumFlashes[i].enabled, Is.EqualTo(i == expectedFlash),
                            $"Strike {strikes + 1}, {note.Kind}: expected drum flash {expectedFlash}.");
                    if (result == Judgment.Roll) longKinds.Add(note.Kind);
                    strikes++;
                    ClearFlashes();
                }

                ClearFlashes();
                play.Session.Judged += Observe;
                play.Session.Advance(2, true);
                Assert.That(strikes, Is.EqualTo(1));
                if (practice)
                {
                    // Seeking after an odd number of strikes must start the next attempt on the right.
                    play.MovePractice(0);
                    strikes = 0;
                    play.Session.Judged += Observe;
                    play.Session.Advance(2, true);
                    Assert.That(strikes, Is.EqualTo(1));
                }
                // Several hits are caught up in one frame, spanning normal notes and all long kinds.
                play.Session.Advance(11, true);
                Assert.That(longKinds, Is.EquivalentTo(new[]
                    { NoteKind.Roll, NoteKind.BigRoll, NoteKind.Balloon, NoteKind.Kusudama }));
                Assert.That(play.Session.LongHits[1], Is.EqualTo(16));
                Assert.That(play.Session.LongHits[3], Is.EqualTo(16));
                Assert.That(play.Session.LongHits[5], Is.EqualTo(3));
                Assert.That(play.Session.LongHits[7], Is.EqualTo(4));
                Assert.That(strikes, Is.EqualTo(44));
                play.Session.Advance(11, true);
                Assert.That(strikes, Is.EqualTo(44), "Advancing without a new hit must not strike again.");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator LongHitsDoNotRestartJudgmentTextInSinglePlay() => CheckLongHitText(false);

        [UnityTest]
        public IEnumerator LongHitsDoNotRestartJudgmentTextInPractice() => CheckLongHitText(true);

        static IEnumerator CheckLongHitText(bool practice)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            // Each long note starts 125 ms after a normal note, while its text is still visible.
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
                float displaySeconds = play.judgment.GetComponent<ClipSampler>().clip.length;
                while (normalHits < 5 || GameTimeline.FrameTime - lastNormal <= displaySeconds + .05)
                {
                    yield return new WaitForEndOfFrame();
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline),
                        $"Observed {normalHits} normal hits; song time {play.SongTime:F3}, paused {play.IsPaused}.");
                    double elapsed = GameTimeline.FrameTime - lastNormal;
                    float expectedAlpha = elapsed < displaySeconds ? 1 : 0;
                    Assert.That(play.judgment.color.a, Is.EqualTo(expectedAlpha).Within(.025f),
                        $"Long-note hits must not extend the previous judgment text ({elapsed:F3}s since normal hit).");
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
            Assert.That(AudioEngine.Instance.Available, Is.True);
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
