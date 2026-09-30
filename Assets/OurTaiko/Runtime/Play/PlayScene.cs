using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OurTaiko
{
    public sealed class PlayScene : MonoBehaviour
    {
        public SongDefinition defaultSong;
        public AudioSource music, hitAudio;
        public AudioClip don, ka, balloonPop;
        public RectTransform noteLayer, barLayer;
        public Sprite[] noteSprites;
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
        public double SongTime => IsPaused || IsFinished ? frozenTime : AudioSettings.dspTime - startDsp;
        public double RenderedTime { get; private set; }
        SongDefinition song;
        bool autoPlay;
        double startDsp, frozenTime;
        float feedbackTime = -10, drumTime = -10;
        readonly List<NoteView> notes = new List<NoteView>();
        readonly List<RectTransform> bars = new List<RectTransform>();

        sealed class NoteView
        {
            public RectTransform Root, Body;
            public UnityEngine.UI.Image BalloonTail;
            public GameObject Object;
        }

        void Start()
        {
            var switcher = SceneSwitcher.EnsureInstance();
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
                Session = new PlaySession(song.Parse());
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
                startDsp = AudioSettings.dspTime + Math.Max(2, Session.Chart.Offset + 2);
                ScheduleMusic();
                UpdateHud();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                IsFinished = true; resultPanel.SetActive(true);
                resultText.text = "CHART COULD NOT LOAD\n<size=22>" + error.Message + "</size>";
            }
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
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { Back(); return; }
                if (keyboard.f1Key.wasPressedThisFrame) { Restart(); return; }
                if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
            }
            if (Session == null || IsPaused || IsFinished) return;
            double time = SongTime - song.audioOffsetMs / 1000.0;
            Session.Advance(time, autoPlay);
            if (branchLane != null) branchLane.ShowTime(time);
            if (!autoPlay && keyboard != null)
            {
                if (keyboard.fKey.wasPressedThisFrame) Hit(false, false);
                if (keyboard.jKey.wasPressedThisFrame) Hit(false, true);
                if (keyboard.dKey.wasPressedThisFrame) Hit(true, false);
                if (keyboard.kKey.wasPressedThisFrame) Hit(true, true);
            }
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

        public void Hit(bool isKa, bool right)
        {
            if (Session == null || IsPaused || IsFinished || autoPlay) return;
            Feedback(isKa, right);
            Session.Hit(isKa, SongTime - song.audioOffsetMs / 1000.0);
        }
        void Feedback(bool isKa, bool right)
        {
            hitAudio.PlayOneShot(isKa ? ka : don);
            drumFlashes[(isKa ? 2 : 0) + (right ? 1 : 0)].enabled = true;
            drumTime = Time.unscaledTime;
        }
        void OnJudged(int index, Judgment result)
        {
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
            soulGauge.SetValue(Session.Gauge, SongTime - song.audioOffsetMs / 1000.0);
        }
        void OnBranchSelected(ChartBranch branch, BranchRoute route)
        {
            if (branchLane != null) branchLane.Select(route, SongTime - song.audioOffsetMs / 1000.0);
        }
        public void TogglePause()
        {
            if (IsFinished || Session == null) return;
            if (!IsPaused) { frozenTime = SongTime; IsPaused = true; music.Stop(); }
            else { startDsp = AudioSettings.dspTime - frozenTime; IsPaused = false; ScheduleMusic(); }
            pausePanel.SetActive(IsPaused);
        }
        void OnApplicationFocus(bool focused) { if (!focused && Session != null && !IsPaused && !IsFinished) TogglePause(); }
        void Finish()
        {
            frozenTime = SongTime; IsFinished = true; music.Stop(); resultPanel.SetActive(true);
            string clear = Session.Gauge >= Session.ClearThreshold ? "CLEAR!" : "FINISHED";
            resultText.text = $"{clear}\n<size=72>{Session.Score:N0}</size>\n<size=36>GOOD {Session.Good}   OK {Session.Ok}   BAD {Session.Bad}\nMAX COMBO {Session.MaxCombo}   DRUMROLL {Session.Rolls}</size>";
        }
        public void Restart() { music.Stop(); SceneSwitcher.EnsureInstance().Restart(); }
        public void Back() { music.Stop(); SceneSwitcher.EnsureInstance().ReturnToMenu(); }
        void OnDestroy() { if (Session != null) { Session.Judged -= OnJudged; Session.BranchSelected -= OnBranchSelected; } }

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
                    view.Body = Rect("RollBody", root, 1, 42);
                    view.Body.anchorMin = view.Body.anchorMax = new Vector2(0.5f, 0.5f);
                    view.Body.pivot = new Vector2(0, 0.5f);
                    Image(view.Body, null).color = new Color(1, 0.72f, 0.05f);
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
                bool visible = Session.IsActive(note) && !Session.Resolved[i] && pos.x + Math.Max(0, length) >= -192 && pos.x + Math.Min(0, length) <= 1650;
                view.Object.SetActive(visible);
                if (visible)
                {
                    view.Root.anchoredPosition = pos;
                    if (view.BalloonTail != null) view.BalloonTail.enabled = balloonCounter.NoteIndex != i;
                    if (view.Body != null) { view.Body.sizeDelta = new Vector2(Mathf.Abs(length), 42); view.Body.localScale = new Vector3(Mathf.Sign(length), 1, 1); }
                    if (note.Gogo && note.Time - time < 1) gogo = true;
                    if (note.Kind == NoteKind.Kusudama && time >= note.Time) rollCounter.text = "BALLOON  " + Math.Max(0, note.BalloonHits - Session.LongHits[i]);
                }
            }
            for (int i = 0; i < bars.Count; i++)
            {
                var pos = Position(Session.Chart.Bars[i], time); pos.y -= 4; bars[i].anchoredPosition = pos;
                bars[i].gameObject.SetActive(Session.Chart.Bars[i].Display && Session.IsActive(Session.Chart.Bars[i]) && pos.x >= 0 && pos.x < 1500);
            }
            gogoTint.alpha = gogo ? 0.18f + Mathf.Sin((float)time * 12) * 0.05f : 0;
        }
    }
}
