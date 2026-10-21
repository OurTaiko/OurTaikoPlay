using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // YataiDON DanResultScreen's pages and timeline (dan_result.cpp). Page 1 slides in each song's
    // plate (at 40, 65 and 90 frames, 22 frames each, overshooting 35 px); a drum hit shows them all
    // and the next turns to page 2, which fades in over 500 ms and plays out on its own schedule
    // (build_page2_timeline): 94 frames in, the totals count up digit by digit (500 ms a place) while
    // the soul gauge fills (25 ms a percent), then each exam row lands, fills its bar and shows its
    // number, and the 合格 / 不合格 stamp lands 500 ms after the last row, its voice 89 frames later.
    // A hit on page 2 before the stamp lands everything at once with the stamp 500 ms on; once the
    // stamp has shown for 500 ms a hit leaves, or first shows the 昇段 celebration (a better pass than
    // before), which a hit after 3.5 s ends, and the 祝 popup (the highest course's first pass), which
    // leaves by itself. Every change locks the drum for 500 ms. Nothing else moves on by itself (user
    // decision: the simulator does not limit the player's time). Times are milliseconds.
    public sealed class DanResultFlow
    {
        public const double FrameMs = 1000.0 / 60, LuaFrameMs = 1000.0 / 120;
        public const double SlideMs = 22 * FrameMs, SlideDistance = 1600, SlideBreakAt = 5.0 / 22, SlideBreakX = 35;
        public const double InputLockMs = 500, PageFadeMs = 500, WaitMs = 500, WaitDetailMs = 200;
        public const double TotalSlideMs = 94 * FrameMs, RowSlideMs = 29 * FrameMs, StampAnimationMs = 89 * FrameMs;
        public const double DigitRollMs = 500, GaugeUnitMs = 3 * LuaFrameMs, GaugeNumberMs = 10 * FrameMs + 500;
        public const double RowUnitMs = 2 * LuaFrameMs, RowNumberMs = 15 * FrameMs, RowWaitMs = 60 * LuaFrameMs;
        public const double CelebrationExitMs = 3500, ShogoMs = 3000, CongratsMs = 899 * FrameMs + 180 * LuaFrameMs;

        public enum Phase { Songs, Totals, Celebrating, Congrats, Left }

        // One exam row on page 2, in ms from the page's start: when it shows, when its bar starts to
        // move and for how long, and when its number shows.
        public struct Row
        {
            public double Land, Fill, FillMs, Number;
        }

        // What page 2 has to show: the longest total (in digits), the soul gauge row's value (-1 for
        // none) and, for every other exam, how far its bar travels (0..1).
        public sealed class Page
        {
            public int Digits = 1;
            public int GaugeRow = -1, GaugeValue;
            public double[] Travel = Array.Empty<double>();
        }

        public int Songs { get; }
        public Phase State { get; private set; } = Phase.Songs;
        public double StartedAt { get; }
        public double SongsShownAt { get; private set; } = double.NaN;
        public double TotalsAt { get; private set; } = double.NaN;
        public double ChangedAt { get; private set; }
        public double CelebratedAt { get; private set; } = double.NaN;
        public double CongratsAt { get; private set; } = double.NaN;
        public bool Celebrates { get; }
        public bool Congratulates { get; }

        // Page 2's schedule, from TotalsAt; a hit before the stamp lands everything (Skipped).
        public double TotalsStart { get; private set; }
        public double TotalsEnd { get; private set; }
        public Row[] Rows { get; }
        public double StampAt { get; private set; }
        public double VoiceAt { get; private set; }
        public bool Skipped { get; private set; }
        readonly Page page;

        public DanResultFlow(int songs, double now, Page page = null, bool celebrate = false, bool congratulate = false)
        {
            Songs = songs; StartedAt = ChangedAt = now;
            this.page = page ?? new Page();
            Celebrates = celebrate;
            Congratulates = celebrate && congratulate;
            Rows = new Row[this.page.Travel.Length];
            Schedule();
        }

        // build_page2_timeline.
        void Schedule()
        {
            TotalsStart = TotalSlideMs;
            double totals = page.Digits * DigitRollMs;
            if (page.GaugeRow >= 0) totals = Math.Max(totals, page.GaugeValue * GaugeUnitMs + GaugeNumberMs);
            TotalsEnd = TotalsStart + totals;
            double t = TotalsEnd + WaitDetailMs;
            for (int i = 0; i < Rows.Length; i++)
            {
                if (i == page.GaugeRow)
                {
                    double fill = page.GaugeValue * GaugeUnitMs;
                    Rows[i] = new Row { Land = 0, Fill = TotalsStart, FillMs = fill, Number = TotalsStart + fill + GaugeNumberMs - 500 };
                    continue;
                }
                var row = new Row { Land = t, Fill = t + RowSlideMs };
                row.FillMs = Math.Max(0, Math.Round(Math.Min(1, Math.Max(0, page.Travel[i])) * 100, MidpointRounding.AwayFromZero) * RowUnitMs);
                row.Number = row.Fill + row.FillMs + RowNumberMs;
                Rows[i] = row;
                t = row.Number + RowWaitMs;
            }
            StampAt = t + WaitMs;
            VoiceAt = StampAt + StampAnimationMs;
        }

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

        // Milliseconds into page 2 (negative before it).
        public double PageTime(double now) => double.IsNaN(TotalsAt) ? -1 : now - TotalsAt;

        public bool StampShown(double now) => !double.IsNaN(TotalsAt) && PageTime(now) >= StampAt;

        // A drum hit; returns whether it did anything.
        public bool Hit(double now)
        {
            if (now - ChangedAt < InputLockMs) return false;
            switch (State)
            {
                case Phase.Songs:
                    ChangedAt = now;
                    if (now - StartedAt < SongsEndMs && double.IsNaN(SongsShownAt)) { SongsShownAt = SongsEndMs; return true; }
                    State = Phase.Totals; TotalsAt = now;
                    return true;
                case Phase.Totals:
                {
                    double on = PageTime(now);
                    if (!Skipped && on < StampAt) { Skip(on); ChangedAt = now; return true; }
                    if (on < StampAt + WaitMs) return false;
                    ChangedAt = now;
                    if (Celebrates) { State = Phase.Celebrating; CelebratedAt = now; }
                    else State = Phase.Left;
                    return true;
                }
                case Phase.Celebrating:
                    if (now - CelebratedAt < CelebrationExitMs) return false;
                    ChangedAt = now;
                    if (Congratulates) { State = Phase.Congrats; CongratsAt = now; }
                    else State = Phase.Left;
                    return true;
                default:
                    return false;
            }
        }

        // The page-2 hit before the stamp: every total, bar and number lands now, the stamp 500 ms on.
        void Skip(double on)
        {
            Skipped = true;
            TotalsStart = Math.Min(TotalsStart, on);
            TotalsEnd = on;
            for (int i = 0; i < Rows.Length; i++)
            {
                Rows[i].Land = Math.Min(Rows[i].Land, on);
                Rows[i].Fill = Math.Min(Rows[i].Fill, on);
                Rows[i].FillMs = 0;
                Rows[i].Number = on;
            }
            StampAt = on + WaitMs;
            VoiceAt = StampAt + StampAnimationMs;
        }

        // Called every frame: the 祝 popup leaves by itself. Returns whether the screen should leave.
        public bool Update(double now)
        {
            if (State != Phase.Congrats || now - CongratsAt < CongratsMs) return false;
            State = Phase.Left;
            return true;
        }

        public bool Leave()
        {
            if (State == Phase.Left) return false;
            State = Phase.Left;
            return true;
        }

        // Page 2's bar for one exam row: the share of its travel shown at `on` (1 once it has filled).
        public double FillShare(int row, double on)
        {
            var r = Rows[row];
            if (Skipped || r.FillMs <= 0) return 1;
            return Math.Min(1, Math.Max(0, (on - r.Fill) / r.FillMs));
        }

        // The soul gauge row's value at `on`, and whether it has finished filling.
        public int GaugeAt(double on, out bool landed)
        {
            landed = Skipped || on >= TotalsStart + page.GaugeValue * GaugeUnitMs;
            if (landed) return page.GaugeValue;
            return (int)Math.Min(page.GaugeValue, Math.Max(0, (on - TotalsStart) / GaugeUnitMs));
        }

        // draw_digit_counter's roll: how long the totals have been counting (-1 once they have landed).
        public double RollTime(double on) => Skipped || on >= TotalsEnd ? -1 : on - TotalsStart;

        // One digit of a rolling total (`place` 0 = ones): -1 hidden, else the digit to show.
        public static int RollDigit(int digit, int place, double roll)
        {
            if (roll < 0) return digit;
            if (roll < place * DigitRollMs) return -1;
            if (roll < (place + 1) * DigitRollMs) return (int)(Math.Floor(roll / 83.0 + place) % 10);
            return digit;
        }

        // How far a bar travels on page 2 (dan_result.cpp: a less exam drains from full).
        public static double Travel(DanExam exam, double progress, IReadOnlyList<double> songs = null)
        {
            double Of(double p) => exam.Range == DanExamRange.Less ? 1 - p : p;
            if (songs == null) return Of(progress);
            double travel = 0;
            foreach (double p in songs) travel = Math.Max(travel, Of(p));
            return travel;
        }

        // The bar shown while it fills: a less exam drains from full, a more exam grows from empty.
        public static double FillProgress(DanExam exam, double progress, double share)
            => exam.Range == DanExamRange.Less ? 1 - (1 - progress) * share : progress * share;

        // A filling bar's colour by how far it has got (dan_result_draw.lua; "up_100" is the plain full bar).
        public static string FillState(double progress)
        {
            int g = (int)Math.Floor(progress * 100);
            return g <= 0 ? "empty" : g <= 49 ? "up_50" : g <= 99 ? "up_80" : "up_100";
        }

        // dan_result_draw.lua draw_best_score: the pill's drop (y offset, alpha) by frames since the
        // totals started counting (5..52).
        static readonly double[,] BestScoreKeys =
        {
            { 5, 24, 0 }, { 6, 7.45, 0.36 }, { 7, -5.4, 0.64 }, { 8, -14.6, 0.84 }, { 9, -20.1, 0.96 }, { 10, -21.95, 1 },
            { 11, -20.55, 1 }, { 12, -16.4, 1 }, { 13, -9.5, 1 }, { 14, 0, 1 }, { 18, 7, 1 }, { 19, 4.5, 1 }, { 20, 2.5, 1 },
            { 21, 1.1, 1 }, { 22, 0.3, 1 }, { 23, 0, 1 }, { 52, 0, 1 },
        };

        public static (double Y, double Alpha) BestScorePill(double f)
        {
            for (int i = 0; i < BestScoreKeys.GetLength(0) - 1; i++)
            {
                if (f < BestScoreKeys[i, 0] || f > BestScoreKeys[i + 1, 0]) continue;
                double u = (f - BestScoreKeys[i, 0]) / (BestScoreKeys[i + 1, 0] - BestScoreKeys[i, 0]);
                return (BestScoreKeys[i, 1] + (BestScoreKeys[i + 1, 1] - BestScoreKeys[i, 1]) * u,
                        BestScoreKeys[i, 2] + (BestScoreKeys[i + 1, 2] - BestScoreKeys[i, 2]) * u);
            }
            return (0, 1);
        }

        // The 昇段 text: the course's rank as two glyphs (shoudan text frames), or none for a course
        // with no rank (only 合格 shows). Frames 18 and 19 are 合 and 格.
        public static (int Left, int Right)? RankGlyphs(int danIndex)
        {
            if (danIndex == 0) return (10, 11);                           // 初級
            if (danIndex >= 1 && danIndex <= 10) return (10 - danIndex, 11); // 十級..一級
            if (danIndex == 11) return (10, 12);                          // 初段
            if (danIndex >= 12 && danIndex <= 20) return (danIndex - 11, 12); // 二段..十段
            if (danIndex >= 21 && danIndex <= 24) return (danIndex - 8, 17);  // 玄人..達人
            return null;
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
