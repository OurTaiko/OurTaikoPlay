using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed class PlayScene : MonoBehaviour
    {
        public SongDefinition defaultSong;
        public AudioSource music, hitAudio;
        public AudioClip don, ka, balloonPop;
        public HitSoundLibrary hitSounds;
        public ModifierBadgeView modifierBadges;
        public RectTransform noteLayer, barLayer;
        public Sprite[] noteSprites;
        public Sprite[] rollBodySprites, rollTailSprites;
        public Sprite balloonTailSprite;
        public BalloonCounterView balloonCounter;
        public Sprite[] judgmentSprites;
        public UnityEngine.UI.Image judgment, hitFlash;
        public SoulGaugeView soulGauge;
        public UnityEngine.UI.Image[] drumFlashes;
        public TMP_Text title, subtitle, score, combo, counters, state, rollCounter, resultText;
        public BranchLaneView branchLane;
        public GameObject pausePanel, resultPanel;
        public UnityEngine.UI.Button pauseButton, restartButton, backButton, resumeButton, resultRestart, resultBack;
        public SpriteFlipbook[] dancers;
        public CanvasGroup gogoTint;

        public PlaySession Session { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsFinished { get; private set; }
        public PlayResult Result { get; private set; }
        public double SongTime => !isReady ? -2 : IsPaused || IsFinished ? frozenTime : AudioSettings.dspTime - startDsp;
        public double RenderedTime { get; private set; }
        SongDefinition song;
        bool autoPlay, isReady;
        SceneSwitcher switcher;
        double startDsp, frozenTime;
        float feedbackTime = -10, drumTime = -10;
        readonly List<NoteView> notes = new List<NoteView>();
        readonly List<RectTransform> bars = new List<RectTransform>();

        sealed class NoteView
        {
            public RectTransform Root, Body, Tail;
            public float TailAspect;
            public UnityEngine.UI.Image BalloonTail;
            public GameObject Object;
        }

        IEnumerator Start()
        {
            switcher = SceneSwitcher.EnsureInstance();
            switcher.SceneChanging += PrepareToLeave;
            song = switcher.SelectedSong != null ? switcher.SelectedSong : defaultSong;
            autoPlay = switcher.AutoPlay;
            pauseButton.onClick.AddListener(TogglePause);
            resumeButton.onClick.AddListener(TogglePause);
            restartButton.onClick.AddListener(Restart);
            backButton.onClick.AddListener(Back);
            resultRestart.onClick.AddListener(Restart);
            resultBack.onClick.AddListener(Back);
            pausePanel.SetActive(false); resultPanel.SetActive(false);
            try
            {
                // Player::reset_chart: the play options change the chart before load times are taken.
                var options = PlayOptions.Shared;
                var chart = song.Parse(switcher.SelectedSong != null ? switcher.SelectedCourse : null);
                ChartModifiers.Apply(chart, options, new System.Random());
                Session = new PlaySession(chart);
                if (modifierBadges != null) modifierBadges.Show(options);
                // 音色: hit_sounds/<neiro>/don.ogg and ka.ogg; 無音 leaves both empty.
                if (hitSounds != null) hitSounds.TryGet(options.neiro, out don, out ka);
                balloonCounter.ResetDisplay();
                soulGauge.Initialize(Session.ClearThreshold);
                foreach (string warning in Session.Chart.Warnings) Debug.LogWarning("Ignored TJA command: " + warning);
                Session.Judged += OnJudged;
                Session.BranchSelected += OnBranchSelected;
                if (branchLane != null) branchLane.Initialize(Session.Chart.Branches.Count > 0);
                title.text = Session.Chart.Title;
                subtitle.text = $"{Session.Chart.Subtitle}    {Session.Chart.Course.ToUpperInvariant()}  LV.{Session.Chart.Level}";
                CreateNotes();
                music.clip = song.music;
                UpdateHud();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = "CHART COULD NOT LOAD\n<size=22>" + error.Message + "</size>";
            }
            if (IsFinished) yield break;
            // The clip decompresses on load; do it behind the global cover, not on the first PlayScheduled.
            foreach (var clip in new[] { music.clip, don, ka })
                if (clip != null && clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            while (switcher.IsInputBlocked) yield return null;
            // Start the full countdown and DSP clock only after the global cover has opened.
            startDsp = AudioSettings.dspTime + Math.Max(2, Session.Chart.Offset + 2);
            isReady = true;
            ScheduleMusic();
        }

        void ScheduleMusic()
        {
            music.Stop();
            if (music.clip == null) return;
            double time = AudioSettings.dspTime - startDsp;
            if (time >= music.clip.length) return;
            if (time < 0) { music.timeSamples = 0; music.PlayScheduled(startDsp); }
            else
            {
                // Schedule both the clip position and chart origin against the same DSP clock.
                const double lead = 0.05;
                double resumeAt = time + lead;
                if (resumeAt >= music.clip.length) return;
                music.timeSamples = Math.Min(music.clip.samples - 1, (int)(resumeAt * music.clip.frequency));
                music.PlayScheduled(AudioSettings.dspTime + lead);
            }
        }

        void Update()
        {
            if (switcher == null || switcher.IsInputBlocked || !isReady) return;
            if (InputManager.GetKeyDown(InputKey.Back)) { Back(); return; }
            if (InputManager.GetKeyDown(InputKey.Restart)) { Restart(); return; }
            if (InputManager.GetKeyDown(InputKey.Pause)) TogglePause();
            if (Session == null || IsPaused || IsFinished) return;
            double time = SongTime - song.audioOffsetMs / 1000.0;
            Session.Advance(time, autoPlay);
            if (branchLane != null) branchLane.ShowTime(time);
            if (!autoPlay) HitFirstDrumPress();
            balloonCounter.ShowTime(time);
            RenderNotes(time - song.visualOffsetMs / 1000.0);
            soulGauge.ShowTime(time);
            foreach (var dancer in dancers) dancer.ShowTime(time);
            float feedback = Mathf.Clamp01(1 - (Time.unscaledTime - feedbackTime) / 0.25f);
            judgment.color = new Color(1, 1, 1, feedback);
            hitFlash.color = new Color(1, 0.8f, 0.2f, feedback * 0.8f);
            foreach (var flash in drumFlashes)
                if (Time.unscaledTime - drumTime > 0.12f) flash.enabled = false;
            state.text = SongTime < 0 ? $"READY  {Math.Ceiling(-SongTime)}" : autoPlay ? "AUTO PLAY" : "1 PLAYER";
            if (time > Math.Max(Session.Chart.Duration, song.music != null ? song.music.length : 0) + 1) Finish();
        }

        // Input mutex: a frame judges only its earliest drum press; later ones in the same frame are dropped.
        void HitFirstDrumPress()
        {
            foreach (var press in InputManager.PressesThisFrame)
            {
                if (!press.Key.IsDrum()) continue;
                Hit(press.Key.IsKa(), press.Key.IsRight());
                return;
            }
        }
        public void Hit(bool isKa, bool right)
        {
            if (Session == null || !isReady || switcher.IsInputBlocked || IsPaused || IsFinished || autoPlay) return;
            Feedback(isKa, right);
            Session.Hit(isKa, SongTime - song.audioOffsetMs / 1000.0);
        }
        void Feedback(bool isKa, bool right)
        {
            var clip = isKa ? ka : don;
            if (clip != null) hitAudio.PlayOneShot(clip);
            drumFlashes[(isKa ? 2 : 0) + (right ? 1 : 0)].enabled = true;
            drumTime = Time.unscaledTime;
        }
        void OnJudged(int index, Judgment result)
        {
            if (result != Judgment.Roll)
                soulGauge.SetPoints(Session.GaugePoints, SongTime - song.audioOffsetMs / 1000.0);
            if (autoPlay) Feedback(Session.Chart.Notes[index].IsKa, (index & 1) != 0);
            feedbackTime = Time.unscaledTime;
            if (result != Judgment.Roll) judgment.sprite = judgmentSprites[(int)result - 1];
            else if (Session.Chart.Notes[index].Kind == NoteKind.Balloon)
            {
                var note = Session.Chart.Notes[index];
                balloonCounter.RecordHit(index, note.BalloonHits, Session.LongHits[index], note.EndTime,
                    SongTime - song.audioOffsetMs / 1000.0);
                if (Session.LongHits[index] == note.BalloonHits) hitAudio.PlayOneShot(balloonPop);
                rollCounter.text = "";
            }
            else { rollCounter.text = "DRUMROLL  " + Session.Rolls; }
            UpdateHud();
        }
        void UpdateHud()
        {
            score.text = Session.Score.ToString("D7");
            combo.text = Session.Combo >= 2 ? $"{Session.Combo}\n<size=30>COMBO</size>" : "";
            counters.text = $"GOOD {Session.Good}     OK {Session.Ok}     BAD {Session.Bad}     ROLL {Session.Rolls}";
        }
        void OnBranchSelected(ChartBranch branch, BranchRoute route)
        {
            if (branchLane != null) branchLane.Select(route, SongTime - song.audioOffsetMs / 1000.0);
        }
        public void TogglePause()
        {
            if (IsFinished || Session == null || !isReady || switcher.IsInputBlocked) return;
            if (!IsPaused) { frozenTime = SongTime; IsPaused = true; music.Stop(); }
            else { startDsp = AudioSettings.dspTime - frozenTime; IsPaused = false; ScheduleMusic(); }
            pausePanel.SetActive(IsPaused);
        }
        void OnApplicationFocus(bool focused) { if (!focused && Session != null && !IsPaused && !IsFinished) TogglePause(); }
        // GameScreen::end_song: store the record, then hand the result to the Result scene.
        void Finish()
        {
            frozenTime = SongTime; IsFinished = true; music.Stop();
            Result = PlayResult.From(Session, song.name, autoPlay);
            ScoreStore.Shared.Save(Result);
            switcher.ShowResult(Result);
        }
        public void Restart() => SceneSwitcher.EnsureInstance().Restart();
        public void Back() => SceneSwitcher.EnsureInstance().ReturnToMenu();
        void PrepareToLeave(string scene)
        {
            frozenTime = SongTime; IsPaused = true;
            music.Stop(); hitAudio.Stop();
        }
        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= PrepareToLeave;
            if (Session != null) { Session.Judged -= OnJudged; Session.BranchSelected -= OnBranchSelected; }
        }

        static RectTransform Rect(string name, Transform parent, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(width, height); return r;
        }
        static UnityEngine.UI.Image Image(RectTransform r, Sprite sprite)
        {
            var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        void CreateNotes()
        {
            foreach (var note in Session.Chart.Notes)
            {
                var root = Rect(note.Kind.ToString(), noteLayer, 192, 192);
                var view = new NoteView { Root = root, Object = root.gameObject };
                if (note.IsLong && !note.IsBalloon)
                {
                    int size = note.Kind == NoteKind.BigRoll ? 1 : 0;
                    view.Body = Rect("RollBody", root, 0, 0);
                    view.Body.anchorMin = new Vector2(0.5f, 0);
                    view.Body.anchorMax = new Vector2(0.5f, 1);
                    view.Body.pivot = new Vector2(0, 0.5f);
                    Image(view.Body, rollBodySprites[size]);
                    view.Tail = Rect("RollTail", root, 0, 0);
                    view.Tail.anchorMin = view.Body.anchorMin;
                    view.Tail.anchorMax = view.Body.anchorMax;
                    view.Tail.pivot = view.Body.pivot;
                    Image(view.Tail, rollTailSprites[size]);
                    view.TailAspect = rollTailSprites[size].rect.width / rollTailSprites[size].rect.height;
                }
                var head = Rect("Head", root, 0, 0);
                // Match draw_balloon's balloon_offset as a fraction of the note width.
                // Stretch anchors keep the face aligned when the note or Canvas scales.
                float faceOffset = note.Kind == NoteKind.Balloon ? 12f / 128f : 0;
                head.anchorMin = new Vector2(-faceOffset, 0);
                head.anchorMax = new Vector2(1 - faceOffset, 1);
                Image(head, noteSprites[(int)note.Kind]); notes.Add(view);
                if (note.Kind == NoteKind.Balloon)
                {
                    // notes/10 joins the right edge of notes/7 in draw_balloon.
                    var tail = Rect("BalloonTail", root, 0, 0);
                    tail.anchorMin = new Vector2(1 - faceOffset, 0);
                    tail.anchorMax = new Vector2(2 - faceOffset, 1);
                    view.BalloonTail = Image(tail, balloonTailSprite);
                }
                root.gameObject.SetActive(false);
            }
            foreach (var bar in Session.Chart.Bars)
            {
                var root = Rect("Measure", barLayer, bar.IsBranchStart ? 6 : 3, 200);
                Image(root, null).color = bar.IsBranchStart ? new Color(1, 0.8f, 0.2f, 0.8f) : new Color(1, 1, 1, 0.35f);
                bars.Add(root); root.gameObject.SetActive(false);
            }
        }
        // Nijiiro: lane x=498/y=276, judge x=618, note top=14 with 192-pixel sprites.
        const float JudgeLocalX = 120, JudgeLocalY = -110;
        double TravelDistance => noteLayer.rect.width - JudgeLocalX;

        Vector2 Position(ChartNote note, double time)
        {
            double x = NoteScroll.DistanceFromJudge(note.Time, time, note.Bpm, note.ScrollX, TravelDistance);
            double y = NoteScroll.DistanceFromJudge(note.Time, time, note.Bpm, note.ScrollY, TravelDistance);
            return new Vector2(JudgeLocalX + (float)x, JudgeLocalY - (float)y);
        }
        void RenderNotes(double time)
        {
            RenderedTime = time;
            bool gogo = false;
            for (int i = 0; i < notes.Count; i++)
            {
                var note = Session.Chart.Notes[i]; var view = notes[i]; var pos = Position(note, time);
                if (note.IsBalloon && time >= note.Time) pos = new Vector2(JudgeLocalX, JudgeLocalY);
                float length = note.IsLong && !note.IsBalloon ? (float)NoteScroll.RollLength(note, TravelDistance) : 0;
                // ドロン hides the notes; they are still judged. Like draw_note_buffer,
                // only a hit removes a note: a 5/6 roll resolved at its tail and a note
                // missed by timeout keep scrolling until they leave the lane.
                bool rolling = note.IsLong && !note.IsBalloon;
                bool visible = note.Display && Session.IsActive(note) && (rolling || Session.Missed[i] || !Session.Resolved[i]) && InLane(pos.x, Reach(view, length));
                view.Object.SetActive(visible);
                if (visible)
                {
                    view.Root.anchoredPosition = pos;
                    if (view.BalloonTail != null) view.BalloonTail.enabled = balloonCounter.NoteIndex != i;
                    if (view.Body != null)
                    {
                        // draw_drumroll adds the strip's native width to length +
                        // drumroll_width_offset: (48 - 47) / 128 of the note height.
                        float overlap = view.Root.rect.height / 128f;
                        float direction = length < 0 ? -1 : 1;
                        view.Body.sizeDelta = new Vector2(Mathf.Abs(length) + overlap, 0);
                        view.Body.localScale = new Vector3(direction, 1, 1);
                        view.Tail.anchoredPosition = new Vector2(length, 0);
                        view.Tail.sizeDelta = new Vector2(view.Root.rect.height * view.TailAspect, 0);
                        view.Tail.localScale = view.Body.localScale;
                    }
                    if (note.Gogo && note.Time - time < 1) gogo = true;
                    if (note.Kind == NoteKind.Kusudama && time >= note.Time) rollCounter.text = "BALLOON  " + Math.Max(0, note.BalloonHits - Session.LongHits[i]);
                }
            }
            for (int i = 0; i < bars.Count; i++)
            {
                var pos = Position(Session.Chart.Bars[i], time); pos.y -= 4; bars[i].anchoredPosition = pos;
                float half = bars[i].rect.width / 2;
                bars[i].gameObject.SetActive(Session.Chart.Bars[i].Display && Session.IsActive(Session.Chart.Bars[i]) && InLane(pos.x, new Vector2(-half, half)));
            }
            gogoTint.alpha = gogo ? 0.18f + Mathf.Sin((float)time * 12) * 0.05f : 0;
        }

        // The lane clip mask is the visible area, in Canvas units that follow the
        // window resolution, so cull against its live rect instead of fixed pixels.
        bool InLane(float x, Vector2 reach) => x + reach.y >= 0 && x + reach.x <= noteLayer.rect.width;

        // Horizontal extent of a note's sprites relative to its centre, from their current sizes.
        static Vector2 Reach(NoteView view, float length)
        {
            float width = view.Root.rect.width, half = width / 2;
            if (view.BalloonTail != null)
            {
                // The face shifts left by 12/128 of the width; notes/10 follows it.
                float face = width * 12f / 128f;
                return new Vector2(-half - face, half + width - face);
            }
            if (view.Body == null) return new Vector2(-half, half);
            float tail = view.Root.rect.height * view.TailAspect;
            return length >= 0 ? new Vector2(-half, Math.Max(half, length + tail)) : new Vector2(Math.Min(-half, length - tail), half);
        }
    }
}
