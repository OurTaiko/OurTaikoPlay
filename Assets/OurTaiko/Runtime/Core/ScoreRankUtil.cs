namespace OurTaiko
{
    // Shared by song select, gameplay and results. Every rank line is a share of the chart's
    // kiwami line (TJAParser ShinuchiScoring), so a score alone has no rank. OurTaikoNet's
    // net.score_rank ranks the scores it stores the same way.
    public static class ScoreRankUtil
    {
        public const int Count = 7;
        // 白粋, 銅粋, 銀粋, 金粋, 桃雅, 紫雅: these percent of the kiwami line, without rounding.
        static readonly int[] Percents = { 50, 60, 70, 80, 90, 95 };

        public static int KiwamiThreshold(TaikoChart chart) => ShinuchiScoring.Of(chart).KiwamiThreshold;

        // Without the chart's kiwami line (an online chart still loading) there is no rank to
        // show, rather than a wrong one.
        public static ScoreRank FromScore(int score, int? kiwamiThreshold)
        {
            if (!kiwamiThreshold.HasValue) return ScoreRank.None;
            long line = kiwamiThreshold.Value;
            if (score >= line) return ScoreRank.Kiwami;
            for (int i = Percents.Length - 1; i >= 0; i--)
                if (score * 100L >= line * Percents[i]) return (ScoreRank)(i + 1);
            return ScoreRank.None;
        }
    }
}
