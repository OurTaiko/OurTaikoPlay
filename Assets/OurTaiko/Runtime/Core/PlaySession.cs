using System;
using System.Linq;

namespace OurTaiko
{
    // Pure C#: independent of frame rate, rendering and audio, and testable without a scene.
    public sealed class PlaySession
    {
        public const double GoodWindow = 0.0250250015258789, OkWindow = 0.0750750045776367, BadWindow = 0.108441665649414;
        public readonly TaikoChart Chart;
        public readonly bool[] Resolved;
        public readonly int[] LongHits;
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Good { get; private set; }
        public int Ok { get; private set; }
        public int Bad { get; private set; }
        public int Rolls { get; private set; }
        public double Gauge { get; private set; }
        public event Action<int, Judgment> Judged;
        readonly int total, baseScore;
        readonly double goodWindow, okWindow, badWindow;

        public PlaySession(TaikoChart chart)
        {
            Chart = chart; Resolved = new bool[chart.Notes.Count]; LongHits = new int[chart.Notes.Count];
            total = Math.Max(1, chart.Notes.Count(n => !n.IsLong));
            baseScore = (int)Math.Ceiling(1000000.0 / total / 10) * 10;
            bool easy = chart.Course == "Easy" || chart.Course == "Normal";
            goodWindow = easy ? 0.0417083358764648 : GoodWindow;
            okWindow = easy ? 0.108441665649414 : OkWindow;
            badWindow = easy ? 0.125125 : BadWindow;
        }

        public void Advance(double time, bool auto)
        {
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                if (Resolved[i]) continue;
                var note = Chart.Notes[i];
                if (note.Time > time) continue;
                if (note.IsLong)
                {
                    if (auto)
                    {
                        int expected = (int)(Math.Max(0, Math.Min(time, note.EndTime) - note.Time) * 15) + 1;
                        while (!Resolved[i] && LongHits[i] < expected) HitLong(i);
                    }
                    if (time > note.EndTime) Resolved[i] = true;
                }
                else if (auto) Resolve(i, Judgment.Good);
                else if (time - note.Time > badWindow) Resolve(i, Judgment.Bad);
            }
        }

        public Judgment Hit(bool ka, double time)
        {
            Advance(time, false);
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                var n = Chart.Notes[i];
                if (Resolved[i] || n.IsLong) continue;
                double delta = Math.Abs(n.Time - time);
                if (n.Time - time > badWindow) break;
                if (delta <= badWindow && n.IsKa == ka)
                {
                    Judgment result = delta <= goodWindow ? Judgment.Good : delta <= okWindow ? Judgment.Ok : Judgment.Bad;
                    Resolve(i, result); return result;
                }
                // A later same-color note cannot steal input from the current note.
                break;
            }
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                var n = Chart.Notes[i];
                if (!Resolved[i] && n.IsLong && time >= n.Time && time <= n.EndTime && (!n.IsBalloon || !ka))
                { HitLong(i); return Judgment.Roll; }
            }
            return Judgment.None;
        }

        void HitLong(int i)
        {
            var n = Chart.Notes[i]; LongHits[i]++; Rolls++; Score += n.IsBalloon ? 300 : 100;
            if (n.IsBalloon && LongHits[i] >= n.BalloonHits) { Score += 5000; Resolved[i] = true; }
            Judged?.Invoke(i, Judgment.Roll);
        }

        void Resolve(int i, Judgment result)
        {
            Resolved[i] = true;
            if (result == Judgment.Bad) { Bad++; Combo = 0; Gauge -= 2.4 / total; }
            else
            {
                if (result == Judgment.Good) Good++; else Ok++;
                Combo++; MaxCombo = Math.Max(MaxCombo, Combo);
                Score += result == Judgment.Good ? baseScore : baseScore / 20 * 10;
                Gauge += (result == Judgment.Good ? 1.2 : 0.6) / total;
            }
            Gauge = Math.Max(0, Math.Min(1, Gauge)); Judged?.Invoke(i, result);
        }
    }
}
