using System;

namespace OurTaiko
{
    // A play's running score. What each judgment and long-note hit is worth, and the kiwami line,
    // belong to the chart (TJAParser ShinuchiScoring); here they are only added up.
    public sealed class ShinuchiScore
    {
        public ShinuchiScoring Scoring { get; }
        public int BaseScore => Scoring.GoodPoints;
        public int CeilingScore => Scoring.KiwamiThreshold;
        public int Total { get; private set; }

        public ShinuchiScore(ShinuchiScoring scoring)
            => Scoring = scoring ?? throw new ArgumentNullException(nameof(scoring));

        public int ApplyJudgment(Judgment judgment)
        {
            int points = judgment == Judgment.Good ? Scoring.GoodPoints
                : judgment == Judgment.Ok ? Scoring.OkPoints : 0;
            Total += points;
            return points;
        }

        public int AddLongHit() { Total += ShinuchiScoring.LongHitPoints; return ShinuchiScoring.LongHitPoints; }
    }
}
