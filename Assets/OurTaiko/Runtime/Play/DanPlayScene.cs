using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // 段位道場's play scene (YataiDON DanGameScreen): one lane plays the course's songs in turn.
    // Each song gets a new PlaySession, so its branches and their counters start over at 普通譜面
    // (OpenTaiko resets them at every #NEXTSONG too), while the course's soul gauge, score,
    // judgments and the running combo carry on (PlayerLane.SharedGauge / Carry). The exams are
    // judged by DanProgress: a less exam fails the moment its allowance is used up, a more exam
    // when its song (or the course) ends; a failed exam ends the course once that song is over.
    // Before each song the doors close over the lane with its title (DanBetween), and the song
    // begins once they have parted and its title has gone from the lane. Pause offers resume, restart (the whole course) and back (the course
    // select); the doors, the music's tail and the delays to the result wait while it is paused.
    public sealed class DanPlayScene : PlaySceneBase
    {
        public DanPlayView view;
        [Tooltip("The touch drum; Settings > Play > Enable Drumpad for Single Player Mode turns it on or off.")]
        public DrumPad drumPad;
        public TMP_Text title, subtitle, resultText;
        public GameObject pausePanel, resultPanel;
        public PauseMenuView pauseMenu;
        public Button pauseButton, restartButton, backButton, resumeButton, resultRestart, resultBack;
        public AudioClip danFailed, fusumaClose, fusumaOpen;
        [Tooltip("anim/dani_between (Nijiiro): the doors' and the title's clip.")]
        public TextAsset betweenTimeline;

        // After the course's last judgment, or the song a failed exam ended it on: the ending
        // (合格 / 不合格, as on the cabinet) starts a second later.
        const double EndingDelay = 1, MaxTail = 20;

        public PlayerLane Lane { get; private set; }
        public PlaySession Session => Lane?.Session;
        public DanRun Run { get; private set; }
        public DanProgress Progress { get; private set; }
        public SoulGauge Gauge { get; private set; }
        public int SongIndex { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsFinished { get; private set; }
        public bool SongEnded { get; private set; }
        public double SongTime => songClock.Time;
        public double BetweenStartedAt { get; private set; } = double.NaN;
        // Milliseconds since the doors started, on the pause-aware clock.
        public double BetweenElapsedMs => PlayNow * 1000 - BetweenStartedAt;
        // A failed exam ended the course after its song: the music stops and the lane holds still.
        public bool FailedOut { get; private set; }

        readonly SongClock songClock = new SongClock();
        readonly List<PlayResult> songResults = new List<PlayResult>();
        readonly List<Online.PlayRecord> songRecords = new List<Online.PlayRecord>();
        SceneSwitcher switcher;
        SongDefinition song;
        SongInfo displayInfo;
        double audioOffset, visualOffset, judgeOffset;
        int playedAudioOffsetMs, songMaxCombo;
        bool autoPlay, closingPauseMenu, betweenOpened;
        double songEndedAt, lastNoteEnd;
        // Real time less the time spent paused, in seconds.
        double pausedTotal, pausedAt = double.NaN;
        double PlayNow => (double.IsNaN(pausedAt) ? GameTimeline.FrameTime : pausedAt) - pausedTotal;
        LumenClip betweenClip;
        int resumeFrame = -1, pauseOpenedFrame = -1;
        readonly List<DrumPad> pausedPads = new List<DrumPad>();

        public double ChartTime => TjaTime.Truncate(SongTime - audioOffset);
        internal override double ChartTimeNow => ChartTime;
        internal override double JudgeOffset => judgeOffset;
        internal override bool AcceptsHits => songClock.Started && !switcher.IsInputBlocked && !IsPaused && !IsFinished && !SongEnded;

        IEnumerator Start()
        {
            switcher = SceneSwitcher.EnsureInstance();
            switcher.PracticeMode = false; switcher.TwoPlayers = false;
            switcher.SceneChanging += PrepareToLeave;
            Run = switcher.CurrentDan;
            if (Run == null && switcher.SelectedDan != null && switcher.SelectedDan.IsPlayable) Run = new DanRun(switcher.SelectedDan);
            if (Run == null)
            {
                Debug.LogWarning("No 段位 course is selected; returning to the course select.", this);
                switcher.SwitchScene(SceneSwitcher.DanSelectScene);
                yield break;
            }
            var options = PlayOptions.ForSeat(0);
            autoPlay = options.auto;
            var playSettings = SettingManager.EnsureInstance().Settings.play;
            playedAudioOffsetMs = playSettings.audioOffsetMs;
            judgeOffset = autoPlay ? 0 : playSettings.judgeOffsetMs / 1000.0;
            InputManager.DrumDelay = Math.Min(Math.Max(0, playSettings.inputDelayMs), PlaySettings.InputDelayMaxMs) / 1000.0;
            pauseButton.onClick.AddListener(TogglePause);
            resumeButton.onClick.AddListener(Resume);
            restartButton.onClick.AddListener(Restart);
            backButton.onClick.AddListener(Back);
            resultRestart.onClick.AddListener(Restart);
            resultBack.onClick.AddListener(Back);
            pausePanel.SetActive(false); resultPanel.SetActive(false);
            if (drumPad != null) drumPad.gameObject.SetActive(playSettings.singlePlayerDrumPad);
            hitAudio.PrepareAudioEffects(balloonPop, kusudamaPop, danFailed, fusumaClose, fusumaOpen);
            betweenClip = betweenTimeline != null ? LumenClip.Parse(betweenTimeline.text) : LumenClip.Empty;
            try
            {
                for (int i = 0; i < Run.Count; i++) Run.Charts[i] ??= PlayScene.PrepareChart(Run.Song(i), Run.Course(i));
                var notes = Run.Charts.Select(c => new ChartStatistics(c).JudgeableNotes).ToList();
                Progress = new DanProgress(Run.Entry.Course, notes);
                Gauge = SoulGauge.Dan(Run.Entry.Songs.Select(s => (s.GaugeCourse, s.Level)).ToList(), notes.Sum());
                Lane = new PlayerLane(this, 0, this, options, autoPlay, new Online.PlayRecord()) { SharedGauge = Gauge };
                ShowCourse();
                BeginSong(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = "CHART COULD NOT LOAD\n<size=22>" + error.Message + "</size>";
            }
            if (IsFinished) yield break;
            // The first song's doors and clock start once the curtain has opened, as PlayScene's.
            while (switcher.IsInputBlocked) yield return null;
            StartSong();
        }

        // The next song on the same lane: a new session (so branches start over), the effects timed
        // on the old chart cleared, and the course's counts carried into the lane's HUD.
        void BeginSong(int index)
        {
            SongIndex = index;
            song = Run.Song(index);
            var playSettings = SettingManager.EnsureInstance().Settings.play;
            audioOffset = (song.audioOffsetMs + (double)playSettings.audioOffsetMs) / 1000.0;
            visualOffset = song.visualOffsetMs / 1000.0;
            var session = new PlaySession(Run.Charts[index], judgeOffset);
            foreach (string warning in session.Chart.Warnings) Debug.LogWarning("Ignored TJA command: " + warning);
            Lane.Record.AudioOffsetMs = (int)Math.Round((audioOffset + judgeOffset) * 1000);
            Lane.Record.VisualOffsetMs = (int)Math.Round((visualOffset - judgeOffset) * 1000);
            Lane.Record.Inputs.Clear();
            Lane.Prepare(session);
            Lane.ResetEffects();
            Lane.ResetBranchLane(0);
            // YataiDON's end_time: the last note (or roll end) to be judged.
            lastNoteEnd = session.Chart.Notes.Count > 0 ? session.Chart.Notes.Max(n => n.IsLong ? n.EndTime : n.Time) : session.Chart.Duration;
            don = Lane.Don; ka = Lane.Ka;
            displayInfo = song.ReadDisplayInfo();
            title.text = displayInfo.Title;
            subtitle.text = displayInfo.Subtitle;
            music.StopAudio();
            var audio = Run.TakeAudio(index);
            if (audio != null) song.SetPreparedAudio(audio);
            music.SetAudioSong(song);
            SongEnded = false; songMaxCombo = 0;
            Progress.Update(default, Gauge.Percent);
            ShowExams();
        }

        // The doors close and part again over the song's title (hidden songs keep it back), which
        // stays on the lane until their animation ends (frame 366); only then does the song begin,
        // with the same lead as a normal play (2 s, or more when its OFFSET asks for it), so no note
        // shows while the title does.
        void StartSong()
        {
            var session = Session;
            double lead = DanBetween.TotalMs / 1000 + Math.Max(2, session.Chart.Offset + 2 - audioOffset - Math.Min(0, judgeOffset));
            BetweenStartedAt = PlayNow * 1000; betweenOpened = false;
            bool hidden = Run.Entry.Songs[SongIndex].Ref.Hidden;
            ShowBetweenText(hidden ? "？？？" : displayInfo.Title, hidden ? "" : displayInfo.Subtitle);
            hitAudio.PlayAudioOneShot(fusumaClose);
            songClock.Start(GameTimeline.AudioNow, lead);
            StartDueMusic();
        }

        void StartDueMusic()
        {
            if (songClock.TrackDue(GameTimeline.AudioNow) is not double position) return;
            music.StopAudio();
            if (position >= music.AudioLength()) { songClock.SkipTrack(); return; }
            music.SeekAudio(position);
            music.PlayAudio();
            songClock.TrackStarted(GameTimeline.AudioNow);
        }

        void Update()
        {
            ShowBetween();
            if (switcher == null || switcher.IsInputBlocked || !songClock.Started || IsFinished || Lane == null) return;
            double? playback = AudioEngine.EnsureInstance().Backend == AudioBackend.BassSimple && music.IsAudioPlaying() ? music.AudioPosition() : null;
            songClock.Update(GameTimeline.AudioFrameTime, playback);
            StartDueMusic();
            if (closingPauseMenu || Time.frameCount == resumeFrame || Time.frameCount == pauseOpenedFrame) return;
            if (InputManager.GetKeyDown(InputKey.Back) || InputManager.GetKeyDown(InputKey.Pause)) { TogglePause(); return; }
            if (InputManager.GetKeyDown(InputKey.Restart)) { Restart(); return; }
            if (IsPaused) { pauseMenu.HandleInput(); return; }
            double time = ChartTime;
            if (!SongEnded) Lane.Advance(time);
            if (!FailedOut)
            {
                Lane.ShowVisuals(time, time - visualOffset, false, false);
                if (kusudama != null) kusudama.ShowTime(time);
            }
            if (!SongEnded)
            {
                songMaxCombo = Math.Max(songMaxCombo, Lane.DisplayCombo);
                if (Progress.Update(Stats(), Gauge.Percent).Count > 0) hitAudio.PlayAudioOneShot(danFailed);
                // YataiDON's end_time: once the last note is judged the song is over for the exams;
                // its music plays out before the next one (capped, for a long silent tail).
                if (time > lastNoteEnd + Math.Max(0, judgeOffset) + PlaySession.BadWindow) EndSong();
            }
            else if (Progress.IsEnded)
            {
                double since = PlayNow - songEndedAt;
                if (since >= EndingDelay) Finish();
            }
            else
            {
                double audio = music.AudioLength();
                if (audio <= 0 || SongTime >= audio || PlayNow - songEndedAt >= MaxTail) NextSong();
            }
            ShowExams();
        }

        DanSongStats Stats() => new DanSongStats
        {
            Good = Session.Good, Ok = Session.Ok, Bad = Session.Bad, Rolls = Session.Rolls, Score = Session.Score, MaxCombo = songMaxCombo,
        };

        void EndSong()
        {
            SongEnded = true;
            songEndedAt = PlayNow;
            Progress.Update(Stats(), Gauge.Percent);
            if (Progress.EndSong().Count > 0) hitAudio.PlayAudioOneShot(danFailed);
            // trigger_fail_out: a failed exam before the last song stops the course at once.
            if (Progress.IsEnded && !Progress.IsLastSong) { FailedOut = true; music.StopAudio(); }
            var result = PlayResult.From(Session, song.ScoreKey, autoPlay, playedAudioOffsetMs);
            result.Title = displayInfo.Title; result.Subtitle = displayInfo.Subtitle;
            // save_song_score: a song counts as cleared unless the course failed out on it.
            result.IsClear = !FailedOut;
            songResults.Add(result);
            var record = new Online.PlayRecord { AudioOffsetMs = Lane.Record.AudioOffsetMs, VisualOffsetMs = Lane.Record.VisualOffsetMs };
            record.Inputs.AddRange(Lane.Record.Inputs);
            songRecords.Add(record);
        }

        void NextSong()
        {
            var finished = Stats();
            Lane.Carry += finished;
            Lane.ComboCarry = Lane.DisplayCombo;
            Lane.KiwamiCarry += Session.KiwamiThreshold;
            Progress.Next();
            BeginSong(SongIndex + 1);
            StartSong();
        }

        // The course is over: 合格 or 不合格 plays over the lane, and the result follows.
        void Finish()
        {
            IsFinished = true;
            songClock.Pause();
            DisableDrumPads();
            pauseButton.interactable = false;
            var total = Progress.Total;
            var result = new DanPlayResult
            {
                Run = Run, Progress = Progress, Verdict = Progress.Verdict(), Songs = songResults.ToArray(), Records = songRecords.ToArray(),
                Score = total.Score, MaxCombo = total.MaxCombo, GaugePoints = Gauge.Points, AutoPlay = autoPlay,
            };
            StartCoroutine(ShowEnding(result));
        }

        IEnumerator ShowEnding(DanPlayResult result)
        {
            if (ending != null)
            {
                ending.BeginDan(result.Verdict > 0);
                double started = GameTimeline.FrameTime, chartTime = ChartTime;
                while (GameTimeline.FrameTime - started < ending.Duration)
                {
                    double elapsed = GameTimeline.FrameTime - started;
                    // A course that failed out keeps its lane still.
                    if (!FailedOut) Lane.ShowVisuals(chartTime + elapsed, chartTime + elapsed - visualOffset, false, false);
                    ending.ShowTime(elapsed);
                    yield return null;
                }
            }
            music.StopAudio();
            // dan_transition is the result screen's to play, as it opens.
            switcher.ShowDanResult(result);
        }

        // ---------------------------------------------------------------- HUD

        void ShowCourse()
        {
            var course = Run.Entry.Course;
            string language = SettingManager.EnsureInstance().Settings.general.Language;
            view.frame.sprite = view.frames[Mathf.Clamp(course.Color, 0, view.frames.Length - 1)];
            view.horiName.text = course.Title(language);
            view.horiName.Squeeze(200);
            // dan_info.lua: the gauge's border plate overhangs its threshold cell by 24 px.
            var gauge = course.Exams.FirstOrDefault(e => e.Type == DanExamType.Gauge);
            view.gaugePlate.gameObject.SetActive(gauge != null && gauge.Course.Red > 0);
            if (view.gaugeCell != null) view.gaugeCell.gameObject.SetActive(view.gaugePlate.gameObject.activeSelf);
            if (gauge != null && gauge.Course.Red > 0)
            {
                int cell = Math.Max(1, Math.Min(SoulGaugeView.Cells, gauge.Course.Red / 2));
                double x = 738 + SoulGaugeView.CellWidth * cell - SoulGaugeView.CellWidth / 2.0 - 1;
                view.gaugePlate.anchoredPosition = new Vector2(Mathf.Max(0, Mathf.Floor((float)(x - 586 + 24) + 0.5f)), view.gaugePlate.anchoredPosition.y);
                // The cabinet's white strip, centred on that cell over the bar (YataiDON ships it but does not draw it).
                if (view.gaugeCell != null) view.gaugeCell.anchoredPosition = new Vector2(Mathf.Floor((float)x - 14 + 0.5f), view.gaugeCell.anchoredPosition.y);
                view.gaugeValue.text = $"{gauge.Course.Red} ％{(gauge.Range == DanExamRange.Less ? "未満" : "以上")}";
            }
            view.between.gameObject.SetActive(false);
        }

        // YataiDON draw_exam_row for each exam other than the gauge, up to three rows.
        void ShowExams()
        {
            var exams = Run.Entry.Course.Exams;
            int slot = 0;
            for (int i = 0; i < exams.Count && slot < view.rows.Length; i++)
            {
                if (exams[i].Type == DanExamType.Gauge) continue;
                ShowRow(view.rows[slot++], exams[i], Progress.View(i, !IsFinished));
            }
            for (; slot < view.rows.Length; slot++) view.rows[slot].root.gameObject.SetActive(false);
        }

        void ShowRow(DanPlayView.ExamRowView row, DanExam exam, DanExamView state)
        {
            row.root.gameObject.SetActive(true);
            bool all = !exam.PerSong;
            row.badge.sprite = view.badges[all ? 0 : 1 + Math.Min(SongIndex, 2)];
            row.frameBackAll.enabled = row.frameFrontAll.enabled = all;
            row.overlay1.enabled = row.overlay2.enabled = !all;
            row.icon.sprite = view.icons[(int)exam.Type];
            row.caption.text = state.HasBorder ? $"{state.Red} {(exam.Range == DanExamRange.Less ? "未満" : "以上")}" : "―";
            float full = all ? 972 : 648;
            if (state.Failed) Fill(row.bar, row.rainbow, view.barFail, null, full, "fail", false);
            else Fill(row.bar, row.rainbow, BarSprite(state.BarState, false), all ? view.rainbowAll : view.rainbow, full * state.Progress, state.BarState, false);
            row.failed.enabled = state.Failed;
            ShowDigits(row.digits, state.Failed || !state.HasBorder ? "" : state.Counter.ToString(), view.valueDigits, 528, 56, false);
            row.percent.enabled = false;
            for (int j = 0; j < row.songs.Length; j++)
            {
                var sub = row.songs[j];
                bool shown = !all;
                sub.root.gameObject.SetActive(shown);
                if (!shown) continue;
                // A song before the one playing: its own bar and count (not reached yet: the stub only).
                bool reached = j < SongIndex && exam.BorderFor(j) != null;
                sub.track.enabled = sub.front.enabled = sub.chip.enabled = reached;
                sub.chip.sprite = view.songChips[Math.Min(j, view.songChips.Length - 1)];
                if (reached) Fill(sub.bar, sub.rainbow, BarSprite(state.SongState[j], true), view.songRainbow, 234 * state.SongProgress[j], state.SongState[j], true);
                else { sub.bar.enabled = false; sub.rainbow.enabled = false; }
                ShowDigits(sub.digits, reached ? state.SongCounter[j].ToString() : "", view.songDigits, 1478, 20, true);
            }
        }

        Sprite BarSprite(string state, bool song)
        {
            switch (state)
            {
                case "up_50": return song ? view.songRed : view.barRed;
                case "up_80": return song ? view.songGold : view.barGold;
                case "down_80": return song ? view.songDown80 : view.barDown80;
                default: return song ? view.songMax : view.barMax;
            }
        }

        // A full bar ("max") is the scrolling rainbow; the others stretch their colour across the width.
        void Fill(Image bar, RawImage rainbow, Sprite sprite, Texture rainbowTexture, float width, string state, bool song)
        {
            bool empty = width <= 0.01f || state == "empty";
            bool rainbowOn = !empty && state == "max" && rainbowTexture != null;
            bar.enabled = !empty && !rainbowOn;
            rainbow.enabled = rainbowOn;
            if (bar.enabled)
            {
                bar.sprite = sprite;
                bar.rectTransform.sizeDelta = new Vector2(width, bar.rectTransform.sizeDelta.y);
            }
            if (rainbowOn)
            {
                rainbow.texture = rainbowTexture;
                float tw = rainbowTexture.width, period = tw / 2;
                float phase = period - (float)(GameTimeline.FrameTime * (period * 60.0 / 80.0) % period);
                rainbow.uvRect = new Rect(phase / tw, 0, width / tw, 1);
                rainbow.rectTransform.sizeDelta = new Vector2(width, rainbow.rectTransform.sizeDelta.y);
            }
        }

        // Digits left-aligned from x (live values), or right-aligned ending at x (song counts).
        static void ShowDigits(Image[] digits, string value, Sprite[] sprites, float x, float margin, bool rightAligned)
        {
            for (int k = 0; k < digits.Length; k++)
            {
                bool on = k < value.Length;
                digits[k].enabled = on;
                if (!on) continue;
                digits[k].sprite = sprites[value[k] - '0'];
                float left = rightAligned ? x - (value.Length - k) * margin : x + k * margin;
                digits[k].rectTransform.anchoredPosition = new Vector2(left, digits[k].rectTransform.anchoredPosition.y);
            }
        }

        void ShowBetweenText(string text, string sub)
        {
            view.betweenTitle.text = text;
            view.betweenSubtitle.text = sub;
            view.betweenTitle.Squeeze(1300);
            view.betweenSubtitle.Squeeze(1300);
        }

        // The doors close over the lane and part again (no black behind them); the song's title is
        // written on the lane under them, so it shows once they have parted, until their animation
        // ends and the song begins.
        void ShowBetween()
        {
            double elapsed = BetweenElapsedMs;
            bool started = !double.IsNaN(BetweenStartedAt);
            bool doors = started && DanBetween.IsShown(elapsed);
            bool title = doors;
            view.between.gameObject.SetActive(doors || title);
            if (!started) return;
            if (!betweenOpened && !IsPaused && elapsed >= DanBetween.OpenMs) { betweenOpened = true; hitAudio.PlayAudioOneShot(fusumaOpen); }
            view.doorLeft.gameObject.SetActive(doors);
            view.doorRight.gameObject.SetActive(doors);
            view.betweenTitle.gameObject.SetActive(title);
            view.betweenSubtitle.gameObject.SetActive(title);
            if (!doors) return;
            double frame = DanBetween.Frame(elapsed);
            float door = (float)DanBetween.DoorX(betweenClip, frame);
            // lane/dan_between_fusuma at x 849 (lane space); the left door is its mirror.
            view.doorRight.anchoredPosition = new Vector2(849 + door - 498, view.doorRight.anchoredPosition.y);
            view.doorLeft.anchoredPosition = new Vector2(849 - door + 720 - 498, view.doorLeft.anchoredPosition.y);
        }

        // ---------------------------------------------------------------- pause

        public void Hit(bool isKa, bool right, bool sound = true) => Lane?.Hit(isKa, right, sound);

        public void TogglePause()
        {
            if (IsFinished || Session == null || !songClock.Started || switcher.IsInputBlocked || closingPauseMenu) return;
            if (IsPaused) { Resume(); return; }
            songClock.Pause();
            IsPaused = true;
            pausedAt = GameTimeline.FrameTime;
            InputManager.DropDelayed(); Lane.ForgetHeard();
            pauseOpenedFrame = Time.frameCount;
            music.StopAudio(); hitAudio.StopAudio();
            DisableDrumPads();
            pauseButton.interactable = false;
            pauseMenu.Show();
        }

        public void Resume()
        {
            if (!IsPaused || closingPauseMenu || switcher.IsInputBlocked || IsFinished) return;
            StartCoroutine(ClosePauseMenu(() =>
            {
                songClock.Resume(GameTimeline.AudioNow);
                IsPaused = false;
                if (!double.IsNaN(pausedAt)) pausedTotal += GameTimeline.FrameTime - pausedAt;
                pausedAt = double.NaN;
                InputManager.DropDelayed(); Lane.ForgetHeard();
                resumeFrame = Time.frameCount;
                StartDueMusic();
                foreach (var pad in pausedPads) if (pad != null) pad.enabled = true;
                pausedPads.Clear();
                pauseButton.interactable = true;
            }));
        }

        IEnumerator ClosePauseMenu(Action completed)
        {
            closingPauseMenu = true;
            yield return pauseMenu.Hide();
            completed();
            closingPauseMenu = false;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && Session != null && !IsPaused && !IsFinished) TogglePause();
        }

        void DisableDrumPads()
        {
            foreach (var pad in FindObjectsByType<DrumPad>(FindObjectsSortMode.None))
            {
                if (pad.gameObject.scene != gameObject.scene || !pad.enabled) continue;
                pausedPads.Add(pad);
                pad.enabled = false;
            }
        }

        // The whole course again, through the loading curtain.
        public void Restart() => LeavePlay(() => switcher.PlayDan(switcher.SelectedDan));
        public void Back() => LeavePlay(() => switcher.SwitchScene(SceneSwitcher.DanSelectScene));

        void LeavePlay(Action action)
        {
            if (closingPauseMenu || switcher.IsInputBlocked) return;
            if (IsPaused && !IsFinished) StartCoroutine(ClosePauseMenu(action));
            else action();
        }

        void PrepareToLeave(string scene)
        {
            songClock.Pause(); IsPaused = true;
            music.StopAudio(); hitAudio.StopAudio();
            DisableDrumPads();
        }

        void OnDestroy()
        {
            InputManager.DrumDelay = 0;
            if (switcher != null) switcher.SceneChanging -= PrepareToLeave;
            Lane?.Release();
            // Songs never reached keep no decoded audio.
            Run?.Release();
        }
    }
}
