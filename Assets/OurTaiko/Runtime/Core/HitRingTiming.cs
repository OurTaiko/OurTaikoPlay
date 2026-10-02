namespace OurTaiko
{
    // Judgment::draw_outer_effect: the frame comes from Nijiiro animation 30 and the
    // opacity from animation 27 (opaque until 166.7 ms, then gone over 33.3 ms).
    public static class HitRingTiming
    {
        public const double FadeDelayMs = 166.7, FadeMs = 33.3, DurationMs = FadeDelayMs + FadeMs;
        public const int Frames = 4;
        // Animation 30 keyframes (start, end] in ms; the last frame holds once they run out.
        static readonly double[] FrameEnds = { 54.5, 72.7, 90.9 };

        public static bool IsVisible(double elapsedMs) => elapsedMs >= 0 && elapsedMs < DurationMs;

        public static int Frame(double elapsedMs)
        {
            for (int i = 0; i < FrameEnds.Length; i++)
                if (elapsedMs <= FrameEnds[i]) return i;
            return Frames - 1;
        }

        public static double Opacity(double elapsedMs)
        {
            if (elapsedMs <= FadeDelayMs) return 1;
            return System.Math.Max(0, 1 - (elapsedMs - FadeDelayMs) / FadeMs);
        }
    }
}
