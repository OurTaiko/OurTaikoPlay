namespace OurTaiko
{
    // GaugeHitEffect: the burst on the soul badge when a flying note lands. Nijiiro
    // animation.json 2 (texture change), 32 (resize) and 33 (fade out), in seconds.
    // 31 (circle fade-in) drives hit_effect_circle*, which Nijiiro ships as transparent
    // 8x8 placeholders, and 34 (rotation) is pinned to 0, so neither shows.
    public static class GaugeHitEffectTiming
    {
        public const double SecondFrame = 0.03333, ThirdFrame = 0.06666;
        public const double ResizeDelay = 0.11667, ResizeDuration = 0.266, InitialScale = 0.8, FinalScale = 1.5;
        public const double FadeDelay = 0.3, FadeDuration = 0.083;
        public const double Duration = FadeDelay + FadeDuration;

        // Burst and note centre: hit_effect (1834,-30) drawn centred, the note at
        // gauge_hit_effect_note (1738,-126) plus half a note. Lane-local, Y down.
        public const double CentreX = 1834, CentreY = -30, BurstSize = 232;

        public static int Frame(double t) => t <= SecondFrame ? 0 : t <= ThirdFrame ? 1 : 2;

        public static double Scale(double t) =>
            t <= ResizeDelay ? InitialScale
            : t >= ResizeDelay + ResizeDuration ? FinalScale
            : InitialScale + (FinalScale - InitialScale) * (t - ResizeDelay) / ResizeDuration;

        // Shared by the burst and the note drawn under it.
        public static double Opacity(double t) =>
            t <= FadeDelay ? 1 : t >= Duration ? 0 : 1 - (t - FadeDelay) / FadeDuration;

        // Erased on the update the fade-out finishes, so it is never drawn at zero.
        public static bool IsFinished(double t) => t >= Duration;

        // GaugeHitEffect::update colours the burst by its size: raylib YELLOW, ORANGE, RED (1P).
        public static (byte r, byte g, byte b) Tint(double scale) =>
            scale <= 0.70 ? ((byte)255, (byte)255, (byte)255)
            : scale <= 0.80 ? ((byte)253, (byte)249, (byte)0)
            : scale <= 0.90 ? ((byte)255, (byte)161, (byte)0)
            : ((byte)230, (byte)41, (byte)55);
    }
}
