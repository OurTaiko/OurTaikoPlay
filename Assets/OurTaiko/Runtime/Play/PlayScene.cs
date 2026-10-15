using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed partial class PlayScene : MonoBehaviour
    {
        public AudioSource music, hitAudio;
        public AudioClip don, ka, balloonPop;
        public HitSoundLibrary hitSounds;
        public ModifierBadgeView modifierBadges;
        // Player::draw: lane/lane_difficulty frame = difficulty (Easy..Ura).
        public UnityEngine.UI.Image laneDifficulty;
        public Sprite[] laneDifficultySprites;
        public RectTransform noteLayer, barLayer, mojiLayer;
        public Sprite[] noteSprites;
        [Tooltip("Nijiiro expression frame 1, indexed by NoteKind like noteSprites; alternates every eighth note at 50 combo.")]
        public Sprite[] alternateNoteSprites;
        public Sprite[] rollBodySprites, rollTailSprites;
        public Sprite balloonTailSprite;
        public Sprite[] mojiSprites;
        public Sprite mojiRollSprite;
        public BalloonCounterView balloonCounter;
        public DrumrollCounterView drumrollCounter;
        public Sprite[] judgmentSprites;
        public UnityEngine.UI.Image judgment;
        public HitFaceView hitFace;
        public HitRingView hitRing;
        public SoulGaugeView soulGauge;
        public NoteArcView noteArcs;
        [Tooltip("drum_don_l/r, drum_kat_l/r; each plays DrumFlash.anim from its own hit.")]
        public UnityEngine.UI.Image[] drumFlashes;
        public ScoreCounterView scoreCounter;
        public ComboView combo;
        public ComboAnnounceView comboAnnounce;
        public JudgeCounterView judgeCounter;
        public TimingFeedbackView timingFeedback;
        [Tooltip("The touch drum; Settings > Play > Enable Drumpad for Single Player Mode turns it on or off.")]
        public DrumPad drumPad;
        public TMP_Text title, subtitle, resultText;
        public BranchLaneView branchLane;
        public GameObject pausePanel, resultPanel;
        public PauseMenuView pauseMenu;
        public UnityEngine.UI.Button pauseButton, restartButton, backButton, resumeButton, resultRestart, resultBack;
        [Tooltip("The backdrop's dancers, on the chart's tempo and the soul gauge.")]
        public DancerTroupeView dancers;
        public GogoView gogo;
        public EndingView ending;

        public PlaySession Session { get; private set; }
        // A drum press still waiting out the input delay must not land on the other side of a pause.
        public bool IsPaused
        {
            get => paused;
            private set { if (paused != value) { InputManager.DropDelayed(); heard.Clear(); } paused = value; }
        }
        bool paused;
        // Drum presses whose sound already played while they wait out the input delay.
        readonly List<InputPress> heard = new List<InputPress>();
        // Drum sounds started so far.
        public int DrumSounds { get; private set; }
        public bool IsFinished { get; private set; }
        public PlayResult Result { get; private set; }
        // Every judged drum press of this play, sent with an online score (scoreReplayVersion 1).
        public Online.PlayRecord Record { get; private set; }
        readonly SongClock songClock = new SongClock();
        public double SongTime => songClock.Time;
        NoteLaneRenderer lane;
        public double RenderedTime => lane?.RenderedTime ?? 0;
        // Views come from pools while a note is on the lane; null when it is not drawn.
        public RectTransform NoteRoot(int index) => lane.NoteRoot(index);
        public RectTransform MojiRoot(int index) => lane.MojiRoot(index);
        public RectTransform BarRoot(int index) => lane.BarRoot(index);
        SongDefinition song;
        double audioOffset, visualOffset, judgeOffset;
        int playedAudioOffsetMs;
        // OpenTaiko judges and draws at a whole millisecond of the play clock (cut toward zero).
        double ChartTime => TjaTime.Truncate(SongTime - audioOffset);
        SongInfo displayInfo;
        bool autoPlay, hitKa, nextAutoRight = true;
        SceneSwitcher switcher;
        double feedbackTime = -10;
        readonly double[] flashedAt = { -10, -10, -10, -10 };
        ClipSampler[] flashClips;
        ClipSampler judgmentFade;
        readonly List<DrumPad> pausedPads = new List<DrumPad>();
        bool closingPauseMenu;
        bool resuming, resumeLostFocus;
        int resumeFrame = -1;
        int lastCombo;
        int pauseOpenedFrame = -1;

        IEnumerator Start()
        {
            switcher = SceneSwitcher.EnsureInstance();
            // Direct Editor runs must return to the same mode after Back and another song.
            switcher.PracticeMode = IsPractice;
            switcher.SceneChanging += PrepareToLeave;
            song = switcher.SelectedSong;
            // Play scenes start from the song list; a direct run without a song goes back to Entry.
            if (song == null)
            {
                Debug.LogWarning("No song is selected; returning to Entry.", this);
                switcher.SwitchScene(SceneSwitcher.MenuScene);
                yield break;
            }
            autoPlay = switcher.AutoPlay;
            var playSettings = SettingManager.EnsureInstance().Settings.play;
            playedAudioOffsetMs = playSettings.audioOffsetMs;
            audioOffset = (song.audioOffsetMs + (double)playSettings.audioOffsetMs) / 1000.0;
            visualOffset = song.visualOffsetMs / 1000.0;
            judgeOffset = autoPlay ? 0 : playSettings.judgeOffsetMs / 1000.0;
            InputManager.DrumDelay = Math.Min(Math.Max(0, playSettings.inputDelayMs), PlaySettings.InputDelayMaxMs) / 1000.0;
            Record = new Online.PlayRecord
            {
                // Replay v1 stores already corrected judgment time. Keep its two-offset convention:
                // visual time = recorded judgment time - VisualOffsetMs (B cancels here).
                AudioOffsetMs = (int)Math.Round((audioOffset + judgeOffset) * 1000),
                VisualOffsetMs = (int)Math.Round((visualOffset - judgeOffset) * 1000),
            };
            pauseButton.onClick.AddListener(TogglePause);
            resumeButton.onClick.AddListener(Resume);
            restartButton.onClick.AddListener(Restart);
            backButton.onClick.AddListener(Back);
            resultRestart.onClick.AddListener(Restart);
            resultBack.onClick.AddListener(Back);
            pausePanel.SetActive(false); resultPanel.SetActive(false); combo.gameObject.SetActive(false); comboAnnounce.Hide();
            // Disabled and hidden together: an inactive pad neither draws nor registers with InputManager,
            // and pause/resume only re-enables the pads it disabled itself.
            if (drumPad != null) drumPad.gameObject.SetActive(SettingManager.EnsureInstance().Settings.play.singlePlayerDrumPad);
            try
            {
                // SongLoadingScene parsed the chart behind the curtain; restarts and direct runs parse here.
                string course = switcher.SelectedCourse;
                var options = PlayOptions.Shared;
                var chart = switcher.TakePreparedChart(song, course) ?? PrepareChart(song, course);
                // Practice plays a fixed route, chosen only in its menu; a normal play evaluates branches.
                practiceBranch = BranchRoute.Normal;
                Session = new PlaySession(chart, judgeOffset, IsPractice ? practiceBranch : (BranchRoute?)null);
                if (modifierBadges != null) modifierBadges.Show(options, autoPlay);
                ShowDifficulty(chart.Course);
                // 音色: hit_sounds/<neiro>/don.ogg and ka.ogg; 無音 leaves both empty.
                if (hitSounds != null) hitSounds.TryGet(options.neiro, out don, out ka);
                balloonCounter.ResetDisplay();
                drumrollCounter.ResetDisplay();
                soulGauge.Initialize(Session.ClearThreshold);
                PutSoulGaugeUnderNotes();
                foreach (string warning in Session.Chart.Warnings) Debug.LogWarning("Ignored TJA command: " + warning);
                Session.Judged += OnJudged;
                if (branchLane != null) branchLane.Initialize(Session.Chart.Branches.Count > 0);
                displayInfo = song.ReadDisplayInfo();
                title.text = displayInfo.Title;
                subtitle.text = $"{displayInfo.Subtitle}    {Session.Chart.Course.ToUpperInvariant()}  LV.{Session.Chart.Level}";
                lane = new NoteLaneRenderer(new NoteLaneRenderer.Assets
                {
                    NoteLayer = noteLayer, BarLayer = barLayer, MojiLayer = mojiLayer,
                    NoteSprites = noteSprites, AlternateNoteSprites = alternateNoteSprites,
                    RollBodySprites = rollBodySprites, RollTailSprites = rollTailSprites, MojiSprites = mojiSprites,
                    BalloonTailSprite = balloonTailSprite, MojiRollSprite = mojiRollSprite,
                }, balloonCounter);
                lane.Prepare(Session);
                music.SetAudioSong(song);
                hitAudio.PrepareAudioEffects(don, ka, balloonPop);
                hitAudio.PrepareAudioEffects(comboAnnounce.voices);
                UpdateHud();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = "CHART COULD NOT LOAD\n<size=22>" + error.Message + "</size>";
            }
            if (IsFinished) yield break;
            while (switcher.IsInputBlocked) yield return null;
            // The full countdown starts only after the global cover has opened.
            songClock.Start(GameTimeline.AudioNow, Math.Max(2, Session.Chart.Offset + 2 - audioOffset - Math.Min(0, judgeOffset)));
            if (IsPractice) InitializePractice();
            else StartDueMusic();
        }

        void ShowDifficulty(string course)
        {
            if (laneDifficulty == null || laneDifficultySprites == null || laneDifficultySprites.Length == 0) return;
            int index = (int)(SongInfo.DifficultyOf(course) ?? Difficulty.Oni);
            laneDifficulty.sprite = laneDifficultySprites[Math.Min(index, laneDifficultySprites.Length - 1)];
        }

        // Player::reset_chart: the play options change the chart before load times are taken.
        public static TaikoChart PrepareChart(SongDefinition song, string course)
        {
            var chart = song.Parse(course);
            ChartModifiers.Apply(chart, PlayOptions.Shared, new System.Random());
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
            songClock.TrackStarted(GameTimeline.AudioNow);
        }

        void Update()
        {
            if (switcher == null || switcher.IsInputBlocked || !songClock.Started) return;
            if (IsFinished) return;
            // Keep the intentional frame-based judgment; input event timestamps only sort hits.
            double? playback = AudioEngine.EnsureInstance().Backend == AudioBackend.BassSimple && music.IsAudioPlaying()
                ? music.AudioPosition() : null;
            songClock.Update(GameTimeline.AudioFrameTime, playback);
            StartDueMusic();
            if (closingPauseMenu || Time.frameCount == resumeFrame || Time.frameCount == pauseOpenedFrame) return;
            if (InputManager.GetKeyDown(InputKey.Back) || InputManager.GetKeyDown(InputKey.Pause))
            { TogglePause(); return; }
            if (!IsPractice && InputManager.GetKeyDown(InputKey.Restart)) { Restart(); return; }
            if (IsPaused)
            {
                if (IsPractice && !pausePanel.activeSelf) UpdatePracticePause();
                else pauseMenu.HandleInput();
                return;
            }
            if (Session == null || IsFinished) return;
            double time = ChartTime;
            Session.Advance(time, autoPlay);
            UpdateBranchLane(time);
            if (!autoPlay) { SoundDrumLeads(); HitFirstDrumPress(); }
            UpdatePlayVisuals(time);
            // The song ends a second after its audio; charts often stretch their last measure far
            // past it (a closing measure at a few BPM). Without audio, the chart's length decides.
            double audio = music.AudioLength();
            if (audio > 0 ? SongTime > audio + 1 : time > Session.Chart.Duration + Math.Max(0, judgeOffset) + 1) Finish();
        }

        // Presentation can keep running after the result is captured, without advancing judgment.
        void UpdatePlayVisuals(double time)
        {
            balloonCounter.ShowTime(time);
            drumrollCounter.ShowTime(time);
            RenderNotes(time - visualOffset);
            soulGauge.ShowTime(time);
            if (noteArcs != null) noteArcs.ShowTime(time);
            SampleDancers(time);
            combo.ShowTime(time);
            comboAnnounce.ShowTime(time);
            judgmentFade ??= judgment.GetComponent<ClipSampler>();
            judgmentFade.Sample(Math.Min(GameTimeline.FrameTime - feedbackTime, judgmentFade.clip.length));
            hitFace.ShowTime(time);
            hitRing.ShowTime(time);
            for (int i = 0; i < drumFlashes.Length; i++) ShowFlash(i);
        }

        // Input mutex: a frame judges only its earliest drum press; later ones in the same frame are dropped.
        void HitFirstDrumPress()
        {
            bool judged = false;
            foreach (var press in InputManager.PressesThisFrame)
            {
                if (!press.Key.IsDrum()) continue;
                bool wasHeard = Forget(press);
                if (judged) continue;
                judged = true;
                Hit(press.Key.IsKa(), press.Key.IsRight(), !wasHeard);
            }
        }
        // The drum sound does not wait for the input delay: a press that will be judged is heard in the
        // frame it arrives, and only its judgment and effects come later.
        void SoundDrumLeads()
        {
            foreach (var press in InputManager.DrumLeadsThisFrame)
            {
                PlayDrum(press.Key.IsKa());
                heard.Add(press);
            }
        }
        bool Forget(InputPress press)
        {
            for (int i = 0; i < heard.Count; i++)
            {
                if (heard[i].Key != press.Key || heard[i].Time != press.Time) continue;
                heard.RemoveAt(i);
                return true;
            }
            return false;
        }
        void PlayDrum(bool isKa)
        {
            var clip = isKa ? ka : don;
            if (clip != null) hitAudio.PlayAudioOneShot(clip, AudioGroup.Drum);
            DrumSounds++;
        }
        public void Hit(bool isKa, bool right, bool sound = true)
        {
            if (Session == null || !songClock.Started || switcher.IsInputBlocked || IsPaused || IsFinished || autoPlay) return;
            if (sound) PlayDrum(isKa);
            Feedback(isKa, right);
            hitKa = isKa;
            double time = ChartTime;
            Record.Inputs.Add(((time - judgeOffset) * 1000, Online.PlayRecord.TypeOf(isKa, right)));
            Session.Hit(isKa, time);
        }
        void Feedback(bool isKa, bool right)
        {
            int flash = (isKa ? 2 : 0) + (right ? 1 : 0);
            flashedAt[flash] = GameTimeline.FrameTime;
            ShowFlash(flash);
        }
        // Real time since that drum's last hit; the clip ends switched off.
        void ShowFlash(int index)
        {
            flashClips ??= Array.ConvertAll(drumFlashes, flash => flash.GetComponent<ClipSampler>());
            var sampler = flashClips[index];
            sampler.Sample(Math.Min(GameTimeline.FrameTime - flashedAt[index], sampler.clip.length));
        }
        void OnJudged(int index, Judgment result)
        {
            if (result != Judgment.Roll)
                soulGauge.SetPoints(Session.GaugePoints, ChartTime);
            if (autoPlay)
            {
                // Alternate actual strikes, including every hit within the same long note.
                PlayDrum(Session.Chart.Notes[index].IsKa);
                Feedback(Session.Chart.Notes[index].IsKa, nextAutoRight);
                nextAutoRight = !nextAutoRight;
            }
            SpawnArc(index, result);
            var judged = Session.Chart.Notes[index];
            bool big = judged.IsBigNote;
            double judgedAt = ChartTime;
            hitFace.Play(result, big, judgedAt);
            hitRing.Play(result, big, judgedAt);
            if (result != Judgment.Roll)
            {
                double? offset = Session.TimingOffsetMs(index);
                if (timingFeedback != null)
                {
                    if (!autoPlay && offset.HasValue) timingFeedback.Show(offset.Value);
                    else timingFeedback.Clear();
                }
                // Long-note hits must not restart the previous normal judgment's text animation.
                feedbackTime = GameTimeline.FrameTime;
                judgment.sprite = judgmentSprites[(int)result - 1];
            }
            else if (Session.Chart.Notes[index].Kind == NoteKind.Balloon)
            {
                var note = Session.Chart.Notes[index];
                balloonCounter.RecordHit(index, note.BalloonHits, Session.LongHits[index], note.EndTime + judgeOffset,
                    ChartTime);
                if (Session.LongHits[index] == note.BalloonHits) hitAudio.PlayAudioOneShot(balloonPop);
            }
            else if (judged.IsDrumroll)
                drumrollCounter.RecordHit(index, Session.LongHits[index], judgedAt);
            UpdateHud();
        }
        // Good/ok notes 1-4 fly as themselves; popped balloons fly as big don. Roll hits use the roll's size
        // and the struck drum's colour (autoplay rolls with don). Kusudama never flies.
        void SpawnArc(int index, Judgment result)
        {
            if (noteArcs == null) return;
            var note = Session.Chart.Notes[index];
            NoteKind kind;
            if (result == Judgment.Good || result == Judgment.Ok) kind = note.Kind;
            else if (result != Judgment.Roll || note.Kind == NoteKind.Kusudama) return;
            else if (note.Kind == NoteKind.Balloon)
            {
                if (Session.LongHits[index] != note.BalloonHits) return;
                kind = NoteKind.BigDon;
            }
            else if (note.Kind == NoteKind.BigRoll)
                kind = hitKa && !autoPlay ? NoteKind.BigKa : NoteKind.BigDon;
            else kind = hitKa && !autoPlay ? NoteKind.Ka : NoteKind.Don;
            // NoteArc's is_big picks the gauge burst's circle; popped balloons fly as big don.
            bool big = kind == NoteKind.BigDon || kind == NoteKind.BigKa;
            // The note leaves the judge where #JPOSSCROLL has it now (user request; OpenTaiko's
            // FlyingNotes start from the fixed skin point).
            var shift = JudgeShift.At(Session.Chart.JudgeMoves, ChartTime);
            noteArcs.Spawn(noteSprites[(int)kind], big, ChartTime, shift.X, shift.Y);
        }
        void UpdateHud()
        {
            scoreCounter.Show(Session.Score, Session.KiwamiThreshold);
            judgeCounter.Show(Session.Good, Session.Ok, Session.Bad, Session.Rolls);
            combo.Show(Session.Combo);
            // Announce at 50, then at each 100th combo, once per combo change.
            if (Session.Combo != lastCombo && (Session.Combo == 50 || Session.Combo > 0 && Session.Combo % 100 == 0))
            {
                var voice = comboAnnounce.Announce(Session.Combo, ChartTime);
                if (voice != null) hitAudio.PlayAudioOneShot(voice, AudioGroup.Voice);
            }
            lastCombo = Session.Combo;
        }
        void UpdateBranchLane(double time)
        {
            if (branchLane == null) return;
            branchLane.Select(Session.DisplayBranchAt(time), time);
            branchLane.ShowTime(time);
        }
        public void TogglePause()
        {
            if (IsFinished || Session == null || !songClock.Started || switcher.IsInputBlocked || closingPauseMenu) return;
            if (IsPractice) { TogglePracticePause(); return; }
            if (IsPaused) { Resume(); return; }
            songClock.Pause();
            IsPaused = true;
            pauseOpenedFrame = Time.frameCount;
            music.StopAudio(); hitAudio.StopAudio();
            DisableDrumPads();
            pauseButton.interactable = false;
            pauseMenu.Show();
        }

        public void Resume()
        {
            if (!IsPaused || closingPauseMenu || switcher.IsInputBlocked || IsFinished) return;
            if (IsPractice) { ClosePracticeMenu(); return; }
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
                songClock.Resume(GameTimeline.AudioNow);
                IsPaused = false;
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

        void DisableDrumPads()
        {
            foreach (var pad in FindObjectsByType<DrumPad>(FindObjectsSortMode.None))
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
        // Practice loops here; completed plays hand their data to ResultScene for persistence.
        void Finish()
        {
            if (IsPractice) { PausePractice(true); return; }
            songClock.Pause(); IsFinished = true; music.StopAudio();
            Result = PlayResult.From(Session, song.ScoreKey, autoPlay, playedAudioOffsetMs);
            Result.Title = displayInfo.Title;
            Result.Subtitle = displayInfo.Subtitle;
            DisableDrumPads();
            pauseButton.interactable = false;
            StartCoroutine(ShowEnding());
        }

        IEnumerator ShowEnding()
        {
            if (ending != null)
            {
                ending.Begin(Result);
                double started = GameTimeline.FrameTime;
                double chartTime = ChartTime;
                while (GameTimeline.FrameTime - started < ending.Duration)
                {
                    double elapsed = GameTimeline.FrameTime - started;
                    // Continue from the last song frame on the ending's real-time clock. The
                    // stopped SongClock and Session remain untouched; dancers, gauge loops and
                    // existing hit effects keep playing while input and scoring stay disabled.
                    UpdatePlayVisuals(chartTime + elapsed);
                    if (branchLane != null) branchLane.ShowTime(chartTime + elapsed);
                    ending.ShowTime(elapsed);
                    yield return null;
                }
                UpdatePlayVisuals(chartTime + ending.Duration);
                ending.ShowTime(ending.Duration);
            }
            switcher.ShowResult(Result, song, Record);
        }
        public void Restart()
        {
            if (IsPractice && !IsFinished)
            {
                if (pausePanel.activeSelf && !closingPauseMenu)
                    StartCoroutine(ClosePauseMenu(() => { RestorePracticePads(); PausePractice(true); }));
                return;
            }
            LeavePlay(() => SceneSwitcher.EnsureInstance().Restart());
        }
        public void Back()
        {
            if (IsPractice && !pausePanel.activeSelf && !IsFinished) return;
            LeavePlay(() => SceneSwitcher.EnsureInstance().SwitchScene(SceneSwitcher.SongSelectScene));
        }

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
            if (Session != null) Session.Judged -= OnJudged;
        }

        // A paused practice previews its routes; while it plays (or rewinds) it hides what the
        // attempt skipped, and browsing measures keeps past notes.
        void RenderNotes(double time)
        {
            bool preview = IsPractice && IsPaused;
            bool hideSkipped = IsPractice && (!IsPaused || practiceRewinding);
            ShiftJudge(time);
            if (lane.Render(Session, time, preview, hideSkipped)) gogo.ShowTime(Session, time, preview);
        }

        // OpenTaiko draws the soul gauge after the lane and before the notes (t進行描画_ゲージ, then
        // t進行描画_チップ), so notes a vertical SCROLL or #JPOSSCROLL carries up pass over it. The
        // rest of the HUD (combo, score, flying notes…) stays above them. The note sprites sort at
        // -1 (bar lines) to 10 (text); the lane's canvas is at -50.
        public const int SoulGaugeOrder = -20;
        void PutSoulGaugeUnderNotes()
        {
            if (soulGauge == null) return;
            var canvas = soulGauge.GetComponent<Canvas>();
            if (canvas == null) canvas = soulGauge.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SoulGaugeOrder;
        }

        // What #JPOSSCROLL moves with the note origin, as OpenTaiko adds GetJPOSCROLLX/Y to them:
        // the judge frame, judgment text, hit effects, GOGO fire and balloon counter, and the notes
        // flying to the soul gauge leave from the moved judge (SpawnArc). The lane and its GOGO tint stay.
        RectTransform[] judgeFollowers;
        Vector2[] judgeHomes;
        void ShiftJudge(double time)
        {
            if (judgeFollowers == null)
            {
                var lane = noteLayer.parent.parent;
                judgeFollowers = new[] { lane.Find("JudgeCircle") as RectTransform, judgment != null ? judgment.rectTransform : null,
                        hitFace != null ? (RectTransform)hitFace.transform : null, hitRing != null ? (RectTransform)hitRing.transform : null,
                        gogo != null && gogo.fire != null ? gogo.fire.rectTransform : null,
                        balloonCounter != null ? (RectTransform)balloonCounter.transform : null }
                    .Where(t => t != null).ToArray();
                judgeHomes = judgeFollowers.Select(t => t.anchoredPosition).ToArray();
            }
            var shift = JudgeShift.At(Session.Chart.JudgeMoves, time);
            var offset = new Vector2((float)shift.X, -(float)shift.Y);
            for (int i = 0; i < judgeFollowers.Length; i++) judgeFollowers[i].anchoredPosition = judgeHomes[i] + offset;
        }

        void SampleDancers(double time)
            => dancers.ShowTime(time, Session.BpmAt(time), Session.GaugePoints / SoulGauge.MaximumPoints, Session.IsClear);
    }
}
