using System;

namespace OurTaiko
{
    // YataiDON DanResultScreen's pages without the count-up animations: page 1 slides in each song's
    // plate (at 40, 65 and 90 frames, 22 frames each, overshooting 35 px), and a drum hit shows them
    // all; the next hit turns to page 2, the course's totals and exams, whose 合格 / 不合格 stamp
    // lands 1.2 s later (or at once on another hit); a hit once the stamp has shown for 500 ms
    // leaves. Every change locks the drum for 500 ms. No timer moves on by itself (user decision:
    // the simulator does not limit the player's time). Times are milliseconds of the scene's clock.
    public sealed class DanResultFlow
    {
        public const double FrameMs = 1000.0 / 60;
        public const double SlideMs = 22 * FrameMs, SlideDistance = 1600, SlideBreakAt = 5.0 / 22, SlideBreakX = 35;
        public const double InputLockMs = 500, StampDelayMs = 1200, PageFadeMs = 250;

        public enum Phase { Songs, Totals, Stamped, Left }

        public int Songs { get; }
        public Phase State { get; private set; } = Phase.Songs;
        public double StartedAt { get; }
        public double SongsShownAt { get; private set; } = double.NaN;
        public double TotalsAt { get; private set; } = double.NaN;
        public double StampAt { get; private set; } = double.NaN;
        public double ChangedAt { get; private set; }

        public DanResultFlow(int songs, double now) { Songs = songs; StartedAt = ChangedAt = now; }

        // When song `index`'s plate lands, from the start of page 1.
        public static double LandMs(int index) => (40 + 25 * index) * FrameMs;

        // A plate's offset to the right while it slides in (dan_result_draw.lua slide_offset).
        public static double SlideOffset(double t)
        {
            if (t <= 0) return SlideDistance;
            if (t >= SlideMs) return 0;
            double p = t / SlideMs;
            if (p <= SlideBreakAt) return SlideDistance + (SlideBreakX - SlideDistance) * (p / SlideBreakAt);
            return SlideBreakX * (1 - (p - SlideBreakAt) / (1 - SlideBreakAt));
        }

        // Page 1's time for plate placement: a hit there jumps to the end of the slides.
        public double SongsTime(double now) => double.IsNaN(SongsShownAt) ? now - StartedAt : Math.Max(now - StartedAt, SongsShownAt);
        double SongsEndMs => Songs > 0 ? LandMs(Songs - 1) + SlideMs : 0;

        public bool StampShown(double now) => State == Phase.Stamped || State == Phase.Left || State == Phase.Totals && now >= StampAt;

        // A drum hit; returns whether it did anything.
        public bool Hit(double now)
        {
            if (now - ChangedAt < InputLockMs) return false;
            switch (State)
            {
                case Phase.Songs:
                    ChangedAt = now;
                    if (now - StartedAt < SongsEndMs && double.IsNaN(SongsShownAt)) { SongsShownAt = SongsEndMs; return true; }
                    State = Phase.Totals; TotalsAt = now; StampAt = now + StampDelayMs;
                    return true;
                case Phase.Totals:
                    if (now >= StampAt) return false;
                    StampAt = now; ChangedAt = now;
                    return true;
                case Phase.Stamped:
                    if (now - StampAt < InputLockMs) return false;
                    State = Phase.Left;
                    return true;
                default:
                    return false;
            }
        }

        // Called every frame: the stamp lands on its own.
        public bool Update(double now)
        {
            if (State != Phase.Totals || now < StampAt) return false;
            State = Phase.Stamped;
            return true;
        }

        public bool Leave()
        {
            if (State == Phase.Left) return false;
            State = Phase.Left;
            return true;
        }

        // YataiDON odai_result as the result and the course select name it.
        public static string VerdictName(int verdict) => verdict switch
        {
            1 => "赤合格", 2 => "赤合格・フルコンボ", 3 => "赤合格・全良",
            4 => "金合格", 5 => "金合格・フルコンボ", 6 => "金合格・全良",
            _ => "不合格",
        };
    }
}
