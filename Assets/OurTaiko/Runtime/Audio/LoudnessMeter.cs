using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // Integrated loudness (LUFS) and sample peak of decoded audio, per ITU-R BS.1770-4: K-weighting,
    // 400 ms blocks overlapping by 75 %, an absolute gate at -70 LUFS and a relative gate 10 LU
    // below the absolute-gated loudness. Feed interleaved float samples in order with Add.
    public sealed class LoudnessMeter
    {
        // Two biquads per channel: the high shelf (head effect) then the high pass (RLB curve).
        readonly double[] shelfB = new double[3], shelfA = new double[3], passB = new double[3], passA = new double[3];
        readonly double[] state;
        readonly double[] weights;
        readonly int channels, subBlockFrames;
        // Weighted sum of squares of each finished 100 ms sub-block.
        readonly List<double> subBlocks = new List<double>();
        double current;
        int currentFrames;

        public float Peak { get; private set; }

        public LoudnessMeter(int sampleRate, int channels)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            this.channels = channels;
            subBlockFrames = Math.Max(1, sampleRate / 10);
            state = new double[channels * 4];
            weights = Weights(channels);

            // The filter definitions behind BS.1770's 48 kHz coefficients, so any rate measures the same.
            double k = Math.Tan(Math.PI * 1681.974450955533 / sampleRate), q = 0.7071752369554196;
            double vh = Math.Pow(10, 3.999843853973347 / 20), vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            shelfB[0] = (vh + vb * k / q + k * k) / a0; shelfB[1] = 2 * (k * k - vh) / a0; shelfB[2] = (vh - vb * k / q + k * k) / a0;
            shelfA[1] = 2 * (k * k - 1) / a0; shelfA[2] = (1 - k / q + k * k) / a0;

            k = Math.Tan(Math.PI * 38.13547087602444 / sampleRate); q = 0.5003270373238773;
            a0 = 1 + k / q + k * k;
            passB[0] = 1; passB[1] = -2; passB[2] = 1;
            passA[1] = 2 * (k * k - 1) / a0; passA[2] = (1 - k / q + k * k) / a0;
        }

        // A mono file plays on both speakers, so it is measured as two identical channels (+3 dB);
        // 5.1 follows BS.1770 (no LFE, surrounds +1.5 dB); other layouts weigh every channel alike.
        static double[] Weights(int channels)
        {
            if (channels == 1) return new[] { 2.0 };
            if (channels == 6) return new[] { 1, 1, 1, 0, 1.41, 1.41 };
            var weights = new double[channels];
            for (int i = 0; i < channels; i++) weights[i] = 1;
            return weights;
        }

        // `count` samples of `samples` (interleaved, whole frames).
        public void Add(float[] samples, int count)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            count = Math.Min(count, samples.Length) / channels * channels;
            float peak = Peak;
            for (int i = 0; i < count; i += channels)
            {
                double frame = 0;
                for (int c = 0; c < channels; c++)
                {
                    float x = samples[i + c];
                    float magnitude = Math.Abs(x);
                    if (magnitude > peak && !float.IsNaN(magnitude)) peak = magnitude;
                    int s = c * 4;
                    // Direct form II transposed, shelf then pass.
                    double y = shelfB[0] * x + state[s];
                    state[s] = shelfB[1] * x - shelfA[1] * y + state[s + 1];
                    state[s + 1] = shelfB[2] * x - shelfA[2] * y;
                    double z = passB[0] * y + state[s + 2];
                    state[s + 2] = passB[1] * y - passA[1] * z + state[s + 3];
                    state[s + 3] = passB[2] * y - passA[2] * z;
                    frame += weights[c] * z * z;
                }
                current += frame;
                if (++currentFrames == subBlockFrames) { subBlocks.Add(current); current = 0; currentFrames = 0; }
            }
            Peak = peak;
        }

        // The gated loudness of everything added so far; NaN when it is shorter than one block or
        // silent (no block above -70 LUFS).
        public double Integrated()
        {
            int blocks = subBlocks.Count - 3;
            if (blocks <= 0) return double.NaN;
            var powers = new double[blocks];
            for (int j = 0; j < blocks; j++)
                powers[j] = (subBlocks[j] + subBlocks[j + 1] + subBlocks[j + 2] + subBlocks[j + 3]) / (4.0 * subBlockFrames);
            double absolute = Power(-70);
            double relative = Power(Loudness(Mean(powers, absolute)) - 10);
            double gated = Mean(powers, Math.Max(absolute, relative));
            return gated > 0 ? Loudness(gated) : double.NaN;
        }

        static double Loudness(double power) => power > 0 ? -0.691 + 10 * Math.Log10(power) : double.NegativeInfinity;
        static double Power(double loudness) => Math.Pow(10, (loudness + 0.691) / 10);

        // Mean of the block powers strictly above `threshold`; 0 when none are.
        static double Mean(double[] powers, double threshold)
        {
            double sum = 0; int n = 0;
            foreach (double p in powers) if (p > threshold) { sum += p; n++; }
            return n > 0 ? sum / n : 0;
        }
    }
}
