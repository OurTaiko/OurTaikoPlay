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
