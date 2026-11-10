using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Autoplay starts each drum sound one output latency before the judgment, and never twice. On the
    // sent-position clock (AudioOptions.clock) it sounds in the judgment's frame.
    public sealed class AutoSoundAheadFlowTests
    {
        SongDefinition song;
        double latency;
        GameSettings saved;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            AudioEngine.EnsureInstance().OutputLatency = latency;
            if (saved != null) SettingManager.EnsureInstance().UseUnsaved(saved);
            saved = null;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            if (song != null) { Object.Destroy(song.chart); Object.Destroy(song); }
            yield return null;
        }

        [UnityTest] public IEnumerator AutoplaySoundsEachStrikeOnceAndAhead() => Play(AudioClock.Automatic);
        [UnityTest] public IEnumerator AutoplayOnTheSentClockSoundsWithTheJudgment() => Play(AudioClock.Sent);

        IEnumerator Play(AudioClock clock)
        {
            saved = SettingManager.EnsureInstance().Settings.Clone();
            var settings = saved.Clone(); settings.audio.clock = clock;
            SettingManager.EnsureInstance().UseUnsaved(settings);
            Assert.That(AudioEngine.EnsureInstance().Backend, Is.EqualTo(AudioBackend.BassSimple));
            latency = AudioEngine.EnsureInstance().OutputLatency;
            song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Sound Ahead\nBPM:240\nCOURSE:Oni\nLEVEL:1\nBALLOON:3\n#START\n"
                + "1020,\n5008,\n7008,\n3G40,\n0000,\n#END");
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            SceneSwitcher.Instance.PracticeMode = false;
            SceneSwitcher.Instance.Play(song, "Oni", true);
            float deadline = Time.realtimeSinceStartup + 40;
            while (SceneSwitcher.Instance.IsInputBlocked || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            var play = Object.FindAnyObjectByType<PlayScene>();
            AudioEngine.EnsureInstance().OutputLatency = 0.1;
            bool ahead = false;
            // A Kadon sounds don and ka for its one judgment.
            int Strikes() => play.Session.Good + play.Session.Rolls + (play.Session.Resolved[play.Session.Chart.Notes.Count - 2] ? 1 : 0);
            while (!play.IsFinished)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                Assert.That(play.Lane1.DrumSounds, Is.GreaterThanOrEqualTo(Strikes()));
                ahead |= play.Lane1.DrumSounds > Strikes();
                yield return null;
            }
            Assert.That(play.Session.Good, Is.EqualTo(5));
            Assert.That(play.Session.Rolls, Is.GreaterThan(3));
            Assert.That(play.Lane1.DrumSounds, Is.EqualTo(Strikes()));
            Assert.That(ahead, Is.EqualTo(clock != AudioClock.Sent), "Sounds lead the judgment only on the heard clock.");
        }
    }
}
