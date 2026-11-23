using System;
using System.Globalization;

namespace OurTaiko
{
    // Empirical Nijiiro score budget, reconstructed from authored slots instead of play times.
    // Unsupported charts deliberately keep ShinuchiScore's reference-player policy.
    internal sealed class TjaScoreEstimate
    {
        public readonly int BaseScore, CeilingScore, BalloonHits;
        TjaScoreEstimate(int initial, int ceiling, int balloons)
        { BaseScore = initial; CeilingScore = ceiling; BalloonHits = balloons; }

        public static TjaScoreEstimate Read(TjaAnalysis analysis)
        {
            var chart = analysis.Chart;
            var difficulty = SongInfo.DifficultyOf(chart.Course);
            if (analysis.Side != "single" || chart.Branches.Count != 0 || !difficulty.HasValue) return null;
            int notes = 0, balloons = 0;
            foreach (var note in chart.Notes)
            {
                if (note.Kind >= NoteKind.Don && note.Kind <= NoteKind.BigKa) notes++;
                else if (!note.IsDrumroll && note.Kind != NoteKind.Balloon) return null;
                if (!note.IsLong) continue;
                if (!analysis.SourceMap.TryGetTailSpan(note, out var tail) || tail.Length != 1 || analysis.Text[tail.Start] != '8') return null;
                if (note.Kind == NoteKind.Balloon)
                {
                    if (note.BalloonHits <= 0 || note.BalloonHits > int.MaxValue - balloons) return null;
                    balloons += note.BalloonHits;
                }
            }
            if (notes == 0) return null;
            if (!ScoreFraction.TryParse(chart.Bpm.ToString("R", CultureInfo.InvariantCulture), out var bpm) || !bpm.IsPositive) return null;
            ScoreFraction length = 4, position = 0, rollTime = 0, balloonTime = 0;
            ChartNote active = null;
            int measure = 0, slotsRead = 0;
            var tokens = TjaSyntax.Tokenize(analysis.Text);
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Span.Start < analysis.StartSpan.End) continue;
                if (token.Span.Start >= analysis.EndSpan.Start) break;
                if (token.Kind == TjaSyntaxKind.Command)
                {
                    string command = analysis.Text.Substring(token.Span.Start, token.Span.Length).ToUpperInvariant();
                    if (command != "#BPMCHANGE" && command != "#MEASURE" && command != "#DELAY") continue;
                    // Syntax keeps comments/layout separate. Join the argument fragments on this line.
                    string argument = "";
                    for (int j = i + 1; j < tokens.Count && tokens[j].Kind != TjaSyntaxKind.NewLine; j++)
                        if (tokens[j].Kind == TjaSyntaxKind.Argument)
                            argument += analysis.Text.Substring(tokens[j].Span.Start, tokens[j].Span.Length);
                    if (command == "#MEASURE")
                    {
                        // Changing the meter inside a measure is outside the fitted formula.
                        var parts = argument.Split('/');
                        if (slotsRead != 0 || parts.Length != 2 || !ScoreFraction.TryParse(parts[0], out var a)
                            || !ScoreFraction.TryParse(parts[1], out var b) || !a.IsPositive || !b.IsPositive) return null;
                        length = 4 * a / b;
                    }
                    else
                    {
                        if (!ScoreFraction.TryParse(argument, out var number)) return null;
                        if (command == "#BPMCHANGE") { if (!number.IsPositive) return null; bpm = number; }
                        else
                        {
                            if (number.IsNegative) return null;
                            AddDuration(number * 1000000, number);
                        }
                    }
                    continue;
                }
                if (token.Kind == TjaSyntaxKind.Comma)
                {
                    if (measure >= analysis.Measures.Count || slotsRead != analysis.Measures[measure].Slots) return null;
                    if (slotsRead == 0) Step(length);
                    measure++; slotsRead = 0; position = 0;
                    continue;
                }
                bool slot = token.Kind == TjaSyntaxKind.Note || token.Kind == TjaSyntaxKind.Blank || token.Kind == TjaSyntaxKind.RollEnd;
                if (token.Kind == TjaSyntaxKind.Unknown) return null;
                if (!slot) continue;
                if (measure >= analysis.Measures.Count || analysis.Measures[measure].Slots == 0) return null;
                var step = length / analysis.Measures[measure].Slots;
                var at = analysis.SourceMap.NoteAt(token.Span.Start, out bool isTail);
                if (isTail)
                {
                    if (active != at) return null;
                    if (active.IsDrumroll && !(4 * position).IsInteger)
                    {
                        ScoreFraction cap = new ScoreFraction(1, 12);
                        rollTime += (step < cap ? step : cap) * BeatMicroseconds();
                    }
                    if (active.Kind == NoteKind.Balloon && balloonTime * 60 < active.BalloonHits) return null;
                    active = null;
                }
                else if (at != null && at.IsLong)
                {
                    active = at;
                    balloonTime = 0;
                }
                Step(step);
                position += step;
                slotsRead++;
            }
            if (active != null) return null;
            // The parser also flushes a final measure at #END without a comma.
            if (slotsRead != 0 && (measure != analysis.Measures.Count - 1 || slotsRead != analysis.Measures[measure].Slots)) return null;
            ScoreFraction coefficient = difficulty.Value == Difficulty.Easy ? 6
                : difficulty.Value == Difficulty.Normal ? new ScoreFraction(15, 2)
                : difficulty.Value == Difficulty.Hard ? 10 : 15;
            var k = (coefficient * rollTime / 1000000).Floor;
            var h = (9 * k + 7) / 8;
            var budget = 100 * (balloons + h);
            var initial = 10 * new ScoreFraction(1000000 - budget, 10L * notes).Ceiling;
            var ceiling = notes * initial + budget;
            // Extremely long custom charts can consume the entire million-point note budget.
            if (initial <= 0 || initial > int.MaxValue || ceiling > int.MaxValue || h > int.MaxValue) return null;
            return new TjaScoreEstimate((int)initial, (int)ceiling, balloons);

            ScoreFraction BeatMicroseconds() => new ScoreFraction((60000000 / bpm + new ScoreFraction(1, 2)).Floor, 1);
            void Step(ScoreFraction beats) => AddDuration(beats * BeatMicroseconds(), beats * 60 / bpm);
            void AddDuration(ScoreFraction microseconds, ScoreFraction seconds)
            {
                if (active == null) return;
                if (active.IsDrumroll) rollTime += microseconds;
                else balloonTime += seconds;
            }
        }
    }
}
