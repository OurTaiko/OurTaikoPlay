using System.Collections;

namespace OurTaiko
{
    // How one kind of play changes PlayScene's flow. PlayScene runs the normal play and asks its mode at
    // each hook below; a hook that returns true has taken the action over. PlayMode.Normal changes
    // nothing. A mode is chosen once per scene (PlayScene.ChooseMode) and drives the scene through
    // PlayScene's internal operations (HaltPlayback, ResumePlayback, OpenPauseMenu, LeavePlay, ...).
    // The modes: PracticeMode, ReplayMode (a practice that watches a record), GhostMode, MultiMode.
    public class PlayMode
    {
        public static readonly PlayMode Normal = new PlayMode();

        // No オート and only the standard judge windows: a record (a replay, a ghost) or a room's round is
        // set beside other plays, and only those windows are ever recorded.
        internal virtual bool StandardRules => false;
        // Whether the player's judgment offset applies; a replay's times are already corrected.
        internal virtual bool KeepsJudgeOffset => true;
        // Whether the touch drum is offered (the single-player setting still decides).
        internal virtual bool TouchDrum => true;
        // Whether the pause menu offers switching between normal play and practice.
        internal virtual bool OffersModeSwitch => true;
        // Whether the play can be paused at all (the pause and back keys, the button, losing focus).
        internal virtual bool Pauses => true;
        // The route every seat's session takes on every branch; null evaluates branches.
        internal virtual BranchRoute? ForcedBranch => null;
        // Whether the restart key restarts the play.
        internal virtual bool RestartKey => true;
        // The notes to draw: preview the paused lane's routes, or hide what the attempt skipped.
        internal virtual (bool preview, bool hideSkipped) Notes(bool paused) => (false, false);

        // Each seat's lane, as PlayScene.AddLane makes it: the views it is drawn on (the scene's own
        // for that seat), its options (side: the seat's side's), its chart (what the loading scene
        // prepared, or null to parse the seat's own block) and the record striking its drum instead of
        // the seat's input (null: the player or オート). A lane struck by a record is judged as it was
        // played, without オート or the player's judgment offset.
        internal virtual LaneViews Views(int seat, LaneViews own) => own;
        internal virtual PlayOptions Options(int seat, int side) => PlayOptions.ForSeat(side);
        internal virtual TaikoChart Chart(SongDefinition song, string course, string side, TaikoChart prepared) => prepared;
        internal virtual ReplayFeed ReplayOf(int seat) => null;

        // The lanes exist but are not prepared yet, so add-ons warm up behind the loading cover.
        internal virtual void Attach() { }
        // The chart could not be read; the scene shows the error unless the mode leaves instead.
        internal virtual void LoadFailed() { }
        // The play is loaded and the cover has opened; the countdown waits for this to end (null: it
        // does not wait). Abandoned: the play left meanwhile and never starts.
        internal virtual IEnumerator Ready() => null;
        internal virtual bool Abandoned => false;
        // The countdown has started; false lets the music start as usual.
        internal virtual bool Begin() => false;
        // Every frame, before the scene's own checks (the play may not have started yet).
        internal virtual void Update() { }
        // A paused frame's input; false hands it to the pause menu.
        internal virtual bool UpdatePaused() => false;
        // The scene's own guards have passed for each of these.
        internal virtual bool TogglePause() => false;
        internal virtual bool Resume() => false;
        internal virtual bool Restart() => false;
        internal virtual bool Back() => false;
        // The song reached its end.
        internal virtual bool Finish() => false;
        // The play has finished: each lane's result is captured and input has stopped.
        internal virtual void Finished() { }
        // The ending is over; true has shown the results its own way.
        internal virtual bool ShowResult() => false;
        // The scene is going away.
        internal virtual void Release() { }
    }
}
