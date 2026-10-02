namespace OurTaiko
{
    // Judgment::draw_effect: the face at the judge point uses Nijiiro animation 28
    // (fade 0.5 -> 1 over 66.7 ms, hold 216.6 ms, back to 0.5 over 66.7 ms, then removed).
    public static class HitFaceTiming
    {
        public const double FadeMs = 66.7, HoldMs = 216.6, DurationMs = FadeMs + HoldMs + FadeMs;
        const double LowOpacity = 0.5;

        public static bool IsVisible(double elapsedMs) => elapsedMs >= 0 && elapsedMs < DurationMs;

        public static double Opacity(double elapsedMs)
        {
            if (elapsedMs < FadeMs) return LowOpacity + (1 - LowOpacity) * System.Math.Max(0, elapsedMs) / FadeMs;
            if (elapsedMs < FadeMs + HoldMs) return 1;
            double t = System.Math.Min(1, (elapsedMs - FadeMs - HoldMs) / FadeMs);
            return 1 + (LowOpacity - 1) * t;
        }
    }
}
