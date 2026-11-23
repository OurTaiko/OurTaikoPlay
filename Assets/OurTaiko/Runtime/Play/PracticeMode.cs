using System;
using System.Linq;

namespace OurTaiko
{
    // 練習モード in Play.Practice (PlayScene.ChooseMode registers it when the scene has a PracticeView).
    // The play opens paused on its menu: route (charts with branches), measure, then speed. Confirming
    // scrolls the lane back to a two-second lead-in and plays from there at the chosen speed; a pause or
    // the song's end stops the attempt and reopens the menu at that point or the first measure. The
    // pause menu is a layer above it. Practice is single-player, and a replay (PlayScene.Replay) is
    // watched in it too.
    public sealed class PracticeMode : PlayMode
    {
        readonly PlayScene scene;
        public PracticeView View { get; }
        // Created when the countdown starts; the menu's cursor, bar list and speed.
        public PracticeProgress Progress { get; private set; }
        public PracticeStage Stage { get; private set; }
        public bool ChoosingSpeed => Stage == PracticeStage.Speed;
        // Practice never evaluates branches: every branch takes the route chosen in the menu.
        public BranchRoute Branch { get; private set; } = BranchRoute.Normal;
        // What practice add-ons follow. Stopped: the attempt was paused or played to the song's end, and
        // carries what it played. Cleared: it was thrown away (practice starts again, or the menu's restart).
        public event Action<PlaySession> AttemptStopped;
        public event Action AttemptCleared;

        // The lane is scrolling back to the preparation lead-in; playback starts when it arrives.
        bool rewinding;
        // Where a normal play left off (PlayScene.SwitchMode), in visual chart time; the first pause
        // snaps to it once.
        double? entryPosition;

        // A replay takes the routes the play took, so it has no route to choose.
        PracticeStage FirstStage => scene.Session.Chart.Branches.Count > 0 && !scene.IsReplay ? PracticeStage.Branch : PracticeStage.Measure;
        // The offsets in chart seconds at the chosen speed. The clock runs at it whenever it plays, so
        // these agree with chart time; a seek to a new speed must use them, not the clock's old rate.
        double AudioOffset => scene.ConfiguredAudioOffset * Progress.Speed;
        double VisualOffset => scene.ConfiguredVisualOffset * Progress.Speed;
        double JudgeOffset => scene.ConfiguredJudgeOffset * Progress.Speed;

        // Before the lanes are made: the route and position a normal play passed into this practice.
        internal PracticeMode(PlayScene scene, PracticeView view, SceneSwitcher switcher)
        {
            this.scene = scene;
            View = view;
            if (switcher.TakePracticeEntry(out double position, out BranchRoute branch))
            {
                entryPosition = position;
                Branch = branch;
            }
        }

        // The route practice forces on every branch; a replay forces none.
        internal override BranchRoute? ForcedBranch => scene.IsReplay ? null : Branch;
        // Restarting is the pause menu's, and starts the attempt over.
        internal override bool RestartKey => false;
        // A paused practice previews its routes; while it plays (or rewinds) it hides what the
        // attempt skipped, and browsing measures keeps past notes.
        internal override (bool preview, bool hideSkipped) Notes(bool paused) => (paused, !paused || rewinding);

        // The add-ons. Each one is registered here and nowhere else.
        internal override void Attach()
        {
            // The last attempt's offsets and misses over the paused lane (auto play has nothing to review).
            var review = new PracticeTimingReview(() => SettingManager.EnsureInstance().Settings.play.practiceTimingReview);
            scene.Lane1.AddOverlay(review);
            AttemptStopped += attempt => review.Attempt = scene.Lane1.AutoPlay ? null : attempt;
            AttemptCleared += () => review.Attempt = null;
        }

        internal override bool Begin()
        {
            Progress = new PracticeProgress();
            View.previous.onClick.AddListener(() => Move(-1));
            View.next.onClick.AddListener(() => Move(1));
            View.confirm.onClick.AddListener(Confirm);
            Pause(true);
            return true;
        }

