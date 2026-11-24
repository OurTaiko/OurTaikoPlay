namespace OurTaiko
{
    // Watching a recorded play (SceneSwitcher.Replay) in the practice scene: practice's measure and
    // speed menu (PracticeMode), with the drum struck by the record instead of the player or オート.
    // The chart is rebuilt with the record's options, branches are evaluated as in the real play, and
    // every start or pause judges the record up to that point again, so the score, combo, gauge and
    // route shown are the play's own wherever it is opened. The judgment offset and custom judge
    // windows do not apply: the record's times are already corrected, and only standard windows are
    // ever uploaded. Nobody plays a replay, so it has no touch drum and no switch to normal play.
    public sealed class ReplayMode : PracticeMode
    {
        public ReplayFeed Feed { get; }

        internal ReplayMode(PlayScene scene, PracticeView view, SceneSwitcher switcher) : base(scene, view, switcher)
        {
            Feed = new ReplayFeed(switcher.Replay);
        }

        internal override bool StandardRules => true;
        internal override bool KeepsJudgeOffset => false;
        internal override bool TouchDrum => false;
        internal override bool OffersModeSwitch => false;
        // Branches are evaluated from the record's hits.
        internal override BranchRoute? ForcedBranch => null;
        // A replay takes the routes the play took, so it has no route to choose.
        private protected override bool ChoosesRoute => false;

        internal override PlayOptions Options(int seat, int side) => new PlayOptions
        {
            speed = Feed.Record.Speed, inverse = Feed.Record.Inverse, display = Feed.Record.Display,
            random = (RandomMode)Feed.Record.Random,
        };

        // Always parsed again: the loading scene prepared it with the player's options.
        internal override TaikoChart Chart(SongDefinition song, string course, string side, TaikoChart prepared)
        {
            var chart = song.ParseBlock(course, side);
            ChartModifiers.Apply(chart, Feed.Record);
            return chart;
        }

        internal override ReplayFeed ReplayOf(int seat) => Feed;

        // The record is judged again from its first press up to here, at the speed it was played.
        private protected override PlaySession SessionAt(double position) => Feed.SessionAt(scene.Session.Chart, position + VisualOffset);

        // The lead-in plays too: the record is judged up to where playback starts.
        private protected override void PlaybackStarting()
        {
            scene.Session = Feed.SessionAt(scene.Session.Chart, scene.ChartTimeNow);
            scene.Lane1.ResetAttempt();
        }
    }
}
