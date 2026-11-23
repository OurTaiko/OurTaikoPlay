namespace OurTaiko
{
    // How one kind of play changes PlayScene's flow. PlayScene runs the normal play and asks its mode at
    // each hook below; a hook that returns true has taken the action over. PlayMode.Normal changes
    // nothing. A mode is chosen once per scene (PlayScene.ChooseMode) and drives the scene through
    // PlayScene's internal operations (HaltPlayback, ResumePlayback, OpenPauseMenu, ...).
    public class PlayMode
    {
        public static readonly PlayMode Normal = new PlayMode();

        // The route every seat's session takes on every branch; null evaluates branches.
        internal virtual BranchRoute? ForcedBranch => null;
        // Whether the restart key restarts the play.
        internal virtual bool RestartKey => true;
        // The notes to draw: preview the paused lane's routes, or hide what the attempt skipped.
        internal virtual (bool preview, bool hideSkipped) Notes(bool paused) => (false, false);

        // The lanes exist but are not prepared yet, so add-ons warm up behind the loading cover.
        internal virtual void Attach() { }
        // The countdown has started; false lets the music start as usual.
        internal virtual bool Begin() => false;
        // A paused frame's input; false hands it to the pause menu.
        internal virtual bool UpdatePaused() => false;
        // The scene's own guards have passed for each of these.
        internal virtual bool TogglePause() => false;
        internal virtual bool Resume() => false;
        internal virtual bool Restart() => false;
        internal virtual bool Back() => false;
        // The song reached its end.
        internal virtual bool Finish() => false;
    }
}
