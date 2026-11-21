using System;
using System.IO;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // Songs play at a common loudness (BS.1770 integrated LUFS) unless the player turns it off;
    // the files are never changed, only the gain they play at.
    public sealed class LoudnessTests
    {
        // A 997 Hz sine on every channel, `seconds` long, at `amplitude` (1 = full scale).
        static float[] Sine(int rate, int channels, double seconds, float amplitude, double startSeconds = 0)
        {
            int frames = (int)(rate * seconds), start = (int)(rate * startSeconds);
            var samples = new float[(start + frames) * channels];
            for (int i = 0; i < frames; i++)
            {
                float x = amplitude * (float)Math.Sin(2 * Math.PI * 997 * i / rate);
                for (int c = 0; c < channels; c++) samples[(start + i) * channels + c] = x;
            }
            return samples;
        }

        static double Measure(float[] samples, int rate, int channels, int chunk = 4096)
        {
            var meter = new LoudnessMeter(rate, channels);
            var buffer = new float[chunk / channels * channels];
            for (int at = 0; at < samples.Length; at += buffer.Length)
            {
                int count = Math.Min(buffer.Length, samples.Length - at);
                Array.Copy(samples, at, buffer, 0, count);
                meter.Add(buffer, count);
            }
            return meter.Integrated();
        }

        // BS.1770's reference: a 0 dBFS 1 kHz sine on one channel reads -3.01 LKFS, so on both
        // channels a -20 dBFS sine reads -20.
        [TestCase(48000)]
        [TestCase(44100)]
        [TestCase(32000)]
        public void StereoSineReadsItsReferenceLoudnessAtAnyRate(int rate)
            => Assert.That(Measure(Sine(rate, 2, 5, 0.1f), rate, 2), Is.EqualTo(-20.0).Within(0.1));

        [Test]
        public void MonoMeasuresAsBothSpeakersPlayingIt()
            => Assert.That(Measure(Sine(48000, 1, 5, 0.1f), 48000, 1), Is.EqualTo(-20.0).Within(0.1));

        [Test]
        public void ChunkSizeDoesNotChangeTheResult()
        {
            var samples = Sine(44100, 2, 3, 0.3f);
            Assert.That(Measure(samples, 44100, 2, 2), Is.EqualTo(Measure(samples, 44100, 2, 16384)).Within(1e-9));
        }

        // Ungated, 20 s of silence before 4 s of music would read about 8 LU lower; the gates keep
        // it to the blocks that straddle the start (a fraction of a LU).
        [Test]
        public void SilenceBeforeTheMusicIsGatedOut()
        {
            double music = Measure(Sine(48000, 2, 4, 0.1f), 48000, 2);
            double withSilence = Measure(Sine(48000, 2, 4, 0.1f, startSeconds: 20), 48000, 2);
            Assert.That(withSilence, Is.EqualTo(music).Within(0.3));
        }

        [Test]
        public void SilentOrShortAudioHasNoLoudness()
        {
            Assert.That(Measure(new float[48000 * 2 * 3], 48000, 2), Is.NaN);
            Assert.That(Measure(Sine(48000, 2, 0.3, 0.5f), 48000, 2), Is.NaN, "Shorter than one 400 ms block");
        }

        [Test]
        public void PeakIsTheLargestSampleMagnitude()
        {
            var meter = new LoudnessMeter(44100, 2);
            var samples = Sine(44100, 2, 1, 0.25f);
            samples[100] = -0.75f;
            meter.Add(samples, samples.Length);
            Assert.That(meter.Peak, Is.EqualTo(0.75f));
        }

        [Test]
        public void WithoutBalancingTheLoudestSampleReachesFullScale()
        {
            Assert.That(new AudioLevel(0.5f, -6).Gain(false), Is.EqualTo(2f));
            Assert.That(AudioLevel.Unmeasured.Gain(true), Is.EqualTo(1f), "Sound effects and menu music are never measured");
            Assert.That(new AudioLevel(0, double.NaN).Gain(true), Is.EqualTo(1f), "A silent file stays as it is");
        }

        [Test]
        public void BalancingAimsAtTheTargetButNeverLiftsThePeakPastFullScale()
        {
            // A loud master is turned down to the target.
            Assert.That(new AudioLevel(1, -4).Gain(true), Is.EqualTo((float)Math.Pow(10, (AudioLevel.TargetLufs + 4) / 20)).Within(1e-6));
            // A quiet master with headroom comes up to the target.
            Assert.That(new AudioLevel(0.25f, -14).Gain(true), Is.EqualTo((float)Math.Pow(10, (AudioLevel.TargetLufs + 14) / 20)).Within(1e-6));
            // A quiet master already peaking at 0.8 stops at full scale.
            Assert.That(new AudioLevel(0.8f, -16).Gain(true), Is.EqualTo(1.25f).Within(1e-6));
            // Unmeasurable loudness falls back to the peak.
            Assert.That(new AudioLevel(0.5f, double.NaN).Gain(true), Is.EqualTo(2f));
        }

        [Test]
        public void BalancingIsOnForNewAndOlderSettingsFiles()
        {
            Assert.That(new GameSettings().audio.balanceLoudness, Is.True);
            Assert.That(GameSettings.FromJson("{}").audio.balanceLoudness, Is.True);
            Assert.That(GameSettings.FromJson("{\"audio\":{\"sampleRate\":48000}}").audio.balanceLoudness, Is.True);
            var off = new GameSettings(); off.audio.balanceLoudness = false;
            Assert.That(GameSettings.FromJson(UnityEngine.JsonUtility.ToJson(off)).audio.balanceLoudness, Is.False);
        }

        [Test]
        public void StoredLevelsRoundTripByAudioHash()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ourtaiko-levels-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var store = new AudioLevelStore(Path.Combine(dir, "scores.sqlite3"));
                string hash = new string('a', 64), silent = new string('b', 64);
                Assert.That(store.TryGet(hash, out _), Is.False);
                store.Save(hash, new AudioLevel(0.5f, -12.25));
                store.Save(silent, new AudioLevel(0, double.NaN));
                store.Save("not-a-hash", new AudioLevel(1, -8));
                Assert.That(store.TryGet(hash, out var level), Is.True);
                Assert.That(level.Peak, Is.EqualTo(0.5f));
                Assert.That(level.Loudness, Is.EqualTo(-12.25));
                Assert.That(store.TryGet(silent, out var none), Is.True);
                Assert.That(none.Loudness, Is.NaN);
                Assert.That(store.TryGet("not-a-hash", out _), Is.False);
                // A second store on the same database (the next launch) reads what the first saved.
                Assert.That(new AudioLevelStore(Path.Combine(dir, "scores.sqlite3")).TryGet(hash, out var reopened), Is.True);
                Assert.That(reopened.Loudness, Is.EqualTo(-12.25));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
