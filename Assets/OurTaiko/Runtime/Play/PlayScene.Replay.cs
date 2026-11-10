using System;

namespace OurTaiko
{
    // Watching a recorded play (SceneSwitcher.Replay) in the practice scene: its measure and speed
    // menu, with the drum struck by the record instead of the player or autoplay. The chart is rebuilt
    // with the record's options, branches are evaluated as in the real play, and every start or pause
    // judges the record up to that point again, so the score, combo, gauge and route shown are the
    // play's own wherever it is opened. The judgment offset and custom judge windows do not
    // apply: the record's times are already corrected, and only standard windows are ever uploaded.
    public sealed partial class PlayScene
    {
        public bool IsReplay => replay != null;
        public ReplayFeed Replay => replay;
        ReplayFeed replay;

        // Before the lanes are made.
        void TakeReplay()
        {
            if (!IsPractice || switcher.Replay == null) return;
            replay = new ReplayFeed(switcher.Replay);
            autoPlay = false;
            judgeOffset = 0;
            judgeWindows = null;
        }

        // The route practice forces on every branch; a replay forces none.
        BranchRoute? ForcedBranch => IsReplay ? (BranchRoute?)null : practiceBranch;

        TaikoChart PrepareReplayChart(string course, string side)
        {
            var chart = song.ParseBlock(course, side);
            ChartModifiers.Apply(chart, replay.Record);
            return chart;
        }

        PlayOptions ReplayOptions() => new PlayOptions
        {
            speed = replay.Record.Speed, inverse = replay.Record.Inverse, display = replay.Record.Display,
            random = (RandomMode)replay.Record.Random,
        };
    }
}
