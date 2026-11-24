namespace OurTaiko
{
    // The 段位道場's words in the player's language (the Dan table), for the logic in Core that
    // names no text of its own.
    public static class DanText
    {
        // The 段位 select's pages: ローカル, and the 外伝 entry that closes each server's page
        // (DanSelectPages.Build).
        public static string LocalPageTitle => L10n.Get("Dan", "select.pageLocal");
        public static string GaidenTitle => L10n.Get("Dan", "select.gaiden");

        // A 段位's name (0 初級 .. 24 達人, DanResultFlow.RankKey); "" outside that range.
        public static string RankName(int danIndex) => DanResultFlow.RankKey(danIndex) is string key ? L10n.Get("Dan", key) : "";

        // A border as the screens write it (skin_config dan_exam_text_*): "100 ％以上", "5 未満".
        public static string Caption(this DanExam exam, int red)
        {
            bool less = exam.Range == DanExamRange.Less;
            string key = exam.Type == DanExamType.Gauge ? (less ? "exam.gaugeLess" : "exam.gaugeMore") : (less ? "exam.less" : "exam.more");
            return L10n.Format("Dan", key, red);
        }
    }
}
