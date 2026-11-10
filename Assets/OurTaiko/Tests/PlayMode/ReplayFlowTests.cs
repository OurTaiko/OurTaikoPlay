using System.Collections;
using System.Linq;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The practice scene shows a recorded play (SceneSwitcher.Replay): the record strikes the drum, and
    // opening it at a later measure starts with what the play had by then.
    public sealed class ReplayFlowTests
    {
        SongDefinition song;
        GameSettings saved;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (saved != null) SettingManager.EnsureInstance().UseUnsaved(saved);
            saved = null;
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            if (song != null) { Object.Destroy(song.chart); Object.Destroy(song); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ARecordedPlayIsShownAgainAndOpensPartWayWithItsScore()
        {
            saved = SettingManager.EnsureInstance().Settings.Clone();
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            const string tja = "TITLE:Replay\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n1212,\n1212,\n1212,\n1212,\n#END";
            song = ScriptableObject.CreateInstance<SongDefinition>(); song.name = "Replay Test";
            song.chart = new TextAsset(tja);
            // あべこべ: the record's presses are the swapped colours. The fifth note is left alone, and the
            // last is hit 40 ms late (可).
            var notes = TjaParser.Parse(tja).Notes;
            var record = new PlayRecord { Inverse = true };
            for (int i = 0; i < notes.Count; i++)
                if (i != 4) record.Inputs.Add((notes[i].Time * 1000 + (i == notes.Count - 1 ? 40 : 0), PlayRecord.TypeOf(!notes[i].IsKa, i % 2 == 1)));
            record = PlayRecord.FromJson(record.ToJson());

            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.Instance;
            switcher.PracticeMode = true;
            switcher.Play(song, "Oni", false);
            switcher.Replay = record;
            yield return Waits.Until(() => SceneManager.GetActiveScene().name == SceneSwitcher.PracticeScene && !switcher.IsInputBlocked, seconds: 40);
            var play = Object.FindAnyObjectByType<PlayScene>();
            yield return Waits.Until(() => play.Practice != null && play.IsPaused);
            yield return null;
            Assert.That(play.IsReplay, Is.True);
            Assert.That(play.Session.Chart.Notes.Select(n => n.IsKa), Is.EqualTo(notes.Select(n => !n.IsKa)), "The record's options rebuilt the chart.");
            Assert.That(play.Session.ForcedBranch, Is.Null);
            Assert.That(play.PracticeStage, Is.EqualTo(PracticeStage.Measure));
            Assert.That(play.drumPad == null || !play.drumPad.gameObject.activeSelf, Is.True, "Nobody plays a replay.");

            PlaySession ended = null;
            play.PracticeAttemptStopped += attempt => ended = attempt;
            play.ConfirmPractice(); yield return null; yield return null;
            Assert.That(play.ChoosingPracticeSpeed, Is.True);
            play.ConfirmPractice();
            yield return Waits.Until(() => !play.IsPaused);
            int sounds = play.DrumSounds;
            yield return Waits.Until(() => ended != null, seconds: 40);
            Assert.That((ended.Good, ended.Ok, ended.Bad), Is.EqualTo((14, 1, 1)));
            Assert.That(ended.MaxCombo, Is.EqualTo(11));
            Assert.That(ended.TimingOffsetMs(notes.Count - 1), Is.EqualTo(40).Within(0.01), "The offsets are the play's own.");
            Assert.That(play.DrumSounds - sounds, Is.EqualTo(15), "One drum sound a recorded press.");

            // Back at the first measure, paused. The third measure opens with the measures before it judged.
            yield return Waits.Until(() => play.IsPaused && play.PracticeStage == PracticeStage.Measure);
            yield return null;
            play.MovePractice(1); yield return null; play.MovePractice(1); yield return null;
            play.ConfirmPractice(); yield return null; yield return null;
            play.ConfirmPractice();
            yield return Waits.Until(() => !play.IsPaused);
            // Playback starts in the lead-in before that measure, with what the play had there.
            Assert.That(play.Session.Good, Is.InRange(1, 7));
            Assert.That(play.Session.Score, Is.GreaterThan(0));
            ended = null;
            yield return Waits.Until(() => ended != null, seconds: 40);
            Assert.That((ended.Good, ended.Ok, ended.Bad, ended.MaxCombo), Is.EqualTo((14, 1, 1, 11)));
        }
    }
}
