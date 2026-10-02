using System;

namespace OurTaiko
{
    // Nijiiro Scripts/global/nameplate.lua: the 408x96 plate canvas, its text boxes and the rainbow band.
    // Coordinates are canvas pixels from the top-left; the single local player always has the 1P badge.
    public static class NameplateLayout
    {
        public const float Width = 408, Height = 96;
        // Every text_name rect is 190 wide, text_title 215; OutlinedText spacing 2.
        public const float NameBoxWidth = 190, TitleBoxWidth = 215, TextSpacing = 2;
        public const float TitleX = 226.5f, TitleY = 34.5f, TitleFontSize = 22;
        // White fill, black border 3 (OutlinedText thickness 2 x screen_scale 1.5).
        public const float NameOutline = 3;
        public const float BadgeX = 36, BadgeY = 4, DanBackgroundX = 44, DanBackgroundY = 44, DanX = 106, DanY = 44;

        public readonly struct TextBox
        {
            public readonly float X, Y, FontSize;
            public TextBox(float x, float y, float fontSize) { X = x; Y = y; FontSize = fontSize; }
        }

        // Nameplate:name_box for a plate with a badge: 1p_coin, 1p / 1p_shougou or *_dani.
        public static TextBox NameBox(PlayerInfo info)
        {
            if (info.IsCoin) return new TextBox(226, 53, 30);
            return info.HasDan ? new TextBox(261, 67.5f, 24) : new TextBox(226, 67.5f, 24);
        }

        // Global animation 12 (texture_change): frame k for (50k, 50k+50] ms, looping every 300 ms.
        // nameplate.lua never starts it, so the original stays on frame 0; the plate is meant to cycle.
        public const double RainbowFrameMs = 50, RainbowLoopMs = 300;
        public const int RainbowFrames = 6;

        public static int RainbowFrame(double elapsedMs)
        {
            if (elapsedMs <= 0) return 0;
            double t = elapsedMs % RainbowLoopMs;
            if (t <= 0) t = RainbowLoopMs;
            return Math.Min(RainbowFrames - 1, (int)Math.Ceiling(t / RainbowFrameMs) - 1);
        }
    }

    // ScoreCounter::draw with Nijiiro's score_counter_pos (255, 277.5), score_number y -272 and the
    // inherited score_counter_margin 20 x 1.5: digits right-aligned at x 255, 30 apart, no zero padding.
    public static class ScoreCounterLayout
    {
        public const float RightX = 255, Pitch = 30, DigitTop = 277.5f - 272, DigitWidth = 56, DigitHeight = 64;
        public const float CoverX = 0, CoverY = 12;

        public static string Text(int score) => Math.Max(score, 0).ToString();
        public static float DigitLeft(int index, int count) => RightX - count * Pitch + index * Pitch;
    }

    // TextStretchAnimation (score counter id 4, balloon counter id 6): a 50 ms rise to 12 px, then a
    // stepped 116 ms return; digits grow upwards by this many pixels.
    public static class TextStretch
    {
        public const double RiseMs = 50, ReturnMs = 116;

        public static float Pixels(double elapsedMs)
        {
            if (elapsedMs < 0) return 0;
            if (elapsedMs <= RiseMs) return 2 + 5 * ((int)elapsedMs / 25f);
            if (elapsedMs <= RiseMs + ReturnMs) return 12 - 2 * ((int)((elapsedMs - RiseMs) / 16.57) + 1);
            return 0;
        }
    }
}
