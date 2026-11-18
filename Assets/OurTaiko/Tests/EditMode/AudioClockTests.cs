using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // AudioOptions.clock chooses what the song clock reads; ClockTiming is how each output carries it out.
    public sealed class AudioClockTests
    {
        const double L = 0.026;

        static ClockTiming Timing(AudioClock clock, AudioBackend backend)
            => new ClockTiming(new AudioOptions { clock = clock }.HeardClock(backend), backend, L);

        static (double, double, double) Parts(ClockTiming t) => (t.SoundLead, t.PositionLead, t.StartDelay);

        [Test] public void AutomaticAndTheMatchingChoiceKeepEachBackendAsItWas()
        {
            // BassSimple: the clock is pulled to the stream position as it is; autoplay sounds lead by the latency.
            Assert.That(Parts(Timing(AudioClock.Automatic, AudioBackend.BassSimple)), Is.EqualTo((L, 0.0, 0.0)));
            Assert.That(Parts(Timing(AudioClock.Heard, AudioBackend.BassSimple)), Is.EqualTo((L, 0.0, 0.0)));
            // BassWASAPI, BassASIO: a stopwatch from Play(), nothing moved.
            foreach (var backend in new[] { AudioBackend.BassWASAPI, AudioBackend.BassASIO })
            {
                Assert.That(Parts(Timing(AudioClock.Automatic, backend)), Is.EqualTo((0.0, 0.0, 0.0)));
                Assert.That(Parts(Timing(AudioClock.Sent, backend)), Is.EqualTo((0.0, 0.0, 0.0)));
            }
        }

        [Test] public void TheOtherChoiceMovesTheClockByTheLatency()
        {
            Assert.That(Parts(Timing(AudioClock.Sent, AudioBackend.BassSimple)), Is.EqualTo((0.0, L, 0.0)));
            foreach (var backend in new[] { AudioBackend.BassWASAPI, AudioBackend.BassASIO })
                Assert.That(Parts(Timing(AudioClock.Heard, backend)), Is.EqualTo((L, 0.0, L)));
            Assert.That(Parts(new ClockTiming(true, AudioBackend.BassWASAPI, -1)), Is.EqualTo((0.0, 0.0, 0.0)));
        }

        [Test] public void TheSettingIsSavedAndAnUnknownValueIsAutomatic()
        {
            Assert.That(new GameSettings().audio.clock, Is.EqualTo(AudioClock.Automatic));
            Assert.That(GameSettings.FromJson("{\"audio\":{\"clock\":9}}").audio.clock, Is.EqualTo(AudioClock.Automatic));
            var settings = new GameSettings();
            var row = SettingsCatalog.Sound(SoundPlatform.Windows, AudioBackend.BassWASAPI).SelectMany(g => g.Rows).Single(r => r.Id == "clock");
            Assert.That(row.Options, Is.EqualTo(new[] { L10n.Get("Settings", "sound.automatic"), L10n.Get("Settings", "sound.clock.heard"), L10n.Get("Settings", "sound.clock.sent") }));
            Assert.That(row.OptionNotes[0], Is.EqualTo(L10n.Format("Settings", "sound.clockNote.autoSent", "BassWASAPI")));
            Assert.That(row.AppliesOnClose, Is.False);
            row.Set(settings, 1);
            Assert.That(settings.Clone().audio.clock, Is.EqualTo(AudioClock.Heard));
            // The device is not reopened for it.
            Assert.That(new AudioOptions().SameDeviceSettings(settings.audio), Is.True);
            Assert.That(SettingsCatalog.Sound(SoundPlatform.Desktop, AudioBackend.Automatic).SelectMany(g => g.Rows).Single(r => r.Id == "clock").OptionNotes[0],
                Is.EqualTo(L10n.Get("Settings", "sound.clockNote.autoHeard")));
        }

        [Test] public void AHeardClockStartsBehindTheTrackByTheDelay()
        {
            var sent = new SongClock(); var heard = new SongClock();
            foreach (var clock in new[] { sent, heard }) { clock.Start(8, 2); clock.Update(10.01); }
            sent.TrackStarted(10.02);
            heard.TrackStarted(10.02, L);
            sent.Update(10.5); heard.Update(10.5);
            Assert.That(sent.Time, Is.EqualTo(0.48).Within(1e-9));
            Assert.That(heard.Time, Is.EqualTo(0.48 - L).Within(1e-9));
            // Practice speed: the delay is real time.
            var half = new SongClock();
            half.Seek(4, 0.5); half.Resume(20);
            half.TrackStarted(20, L);
            half.Update(21);
            Assert.That(half.Time, Is.EqualTo(4 + (1 - L) * 0.5).Within(1e-9));
        }
    }
}
