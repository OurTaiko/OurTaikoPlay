using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class PracticeTests
    {
        [Test]
        public void ClockRateChangesChartSecondsAndScheduledAudioTogether()
        {
            var clock = new SongClock();
            clock.Seek(10, 0.8); clock.Resume(100);
            Assert.That(clock.Schedule(100, 20).Value.Position, Is.EqualTo(10.04).Within(1e-8));
            clock.Update(105);
            Assert.That(clock.Time, Is.EqualTo(14).Within(1e-8));
            clock.Pause(); clock.Update(200);
            Assert.That(clock.Time, Is.EqualTo(14).Within(1e-8));
            clock.Seek(-1, 0.5); clock.Resume(210);
            Assert.That(clock.Schedule(210, 20).Value.At, Is.EqualTo(212).Within(1e-8));
            clock.Update(213);
            Assert.That(clock.Time, Is.EqualTo(0.5).Within(1e-8));
        }
        [Test]
        public void CursorUsesRealBarsAndAnimatesBothDirections()
        {
            var chart = TjaParser.Parse("BPM:120\nOFFSET:0.5\nCOURSE:Oni\n#START\n1000,\n#BARLINEOFF\n#MEASURE 3/4\n#BPMCHANGE 180\n#DELAY 0.25\n100,\n100,\n#END");
            var progress = new PracticeProgress();
            progress.SetBars(chart.Bars.Select(b => b.Time));
            Assert.That(progress.Count, Is.EqualTo(3));
            progress.PauseAt(-0.5, 0); progress.Move(1, 0);
            Assert.That(progress.Target, Is.EqualTo(1.75).Within(1e-8));
            progress.Update(0.1);
            Assert.That(progress.Position, Is.EqualTo(0.625).Within(1e-8));
            progress.Move(-1, 0.1); progress.Update(0.3);
            Assert.That(progress.Position, Is.EqualTo(-0.5).Within(1e-8));
            progress.ChangeSpeed(-1); progress.ChangeSpeed(-1);
            Assert.That(progress.Speed, Is.EqualTo(0.8));
            for (int i = 0; i < 100; i++) progress.ChangeSpeed(-1);
            Assert.That(progress.Speed, Is.EqualTo(0.1));
            for (int i = 0; i < 100; i++) progress.ChangeSpeed(1);
            Assert.That(progress.Speed, Is.EqualTo(3));
        }
        [Test]
        public void SeekingDoesNotJudgeSkippedNotesAndRewindRestoresThem()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1111,\n1111,\n#END");
            var session = PlaySession.PracticeAt(chart, 2);
            session.Advance(2, false);
            Assert.That(session.Bad, Is.Zero);
            Assert.That(session.Score, Is.Zero);
            Assert.That(session.Hit(false, 2), Is.EqualTo(Judgment.Good));
            var rewind = PlaySession.PracticeAt(chart, 0, session);
            Assert.That(rewind.Good, Is.Zero);
            Assert.That(rewind.Hit(false, 0), Is.EqualTo(Judgment.Good));
        }
        [Test]
        public void SeekIntoLongNoteDoesNotAutoplayAllEarlierRollHits()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n5000,\n0000,\n0008,\n#END");
            var session = PlaySession.PracticeAt(chart, 4);
            session.Advance(4, true);
            Assert.That(session.Rolls, Is.EqualTo(1));
            session.Advance(4.2, true);
            Assert.That(session.Rolls, Is.InRange(3, 4));
        }
        [Test]
        public void SeekKeepsPastBranchesWithoutReplayingScoresAndRewindClearsFutureChoices()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1111,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n1100,\n#M\n1111,\n#BRANCHEND\n1000,\n#END");
            var original = new PlaySession(chart); original.Advance(3, true);
            var sought = PlaySession.PracticeAt(chart, 3, original);
            Assert.That(sought.SelectedRoute(0), Is.EqualTo(original.SelectedRoute(0)));
            Assert.That(sought.Good + sought.Bad + sought.Rolls, Is.Zero);
            var rewind = PlaySession.PracticeAt(chart, -5, sought);
            Assert.That(rewind.SelectedRoute(0), Is.Null);
        }
    }
}