        internal override bool UpdatePaused()
        {
            if (scene.PauseMenuOpen) return false;
            if (rewinding) { UpdateRewind(); return true; }
            ShowPause();
            foreach (var press in InputManager.PlayerPresses)
            {
                // Practice is single-player: only the player's own drum reaches its menu.
                if (press.Key == InputKey.LeftKa) { Move(-1); return true; }
                if (press.Key == InputKey.RightKa) { Move(1); return true; }
                if (press.Key == InputKey.LeftDon || press.Key == InputKey.RightDon || press.Key == InputKey.Confirm)
                { Confirm(); return true; }
            }
            return true;
        }

        // Playing, it stops the attempt on the practice menu; on the practice menu it opens the pause
        // menu, and in the pause menu it chooses Resume.
        internal override bool TogglePause()
        {
            if (!scene.IsPaused) { AttemptStopped?.Invoke(scene.Session); Pause(false); return true; }
            if (rewinding) return true;
            if (scene.PauseMenuOpen) { scene.resumeButton.onClick.Invoke(); return true; }
            View.panel.SetActive(false);
            scene.OpenPauseMenu();
            return true;
        }

        // Resume closes the pause menu back to the practice menu.
        internal override bool Resume()
        {
            if (!scene.PauseMenuOpen) return true;
            scene.CloseMenuThen(() =>
            {
                scene.RestorePads(); scene.HoldPauseInput();
                ShowPause();
            });
            return true;
        }

        // The pause menu's restart throws the attempt away at once, and returns to the first measure.
        // A chart that failed to load leaves as any play does.
        internal override bool Restart()
        {
            if (scene.IsFinished) return false;
            if (scene.PauseMenuOpen && !scene.ClosingPauseMenu)
                scene.CloseMenuThen(() =>
                {
                    scene.RestorePads();
                    AttemptCleared?.Invoke();
                    ResetAttempt(Progress.Target);
                    Pause(true);
                });
            return true;
        }

        // Back leaves only from the pause menu, not the practice menu.
        internal override bool Back() => !scene.PauseMenuOpen && !scene.IsFinished;

        // Practice loops back to the first measure; the attempt it just completed stopped there.
        internal override bool Finish()
        {
            AttemptStopped?.Invoke(scene.Session);
            Pause(true);
            return true;
        }

        void RefreshBars() => Progress.SetBars(scene.Session.Chart.Bars
            .Where(scene.Lane1.Shown.IsPracticePreviewActive).Select(bar => bar.Time));

        void Pause(bool first)
        {
            if (Progress == null) return;
            scene.HaltPlayback(); rewinding = false;
            Stage = FirstStage;
            RefreshBars();
            // The cursor is visual chart time, so a bar aligns exactly even with configured offsets.
            double position = first ? TakeStart() : scene.SongTime - AudioOffset - VisualOffset;
            Progress.PauseAt(position, GameTimeline.FrameTime);
            scene.Clock.Seek(position + AudioOffset + VisualOffset, Progress.Speed);
            scene.Lane1.StopAttempt();
            Preview(position);
            scene.HoldPauseInput();
            scene.pauseButton.interactable = true;
            ShowPause();
        }

        // The first pause's cursor: the handed-over measure once, otherwise the first bar.
        double TakeStart()
        {
            double position = entryPosition.HasValue ? Progress.SnapToMeasure(entryPosition.Value) : Progress.First;
            entryPosition = null;
            return position;
        }

