using System.Runtime.CompilerServices;

namespace OurTaiko
{
    // The game's source-aware import boundary. Keep only derived score totals alongside the
    // sealed parser model; the source map/text can be collected immediately after import.
    // Like ChartModifiers' metadata, these totals live only as long as the prepared chart.
    // Colour/scroll/display modifiers do not change them; structural edits require a new parse.
    public static class ScoredTja
    {
        static readonly ConditionalWeakTable<TaikoChart, TjaScoreEstimate> estimates = new ConditionalWeakTable<TaikoChart, TjaScoreEstimate>();

        public static TaikoChart Parse(string text, string course = "Oni", string side = "single")
        {
            var analysis = TjaParser.Analyze(text, course, side);
            // Analyze recovers for editors. Gameplay must keep Parse's exception types/messages.
            if (analysis.HasErrors) return TjaParser.Parse(text, course, side);
            var estimate = TjaScoreEstimate.Read(analysis);
            if (estimate != null) estimates.Add(analysis.Chart, estimate);
            return analysis.Chart;
        }

        internal static TjaScoreEstimate Estimate(TaikoChart chart)
            => estimates.TryGetValue(chart, out var estimate) ? estimate : null;
    }
}
