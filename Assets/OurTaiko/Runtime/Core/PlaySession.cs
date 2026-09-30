using System;
using System.Collections.Generic;
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
        public BranchRoute CurrentBranch { get; private set; } = BranchRoute.Normal;
        public IReadOnlyList<BranchRoute> BranchHistory => branchHistory;
        public double LastBranchValue { get; private set; }
        public event Action<int, Judgment> Judged;
        public event Action<ChartBranch, BranchRoute> BranchSelected;
        readonly int total, baseScore;
        readonly double goodWindow, okWindow, badWindow;
        readonly int[] selectedRoutes;
        readonly List<BranchRoute> branchHistory = new List<BranchRoute>();
        readonly List<TimelineEvent> timeline = new List<TimelineEvent>();
        int nextEvent, branchNotes, branchRolls;
        double branchPoints;

        sealed class TimelineEvent
        {
            public double Time;
            public ChartBranch Branch;
            public ChartSection Section;
            public int Priority => Branch != null ? 1 : Section.BranchId < 0 ? 0 : 2;
        }

        public PlaySession(TaikoChart chart)
        {
            Chart = chart; Resolved = new bool[chart.Notes.Count]; LongHits = new int[chart.Notes.Count];
            // The reference player's base score / gauge denominator uses common + Master notes.
            total = Math.Max(1, chart.Notes.Count(n => !n.IsLong && (n.BranchId < 0 || n.Route == BranchRoute.Master)));
            baseScore = (int)Math.Ceiling(1000000.0 / total / 10) * 10;
            bool easy = chart.Course == "Easy" || chart.Course == "Normal";
            goodWindow = easy ? 0.0417083358764648 : GoodWindow;
            okWindow = easy ? 0.108441665649414 : OkWindow;
            badWindow = easy ? 0.125125 : BadWindow;
            selectedRoutes = Enumerable.Repeat(-1, chart.Branches.Count).ToArray();
            foreach (var branch in chart.Branches) timeline.Add(new TimelineEvent { Time = branch.DecisionTime, Branch = branch });
            foreach (var section in chart.Sections) timeline.Add(new TimelineEvent { Time = section.Time, Section = section });
            // Stable order preserves authored checkpoint order when several become ready together.
            timeline = timeline.OrderBy(e => e.Time).ThenBy(e => e.Priority).ToList();
        }

        public bool IsActive(ChartNote note) => IsActive(note.BranchId, note.Route);
        bool IsActive(int branchId, BranchRoute route) => branchId < 0 || selectedRoutes[branchId] == (int)route;
        public BranchRoute? SelectedRoute(int branchId) => selectedRoutes[branchId] < 0 ? (BranchRoute?)null : (BranchRoute)selectedRoutes[branchId];

        public void Advance(double time, bool auto)
        {
            // Split a long frame at each reset/decision. Future hits must not affect an
            // earlier checkpoint, and inactive routes must never enter its statistics.
            while (nextEvent < timeline.Count && timeline[nextEvent].Time <= time)
            {
                var item = timeline[nextEvent++];
                AdvanceNotes(item.Time - 1e-9, auto);
                if (item.Branch != null) SelectBranch(item.Branch);
                else if (IsActive(item.Section.BranchId, item.Section.Route)) ResetBranchStats();
            }
            AdvanceNotes(time, auto);
        }

        void ResetBranchStats() { branchPoints = 0; branchNotes = 0; branchRolls = 0; }

        void SelectBranch(ChartBranch branch)
        {
            double value;
            if (branch.Condition == BranchCondition.Accuracy)
                value = branchNotes == 0 ? 0 : Math.Max(0, Math.Min(100, (int)(branchPoints / branchNotes * 100)));
            else
            {
                int activeRollHits = 0;
                for (int i = 0; i < Chart.Notes.Count; i++)
                {
                    var note = Chart.Notes[i];
                    if (IsActive(note) && note.IsLong && !note.IsBalloon && note.Time <= branch.DecisionTime && branch.DecisionTime < note.EndTime)
                        activeRollHits = Math.Max(activeRollHits, LongHits[i]);
                }
                value = Math.Max(branchRolls, activeRollHits);
            }
            var chosen = value >= branch.ExpertThreshold && value < branch.MasterThreshold && branch.ExpertThreshold >= 0
                ? BranchRoute.Expert : value >= branch.MasterThreshold ? BranchRoute.Master : BranchRoute.Normal;
            selectedRoutes[branch.Id] = (int)chosen;
            CurrentBranch = chosen; LastBranchValue = value; branchHistory.Add(chosen);
            ResetBranchStats();
            BranchSelected?.Invoke(branch, chosen);
        }

        void AdvanceNotes(double time, bool auto)
        {
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                if (Resolved[i] || !IsActive(Chart.Notes[i])) continue;
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
                if (Resolved[i] || n.IsLong || !IsActive(n)) continue;
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
                if (!Resolved[i] && IsActive(n) && n.IsLong && time >= n.Time && time <= n.EndTime && (!n.IsBalloon || !ka))
                { HitLong(i); return Judgment.Roll; }
            }
            return Judgment.None;
        }

        void HitLong(int i)
        {
            var n = Chart.Notes[i]; LongHits[i]++; Rolls++; Score += n.IsBalloon ? 300 : 100;
            if (!n.IsBalloon) branchRolls++;
            if (n.IsBalloon && LongHits[i] >= n.BalloonHits) { Score += 5000; Resolved[i] = true; }
            Judged?.Invoke(i, Judgment.Roll);
        }

        void Resolve(int i, Judgment result)
        {
            Resolved[i] = true;
            branchNotes++;
            branchPoints += result == Judgment.Good ? 1 : result == Judgment.Ok ? 0.5 : 0;
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
