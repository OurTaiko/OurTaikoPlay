using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // Numeric gauge only; SoulGaugeView owns cells, skin art and animations.
    public sealed class SoulGauge
    {
        public const int MaximumPoints = 10000;
        public const double PointsEpsilon = 1e-6;
        public double Points { get; private set; }
        public double Value => Points / MaximumPoints;
        public int Percent => (int)Math.Floor((Points + PointsEpsilon) / MaximumPoints * 100);
        public int ClearPoints { get; }
        public double ClearThreshold => (double)ClearPoints / MaximumPoints;
        public bool IsClear => Points >= ClearPoints;
        public bool IsFull => Points >= MaximumPoints;
        public double GoodPoints { get; }
        public double OkPoints { get; }
        public double BadPoints { get; }

        public SoulGauge(int judgeableNotes, string course, int level)
            : this(judgeableNotes, SoulGaugeRules.ForChart(course, level)) { }

        SoulGauge(int judgeableNotes, SoulGaugeRules rules)
            : this(judgeableNotes, rules.SoulPercent, rules.OkMultiplier, rules.BadMultiplier, rules.ClearPoints) { }

        SoulGauge(int judgeableNotes, double soulPercent, double okMultiplier, double badMultiplier, int clearPoints)
        {
            ClearPoints = clearPoints;
            double denominator = Math.Max(1, judgeableNotes) * soulPercent;
            GoodPoints = denominator > 0 ? 1000000.0 / denominator : 0;
            OkPoints = GoodPoints * okMultiplier;
            BadPoints = GoodPoints * badMultiplier;
        }

        // YataiDON Gauge::dan: one gauge over every note of the course with no norma zone (it is
        // full or it is not). Its soul percentage is the harmonic mean of the songs' and its ok/bad
        // multipliers their average.
        public static SoulGauge Dan(IReadOnlyList<(string Course, int Level)> songs, int judgeableNotes)
        {
            if (songs == null || songs.Count == 0) throw new ArgumentException("A course has songs.", nameof(songs));
            double inverseSum = 0, ok = 0, bad = 0;
            foreach (var (course, level) in songs)
            {
                var rules = SoulGaugeRules.ForChart(course, level);
                if (rules.SoulPercent > 0) inverseSum += 1 / rules.SoulPercent;
                ok += rules.OkMultiplier / songs.Count;
                bad += rules.BadMultiplier / songs.Count;
            }
            double soul = inverseSum > 0 ? songs.Count / inverseSum : 0;
            return new SoulGauge(judgeableNotes, soul, ok, bad, MaximumPoints);
        }

        public void ApplyJudgment(Judgment judgment)
        {
            double delta;
            switch (judgment)
            {
                case Judgment.Good: delta = GoodPoints; break;
                case Judgment.Ok: delta = OkPoints; break;
                case Judgment.Bad: delta = BadPoints; break;
                default: return;
            }
            Points = Math.Max(0, Math.Min(MaximumPoints, Points + delta));
            // Gauge::apply_points_clamped snaps clear/full boundaries before state comparisons.
            if (Math.Abs(Points - MaximumPoints) < PointsEpsilon) Points = MaximumPoints;
            if (Math.Abs(Points - ClearPoints) < PointsEpsilon) Points = ClearPoints;
        }
    }
}
