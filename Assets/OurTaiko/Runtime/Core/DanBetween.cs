using System;

namespace OurTaiko
{
    // The doors before each song of a 段位 course (YataiDON 250834a dan_between.h with Nijiiro's
    // dani_enso_between clip): from frame 2 they close over the lane, the song's title shows, the
    // song may start at 201 as they part again, and all is over at 366. The clip (anim/dani_between)
    // numbers the same frames three later (start 5, open 204, end 369). Times are milliseconds since
    // the doors started; frames are 60 fps.
    public static class DanBetween
    {
        public const double Fps = 60, Start = 2, Open = 201, End = 366, ClipLead = 3;
        public const double OpenMs = (Open - Start) * 1000 / Fps;
        // game_dan.cpp start_delay: the song (music and chart) begins 4 s after the doors part; its
        // title shows on the lane under them until then.
        public const double SongDelayMs = 4000;
        public const double TotalMs = (End - Start) * 1000 / Fps;

        public static double Frame(double ms) => Math.Min(End, Start + Math.Max(0, ms) * Fps / 1000);
        public static bool IsShown(double ms) => ms >= 0 && ms < TotalMs;

        // The right door's offset from its rest (1071 = off the lane, about 434 = shut); the left
        // one mirrors it. Without the clip, dan_between.h's old linear fallback.
        public static double DoorX(LumenClip clip, double frame)
        {
            double f = frame + ClipLead;
            if (clip != null && !clip.IsEmpty) return clip.Get("fusuma_r", f, "tx", 1071);
            if (f <= 5) return 1071;
            if (f < 67) return 1071 + (360 - 1071) * (f - 5) / 62;
            if (f <= 204) return 360;
            if (f < 219) return 360 + (1071 - 360) * (f - 204) / 15;
            return 1071;
        }
    }
}