        // A replay is judged again from its first press up to here, at the speed it was played.
        PlaySession SessionAt(double position) => scene.IsReplay
            ? scene.Replay.SessionAt(scene.Session.Chart, position + VisualOffset)
            : PlaySession.PracticeAt(scene.Session.Chart, position + VisualOffset, scene.Session, Branch, Progress.Speed);
        // The paused lane browses on a session of its own: the stopped attempt stays the lane's
        // Session, and on the HUD, until the next one replaces it.
        void Preview(double position)
        {
            scene.Lane1.Preview = SessionAt(position);
            scene.Lane1.ResetBranchLane(position + VisualOffset);
        }
        void ResetAttempt(double position)
        {
            scene.Session = SessionAt(position);
            scene.Lane1.ResetBranchLane(position + VisualOffset);
            scene.Lane1.ResetAttempt();
        }

        void ShowPause()
        {
            Progress.Update(GameTimeline.FrameTime);
            scene.RenderNotes(Progress.Position);
            scene.SampleStage(Progress.Position);
            View.Show(Stage, Progress, Branch);
        }

        bool CanAdjust => Progress != null && scene.IsPaused && !rewinding && !scene.PauseMenuOpen
            && !scene.ClosingPauseMenu && !scene.InputBlocked && !scene.PauseInputHeld;

        public void Move(int direction)
        {
            if (!CanAdjust) return;
            // ka whenever the choice moves, in every stage; nothing at either end of a range.
            var before = (Progress.Target, Progress.SpeedTenths, Branch);
            if (Stage == PracticeStage.Speed) Progress.ChangeSpeed(Math.Sign(direction));
            else if (Stage == PracticeStage.Branch) ChangeBranch(Math.Sign(direction));
            else
            {
                Progress.Move(Math.Sign(direction), GameTimeline.FrameTime);
                // Browsing only changes the preview. Build the new judgment session once on start,
                // instead of allocating chart-sized state and resetting every effect on each step.
                scene.Lane1.ResetBranchLane(Progress.Target + VisualOffset);
                scene.Clock.Seek(Progress.Target + AudioOffset + VisualOffset, Progress.Speed);
            }
            if ((Progress.Target, Progress.SpeedTenths, Branch) != before) scene.pauseMenu.PlayMove();
            ShowPause();
        }

        public void Confirm()
        {
            if (!CanAdjust) return;
            scene.HoldPauseInput();
            scene.pauseMenu.PlayConfirm();
            if (Stage != PracticeStage.Speed) { Stage++; ShowPause(); return; }
            AttemptCleared?.Invoke();
            ResetAttempt(Progress.Target);
            View.panel.SetActive(false);
            rewinding = true;
            // Playback renders chart time - VisualOffset, so the scroll ends on the first played frame.
            Progress.Rewind(Progress.PlaybackStart(AudioOffset, VisualOffset, JudgeOffset) - AudioOffset - VisualOffset,
                GameTimeline.FrameTime);
        }

        void UpdateRewind()
        {
            Progress.Update(GameTimeline.FrameTime);
            scene.RenderNotes(Progress.Position);
            scene.SampleStage(Progress.Position);
            if (Progress.Scrolling) return;
            rewinding = false;
            scene.music.pitch = (float)Progress.Speed;
            scene.Clock.Seek(Progress.PlaybackStart(AudioOffset, VisualOffset, JudgeOffset), Progress.Speed);
            // A replay plays its lead-in too: the record is judged up to where playback starts.
            if (scene.IsReplay) { scene.Session = scene.Replay.SessionAt(scene.Session.Chart, scene.ChartTimeNow); scene.Lane1.ResetAttempt(); }
            // The preparation lead-in may start in a different section than the practice target.
            scene.Lane1.ResetBranchLane(scene.ChartTimeNow);
            scene.ResumePlayback();
        }

        // A new route changes the notes and bar lines ahead, so the preview and the bar list are rebuilt
        // at the same position.
        void ChangeBranch(int direction)
        {
            var route = (BranchRoute)Math.Max((int)BranchRoute.Normal, Math.Min((int)BranchRoute.Master, (int)Branch + direction));
            if (route == Branch) return;
            Branch = route;
            Preview(Progress.Target);
            RefreshBars();
            Progress.PauseAt(Progress.Target, GameTimeline.FrameTime);
        }
    }
}
