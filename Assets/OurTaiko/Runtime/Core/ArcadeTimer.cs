using System;

namespace OurTaiko
{
    // Nijiiro Scripts/global/timer.lua: a whole-second countdown that only ticks while it is updated.
    // Below 10 every tick blips and pops the digits; voices at 30 / 10 / 5 s (each pre-empting the
    // earlier ones); reaching 0 stops any voice and confirms once, with nothing spoken. Entry and
    // song select show it as a placeholder that is never updated (user decision), so Update is kept
    // only as the record of the original countdown.
    public sealed class ArcadeTimer
    {
        [Flags]
        public enum Cue { None = 0, Blip = 1, Pop = 2, Voice30 = 4, Voice10 = 8, Voice5 = 16, Finished = 32 }

        public const int RedZone = 10;

        public int Seconds { get; private set; }
        public bool IsFinished { get; private set; }
        public double PoppedAtMs { get; private set; } = double.NegativeInfinity;
        double lastTickMs;

        public ArcadeTimer(int seconds, double nowMs)
        {
            Seconds = seconds;
            lastTickMs = nowMs;
        }

        public bool IsRed => Seconds < RedZone;

        // Timer:update. Like the original, a timer that was not updated for a while ticks once on
        // its next update and then restarts its one-second count from there.
        public Cue Update(double nowMs)
        {
            if (Seconds == 0 && !IsFinished)
            {
                IsFinished = true;
                return Cue.Finished;
            }
            if (nowMs < lastTickMs + 1000 || Seconds <= 0) return Cue.None;
            Seconds--;
            lastTickMs = nowMs;
            var cue = Cue.None;
            if (Seconds < RedZone)
            {
                cue |= Cue.Blip | Cue.Pop;
                PoppedAtMs = nowMs;
            }
            if (Seconds == 30) cue |= Cue.Voice30;
            else if (Seconds == 10) cue |= Cue.Voice10;
            else if (Seconds == 5) cue |= Cue.Voice5;
            return cue;
        }

        // global animation 9: texture_resize 1 -> 1.5 over 333 ms, quadratic ease-out, then back.
        public static float DigitScale(double msSincePop)
        {
            const double half = 333;
            if (msSincePop < 0 || msSincePop >= 2 * half) return 1;
            if (msSincePop < half) return (float)(1 + 0.5 * EaseOut(msSincePop / half));
            return (float)(1.5 - 0.5 * EaseOut((msSincePop - half) / half));
        }

        // global animations 10 / 11: the highlight grows 1 -> 1.5 and fades 1 -> 0, both over 200 ms.
        public static float HighlightScale(double msSincePop) => (float)(1 + 0.5 * Clamp01(msSincePop / 200));
        public static float HighlightAlpha(double msSincePop) => (float)(1 - Clamp01(msSincePop / 200));

        static double EaseOut(double t) { t = Clamp01(t); return 1 - (1 - t) * (1 - t); }
        static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
    }
}
