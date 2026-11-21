using System;

namespace OurTaiko
{
    // What a song's audio measured when it was decoded (NativeAudioSample, LoudnessMeter), and the
    // playback gain that follows. The file itself is never changed: the gain is applied as the
    // stream's volume, under the player's volume settings.
    public readonly struct AudioLevel
    {
        // Where loudness balancing brings every song: about the middle of what Fanmade songs measure
        // (a 2026-11-20 sample of 25 spanned -16.2 to -4.4 LUFS, median -8.6), so the music sits close
        // to where it did beside the drum and voice sounds, which are not balanced.
        public const double TargetLufs = -10;
        // Bump when the measurement changes, so stored levels (Online.AudioLevelStore) are measured again.
        public const int MeterVersion = 1;

        public static readonly AudioLevel Unmeasured = new AudioLevel(1, double.NaN);

        // Sample peak, 0 to 1 (the decoder clips above full scale).
        public readonly float Peak;
        // Integrated loudness in LUFS; NaN when it could not be measured (silent or under 400 ms).
        public readonly double Loudness;

        public AudioLevel(float peak, double loudness)
        {
            Peak = float.IsNaN(peak) ? 1 : Math.Clamp(peak, 0, 1);
            Loudness = double.IsInfinity(loudness) ? double.NaN : loudness;
        }

        // Without balancing, the loudest sample is brought to full scale (the behaviour before
        // balancing existed). Balancing aims the loudness at TargetLufs but never lifts the peak
        // past full scale, so a quiet, dynamic song stops short of the target rather than clip.
        public float Gain(bool balance)
        {
            float peakGain = Peak > 0 ? 1 / Peak : 1;
            if (!balance || double.IsNaN(Loudness)) return peakGain;
            return (float)Math.Min(peakGain, Math.Pow(10, (TargetLufs - Loudness) / 20));
        }
    }
}
