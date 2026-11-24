using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // The play scenes' driver: the song clock, music, pause, title and ending around one PlayerLane per
    // seat. The 1P lane's views are firstLane (on NoteLane); Play.Double adds secondLane.
    // The art and sounds the lanes draw with are PlaySceneBase's.
    public sealed partial class PlayScene : PlaySceneBase
    {
        [Tooltip("The touch drum; Settings > Play > Enable Drumpad for Single Player Mode turns it on or off. It strikes 1P.")]
        public DrumPad drumPad;
        public TMP_Text title, subtitle, resultText;
        [Tooltip("DoublePlayScene: 2P's lane. Empty in the single-player scenes.")]
        public LaneViews secondLane;
        public GameObject pausePanel, resultPanel;
        public PauseMenuView pauseMenu;
        public UnityEngine.UI.Button pauseButton, restartButton, backButton, resumeButton, resultRestart, resultBack;
        [Tooltip("The backdrop: the sets the chart's #SCENEPRESET asks for or a draw, following the players' gauges, misses and drumrolls.")]
        public StageView stage;
        [Tooltip("The chart's background movie behind the lanes; practice moves it with the measure.")]
        public BackgroundMovieView movie;
        double movieOffset;
        [Tooltip("Play.Practice's menu. A scene with one plays PracticeMode; empty in every other play scene.")]
        public PracticeView practiceView;
        public bool IsPractice => practiceView != null;
        // The practice menu and its attempts (a ReplayMode when it watches a record); null outside practice.
        public PracticeMode Practice { get; private set; }
        // A ghost battle in Play.Double, a room's round in Play.Single; null otherwise.
        public GhostMode Ghost { get; private set; }
        public MultiMode Multi { get; private set; }
        // How this play differs from a normal one (PlayMode); chosen once, in ChooseMode.
        PlayMode mode = PlayMode.Normal;

        // One lane per seat: 1P, then 2P in Play.Double.
        public IReadOnlyList<PlayerLane> Lanes => lanes;
        readonly List<PlayerLane> lanes = new List<PlayerLane>();
        public PlayerLane Lane1 => lanes.Count > 0 ? lanes[0] : null;
        public PlaySession Session
        {
            get => Lane1?.Session;
            internal set => Lane1.Replace(value);
        }
        // A drum press still waiting out the input delay must not land on the other side of a pause.
        public bool IsPaused
        {
            get => paused;
            private set
            {
                if (paused != value) InputManager.DropDelayed();
                paused = value;
            }
        }
        bool paused;
        // 1P's drum sounds started so far.
        public int DrumSounds => Lane1?.DrumSounds ?? 0;
        public bool IsFinished { get; private set; }
        public PlayResult Result => Lane1?.Result;
        public Online.PlayRecord Record => Lane1?.Record;
        readonly SongClock songClock = new SongClock();
        public double SongTime => songClock.Time;
        public double RenderedTime => Lane1?.RenderedTime ?? 0;
        // Views come from pools while a note is on the lane; null when it is not drawn.
        public RectTransform NoteRoot(int index) => Lane1.NoteRoot(index);
        public RectTransform MojiRoot(int index) => Lane1.MojiRoot(index);
        public RectTransform BarRoot(int index) => Lane1.BarRoot(index);
        SongDefinition song;
        // Configured in real seconds. Chart time runs at the clock's rate (practice speed), so they are
        // scaled by it wherever they meet chart time.
        double audioOffset, visualOffset, judgeOffset;
        // PlaySettings.CustomJudgeWindows (null: the course's own), read once per play like the offsets.
        JudgeWindows? judgeWindows;
        int playedAudioOffsetMs;
        // OpenTaiko judges and draws at a whole millisecond of the play clock (cut toward zero).
        double ChartTime => TjaTime.Truncate(SongTime - audioOffset * songClock.Rate);
        internal override double ChartTimeNow => ChartTime;
        internal override double JudgeOffset => judgeOffset * songClock.Rate;
        internal override double ClockRate => songClock.Rate;
        internal override bool AcceptsHits => songClock.Started && !switcher.IsInputBlocked && !IsPaused && !IsFinished;
        internal override float MovieLaneAlpha => movie != null ? movie.LaneAlpha : 1;
        SongInfo displayInfo;
        // The subtitle line shows the SUBTITLE and, when the chart names one, the MAKER in turn.
        const float SubtitleRotationSeconds = 4f;
        string[] subtitleLines = Array.Empty<string>();
        int subtitleIndex;
        float subtitleShownAt;
        bool autoPlay;
        SceneSwitcher switcher;
        // What this scene plays (SceneSwitcher.Request); null for a direct run without a song.
        PlayRequest request;
        readonly List<DrumPad> pausedPads = new List<DrumPad>();
        bool closingPauseMenu;
        bool resuming, resumeLostFocus;
        int resumeFrame = -1;
        int pauseOpenedFrame = -1;
        // When the play ends (SongEnd), for the seats' charts both played.
        double? songEnd;
        // After the play's end: the lanes playing an ending, under which the music fades out.
        List<PlayerLane> endings;
        double endingDuration, endingStartedAt, endedAt;
        bool resultShown;

        IEnumerator Start()
        {
            switcher = SceneSwitcher.EnsureInstance();
            PlayfieldMask.Cover(this);
            request = switcher.Request;
            ChooseMode();
            switcher.SceneChanging += PrepareToLeave;
            song = request?.Song;
            // Play scenes start from the song list; a direct run without a song goes back to Entry.
            if (song == null)
            {
                Log.Warn("Play", "No song is selected; returning to Entry.", this);
                switcher.SwitchScene(SceneSwitcher.MenuScene);
                yield break;
            }
            autoPlay = request.Seats[0].AutoPlay;
            var playSettings = SettingManager.EnsureInstance().Settings.play;
            playedAudioOffsetMs = playSettings.audioOffsetMs;
            audioOffset = (song.audioOffsetMs + (double)playSettings.audioOffsetMs) / 1000.0;
            visualOffset = song.visualOffsetMs / 1000.0;
            judgeOffset = autoPlay || !mode.KeepsJudgeOffset ? 0 : playSettings.judgeOffsetMs / 1000.0;
            // A record or a room's round is played by the standard rules: standard windows, no オート.
            judgeWindows = mode.StandardRules ? null : playSettings.CustomJudgeWindows;
            if (mode.StandardRules) autoPlay = false;
            InputManager.DrumDelay = Math.Min(Math.Max(0, playSettings.inputDelayMs), PlaySettings.InputDelayMaxMs) / 1000.0;
            pauseButton.onClick.AddListener(TogglePause);
            resumeButton.onClick.AddListener(Resume);
            restartButton.onClick.AddListener(Restart);
            backButton.onClick.AddListener(Back);
            resultRestart.onClick.AddListener(Restart);
            resultBack.onClick.AddListener(Back);
            pausePanel.SetActive(false); resultPanel.SetActive(false);
            // Disabled and hidden together: an inactive pad neither draws nor registers with InputManager,
            // and pause/resume only re-enables the pads it disabled itself.
            // The touch drum is a single-player setting; two players use the keyboard. A ghost battle
            // has one player, whose side the touch drum strikes (InputManager.Lead).
            if (drumPad != null) drumPad.gameObject.SetActive(SettingManager.EnsureInstance().Settings.play.singlePlayerDrumPad && switcher.Seated.Count < 2 && mode.TouchDrum);
            try
            {
                if (kusudama != null) kusudama.ResetDisplay();
                var sessions = new List<PlaySession> { AddLane(0, mode.Views(0, firstLane)) };
                if (secondLane != null) sessions.Add(AddLane(1, mode.Views(1, secondLane)));
                // Both seats' kusudama at the same time are one ball; a lone one is a balloon.
                if (sessions.Count > 1) Kusudama.Share(sessions[0], sessions[1]);
                mode.Attach();
                for (int i = 0; i < lanes.Count; i++) lanes[i].Prepare(sessions[i]);
                // The single-player scenes' don/ka are 1P's neiro (tests read them).
                don = Lane1.Don; ka = Lane1.Ka;
                displayInfo = song.ReadDisplayInfo();
                title.text = displayInfo.Title;
                ShowSubtitleLines(displayInfo);
                if (stage != null)
                {
                    // SongLoadingScene chose it and fetched what it needs; a play entered any other
                    // way gets one out of what is here.
                    var prepared = switcher.PreparedStage(song);
                    if (prepared.HasValue) stage.Use(prepared.Value);
                    else stage.Setup(request.ScenePresets(displayInfo.ScenePreset));
                }
                music.SetAudioSong(song);
                hitAudio.PrepareAudioEffects(balloonPop, kusudamaPop, adLibHit, mineHit);
                // Both seats play one file, so the song's one BGMOVIE plays behind both lanes.
                if (movie != null) { movieOffset = song.MovieOffset(); movie.Load(song.moviePath); }
            }
            catch (Exception error)
            {
                Log.Exception("Play", error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = L10n.Get("Play", "play.chartLoadFailed") + "\n<size=22>" + error.Message + "</size>";
            }
            if (IsFinished) { mode.LoadFailed(); yield break; }
            AddModeSwitch();
            while (switcher.IsInputBlocked) yield return null;
            // The mode may hold the countdown (a room's round starts for every player at once).
            var ready = mode.Ready();
            if (ready != null)
            {
                yield return ready;
                if (mode.Abandoned) yield break;
            }
            // The full countdown starts only after the global cover has opened. Both seats play the same
            // file, so one OFFSET leads the song in.
            songClock.Start(GameTimeline.AudioNow, Math.Max(2, Session.Chart.Offset + 2 - audioOffset - Math.Min(0, judgeOffset)));
            if (!mode.Begin()) StartDueMusic();
        }

        // The one place a play mode is registered, each with its data from the request. A direct Editor
        // run must return to the same mode after Back and another song; PracticeMode reads
        // SceneSwitcher.PracticeMode as it takes the mode switch's hand-over, and PlaysGhost and
        // PlaysMulti read it, so that is written first. A replay is watched in the practice scene; a
        // ghost battle needs two lanes, a room's round one.
        void ChooseMode()
        {
            switcher.PracticeMode = IsPractice;
            if (IsPractice) mode = Practice = request?.Replay != null
                ? new ReplayMode(this, practiceView, switcher, request.Replay)
                : new PracticeMode(this, practiceView, switcher);
            else if (secondLane != null && switcher.PlaysGhost) mode = Ghost = new GhostMode(this, switcher, request.Ghost);
            else if (secondLane == null && switcher.PlaysMulti) mode = Multi = new MultiMode(this, switcher, request.Multi);
        }

        // SongLoadingScene parsed each seat's chart behind the curtain; restarts and direct runs parse
        // here. Each seat reads the block of the side decided before the play, with its own options
        // (a replay or a ghost battle: its record's, PlayMode.Options and Chart). The lane is
        // prepared with the returned session once every seat has one.
        PlaySession AddLane(int seat, LaneViews views)
        {
            var part = request.Seats[seat];
            string course = part.Course;
            // The first lane is played on the leading side (SceneSwitcher.Lead) unless two play.
            int side = seat == 0 ? switcher.Lead.Index() : seat;
            var options = mode.Options(seat, side);
            var chart = mode.Chart(song, course, part.Side, switcher.TakePreparedChart(song, course, seat))
                ?? PrepareChart(song, course, part.Side, side);
            // A record striking the drum instead of the seat's input.
            var recording = mode.ReplayOf(seat);
            bool recorded = recording != null;
            var lane = new PlayerLane(this, seat, views, options, seat == 0 ? autoPlay : !recorded && part.AutoPlay, new Online.PlayRecord
            {
                // Replay v1 stores already corrected judgment time. Keep its two-offset convention:
                // visual time = recorded judgment time - VisualOffsetMs (B cancels here).
                AudioOffsetMs = (int)Math.Round((audioOffset + judgeOffset) * 1000),
                VisualOffsetMs = (int)Math.Round((visualOffset - judgeOffset) * 1000),
            });
            lane.Record.SetOptions(options, chart);
            lane.Replay = recording;
            lane.DrumSeat = side;
            lanes.Add(lane);
            // The recording's times are judged as played, without the player's offset.
            var session = new PlaySession(chart, recorded ? 0 : judgeOffset, mode.ForcedBranch, 1, judgeWindows);
            foreach (string warning in session.Chart.Warnings) Log.Warn("Play", "Ignored TJA command: " + warning);
            return session;
        }

        void ShowSubtitleLines(SongInfo info)
        {
            subtitleLines = new[] { info.Subtitle, info.Maker }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            subtitleIndex = 0;
            subtitleShownAt = Time.unscaledTime;
            subtitle.text = subtitleLines.Length > 0 ? subtitleLines[0] : "";
        }

        // Alternates the subtitle and the maker on a fixed cadence, through pauses too.
        void RotateSubtitle()
        {
            if (subtitleLines.Length < 2 || Time.unscaledTime - subtitleShownAt < SubtitleRotationSeconds) return;
            subtitleIndex = (subtitleIndex + 1) % subtitleLines.Length;
            subtitleShownAt = Time.unscaledTime;
            subtitle.text = subtitleLines[subtitleIndex];
        }

        // Player::reset_chart: the play options change the chart before load times are taken.
        public static TaikoChart PrepareChart(SongDefinition song, string course, string side, int seat = 0)
        {
            var chart = song.ParseBlock(course, side);
            ChartModifiers.Apply(chart, PlayOptions.ForSeat(seat), new System.Random());
            return chart;
        }

        // A 段位 course's song: the block the resolver settled on (a server course may pin the side),
        // never the seat's own choice; dan is always played from the 1P seat.
        public static TaikoChart PrepareChart(DanResolvedSong song)
        {
            var chart = song.Song.ParseBlock(song.Course, song.Side);
            ChartModifiers.Apply(chart, PlayOptions.ForSeat(InputManager.Lead.Index()), new System.Random());
            return chart;
        }

        // MajdataPlay plays the track once the clock reaches it and restarts the clock from there.
        void StartDueMusic()
        {
            if (songClock.TrackDue(GameTimeline.AudioNow) is not double position) return;
            music.StopAudio();
            if (position >= music.AudioLength()) { songClock.SkipTrack(); return; }
            music.SeekAudio(position);
            music.PlayAudio();
            songClock.TrackStarted(GameTimeline.AudioNow, AudioEngine.EnsureInstance().Timing.StartDelay);
        }

        void Update()
        {
            RotateSubtitle();
            mode.Update();
            if (switcher == null || switcher.IsInputBlocked || !songClock.Started) return;
            if (IsFinished) { UpdateEnding(); return; }
            TickSong();
            if (closingPauseMenu || Time.frameCount == resumeFrame || Time.frameCount == pauseOpenedFrame) return;
            if (mode.Pauses && (InputManager.GetKeyDown(InputKey.Back) || InputManager.GetKeyDown(InputKey.Pause)))
            { TogglePause(); return; }
            if (mode.RestartKey && InputManager.GetKeyDown(InputKey.Restart)) { Restart(); return; }
            if (IsPaused)
            {
                if (!mode.UpdatePaused()) pauseMenu.HandleInput();
                return;
            }
            if (Session == null || IsFinished) return;
            double time = ChartTime;
            foreach (var lane in lanes) lane.Advance(time);
            UpdatePlayVisuals(time);
            if (time >= SongEndNow()) Finish();
        }

        // Keep the intentional frame-based judgment; input event timestamps only sort hits.
        void TickSong()
        {
            var engine = AudioEngine.EnsureInstance();
            double? playback = engine.Backend == AudioBackend.BassSimple && music.IsAudioPlaying()
                ? music.AudioPosition() + engine.Timing.PositionLead * songClock.Rate : null;
            songClock.Update(GameTimeline.AudioFrameTime, playback);
            StartDueMusic();
            if (movie != null) movie.Show(SongTime - movieOffset, songClock.Rate, !IsPaused);
        }

        // Every seat must reach its end (OpenTaiko IsChartEnded: all players).
        public double SongEndNow() => (songEnd ??= lanes.Max(lane => SongEnd.Of(lane.Session.Chart))) + Math.Max(0, JudgeOffset);

        // Presentation can keep running after the result is captured, without advancing judgment.
        void UpdatePlayVisuals(double time)
        {
            var (preview, hideSkipped) = mode.Notes(IsPaused);
            foreach (var lane in lanes) lane.ShowVisuals(time, time - visualOffset * songClock.Rate, preview, hideSkipped);
            if (kusudama != null) kusudama.ShowTime(time);
            SampleStage(time);
        }

        // 1P's drum (touch drum, tests); see PlayerLane.Hit.
        public void Hit(bool isKa, bool right) => Lane1?.Hit(isKa, right);

        public void TogglePause()
        {
            if (IsFinished || Session == null || !songClock.Started || switcher.IsInputBlocked || closingPauseMenu || !mode.Pauses) return;
            if (mode.TogglePause()) return;
            // The pause key chooses Resume like the menu's own button, so it sounds the same don.
            if (IsPaused) { resumeButton.onClick.Invoke(); return; }
            HaltPlayback();
            OpenPauseMenu();
        }

        public void Resume()
        {
            if (!IsPaused || closingPauseMenu || switcher.IsInputBlocked || IsFinished) return;
            if (mode.Resume()) return;
            resuming = true;
            resumeLostFocus = false;
            StartCoroutine(ClosePauseMenu(() =>
            {
                resuming = false;
                if (resumeLostFocus)
                {
                    pauseOpenedFrame = Time.frameCount;
                    pauseMenu.Show();
                    return;
                }
                ResumePlayback();
                RestorePads();
            }));
        }

        IEnumerator ClosePauseMenu(Action completed)
        {
            closingPauseMenu = true;
            yield return pauseMenu.Hide();
            completed();
            closingPauseMenu = false;
        }

        void DisableDrumPads()
        {
            foreach (var pad in FindObjectsByType<DrumPad>())
            {
                if (pad.gameObject.scene != gameObject.scene || !pad.enabled) continue;
                pausedPads.Add(pad);
                pad.enabled = false;
            }
        }
        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            if (resuming) resumeLostFocus = true;
            if (Session != null && !IsPaused && !IsFinished) TogglePause();
        }
        // The play's end (SongEnd), unless the mode takes it (practice loops). A play (OpenTaiko
        // Game_EndChart) captures the result and stops input, and each lane plays its own ending
        // (Player::spawn_ending_anim) over its lane while the music fades out; ResultScene (which
        // persists the result) follows.
        void Finish()
        {
            if (mode.Finish()) return;
            if (IsFinished) return;
            IsFinished = true;
            foreach (var lane in lanes)
            {
                lane.Result = PlayResult.From(lane.Session, song.ScoreKey, lane.AutoPlay, playedAudioOffsetMs);
                lane.Result.Side = request.Seats[lane.Seat].Side;
                lane.Result.Title = displayInfo.Title;
                lane.Result.Subtitle = displayInfo.Subtitle;
            }
            DisableDrumPads();
            pauseButton.interactable = false;
            mode.Finished();
            endings = lanes.Where(lane => lane.Views.ending != null).ToList();
            foreach (var lane in endings) lane.Views.ending.Begin(lane.Result);
            foreach (var lane in lanes) lane.DonChara?.OnEnding(lane.Result.IsClear, lane.Result.Bad);
            endingDuration = endings.Count > 0 ? endings.Max(lane => lane.Views.ending.Duration) : 0;
            endingStartedAt = GameTimeline.FrameTime;
            endedAt = ChartTime;
        }

        // The stopped Session stays untouched: dancers, gauge loops and existing hit effects keep
        // playing on the song clock while input and scoring stay disabled, and the music fades out
        // linearly over the ending, which the result follows. A play finished while paused (tests)
        // carries the presentation on in real time.
        void UpdateEnding()
        {
            if (endings == null || resultShown) return;
            TickSong();
            double elapsed = GameTimeline.FrameTime - endingStartedAt;
            double time = Math.Max(ChartTime, endedAt + elapsed);
            UpdatePlayVisuals(time);
            foreach (var lane in lanes) lane.ShowBranchTime(time);
            foreach (var lane in endings) lane.Views.ending.ShowTime(Math.Min(elapsed, endingDuration));
            music.SetAudioFade(SongEnd.Fade(elapsed, endingDuration));
            if (elapsed < endingDuration) return;
            resultShown = true;
            if (mode.ShowResult()) return;
            switcher.ShowResult(song, lanes.Select(lane => new SeatResult(lane.Result, lane.Record)).ToArray());
        }
        public void Restart()
        {
            if (mode.Restart()) return;
            LeavePlay(() => SceneSwitcher.EnsureInstance().Restart());
        }
        public void Back()
        {
            if (mode.Back()) return;
            LeavePlay(() => SceneSwitcher.EnsureInstance().SwitchScene(SceneSwitcher.SongSelectScene));
        }

        // Closes the pause menu, if open, before leaving the play.
        internal void LeavePlay(Action action)
        {
            if (closingPauseMenu || switcher.IsInputBlocked) return;
            if (IsPaused && !IsFinished) StartCoroutine(ClosePauseMenu(action));
            else action();
        }
        void PrepareToLeave(string scene)
        {
            HaltPlayback();
            InputManager.DropDelayed();
            DisableDrumPads();
        }
        void OnDestroy()
        {
            InputManager.DrumDelay = 0;
            if (switcher != null) switcher.SceneChanging -= PrepareToLeave;
            mode.Release();
            foreach (var lane in lanes) lane.Release();
        }

        public const int SoulGaugeOrder = -20;

        // What a PlayMode drives the scene with. The offsets are the configured ones, in real seconds.
        internal SongClock Clock => songClock;
        internal SongDefinition Song => song;
        internal double ConfiguredAudioOffset => audioOffset;
        internal double ConfiguredVisualOffset => visualOffset;
        internal double ConfiguredJudgeOffset => judgeOffset;
        internal bool InputBlocked => switcher.IsInputBlocked;
        internal bool PauseMenuOpen => pausePanel.activeSelf;
        internal bool ClosingPauseMenu => closingPauseMenu;
        // Input this frame belongs to whatever just opened; nothing else may act on it.
        internal void HoldPauseInput() => pauseOpenedFrame = Time.frameCount;
        internal bool PauseInputHeld => pauseOpenedFrame == Time.frameCount;

        // Stops the clock and every sound where they are.
        internal void HaltPlayback()
        {
            songClock.Pause(); IsPaused = true;
            music.StopAudio(); hitAudio.StopAudio();
        }
        // Runs the clock on from where it stands; the music starts once the clock reaches it.
        internal void ResumePlayback()
        {
            songClock.Resume(GameTimeline.AudioNow);
            IsPaused = false;
            resumeFrame = Time.frameCount;
            StartDueMusic();
        }
        internal void OpenPauseMenu()
        {
            HoldPauseInput();
            DisableDrumPads();
            pauseButton.interactable = false;
            pauseMenu.Show();
        }
        // The pads the pause menu disabled, and the pause button.
        internal void RestorePads()
        {
            foreach (var pad in pausedPads) if (pad != null) pad.enabled = true;
            pausedPads.Clear();
            pauseButton.interactable = true;
        }
        internal void CloseMenuThen(Action completed) => StartCoroutine(ClosePauseMenu(completed));

        internal void RenderNotes(double time)
        {
            var (preview, hideSkipped) = mode.Notes(IsPaused);
            foreach (var lane in lanes) lane.RenderNotes(time, preview, hideSkipped);
        }

        internal void SampleStage(double time)
        {
            if (stage != null) stage.Show(time, lanes);
        }

        internal override void StageJudged(int seat, bool missed, double bpm) => stage?.Judged(seat, missed, bpm);

        internal override void DrumrollHit(int seat)
        {
            if (stage != null) stage.DrumrollHit(seat);
        }
    }
}
