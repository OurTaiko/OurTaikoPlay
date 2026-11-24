using System;
using System.Numerics;

namespace OurTaiko
{
    // Pure scoring policy. Timing, combos, GOGO, note size and rendering belong to other modules.
    public sealed class ShinuchiScore
    {
        public const int LongHitPoints = 100;
        public int BaseScore { get; }
        public int CeilingScore { get; }
        public int Total { get; private set; }

        public ShinuchiScore(ChartStatistics statistics)
        {
            if (statistics == null) throw new ArgumentNullException(nameof(statistics));
            TjaFraction rate = statistics.Difficulty switch
            {
                Difficulty.Easy => 6,
                Difficulty.Normal => new TjaFraction(15, 2),
                Difficulty.Hard => 10,
                _ => 15,
            };
            var k = (statistics.DrumrollScoreMicroseconds * rate / 1000000).Floor;
            var hits = (9 * k + 7) / 8;
            var budget = (statistics.BalloonHitBudget + hits) * LongHitPoints;
            // No judged notes keeps the established single-hit value. An exhausted budget still
            // gives ordinary notes a positive ten-point unit; the ceiling includes the full budget.
            BaseScore = statistics.JudgeableNotes == 0 ? 1000000 : (int)BigInteger.Max(10,
                10 * new TjaFraction(1000000 - budget, (BigInteger)10 * statistics.JudgeableNotes).Ceiling);
            // Rank thresholds are int throughout the game. Saturate only at that storage boundary.
            CeilingScore = (int)BigInteger.Min(int.MaxValue, BigInteger.Max(1000000,
                (BigInteger)BaseScore * statistics.JudgeableNotes + budget));
        }

        public int ApplyJudgment(Judgment judgment)
        {
            int points = judgment == Judgment.Good ? BaseScore
                : judgment == Judgment.Ok ? BaseScore / 2 / 10 * 10 : 0;
            Total += points;
            return points;
        }

        public int AddLongHit() { Total += LongHitPoints; return LongHitPoints; }
    }
}
