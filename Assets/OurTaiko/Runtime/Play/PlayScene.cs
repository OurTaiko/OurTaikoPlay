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
        public AudioClip don, ka;
        public RectTransform noteLayer, barLayer;
        public Sprite[] noteSprites;
        public Sprite[] judgmentSprites;
        public UnityEngine.UI.Image judgment, hitFlash, gaugeFill;
        public UnityEngine.UI.Image[] drumFlashes;
        public TMP_Text title, subtitle, score, combo, counters, state, rollCounter, resultText, branchInfo;
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
                foreach (string warning in Session.Chart.Warnings) Debug.LogWarning("Ignored TJA command: " + warning);
                Session.Judged += OnJudged;
                Session.BranchSelected += OnBranchSelected;
                if (branchInfo != null)
                {
                    branchInfo.transform.parent.gameObject.SetActive(Session.Chart.Branches.Count > 0);
                    ShowBranch(BranchRoute.Normal);
                }
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
            if (!autoPlay && keyboard != null)
            {
                if (keyboard.fKey.wasPressedThisFrame) Hit(false, false);
                if (keyboard.jKey.wasPressedThisFrame) Hit(false, true);
                if (keyboard.dKey.wasPressedThisFrame) Hit(true, false);
                if (keyboard.kKey.wasPressedThisFrame) Hit(true, true);
            }
            RenderNotes(time - song.visualOffsetMs / 1000.0);
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
            else { rollCounter.text = "DRUMROLL  " + Session.Rolls; }
            UpdateHud();
        }
        void UpdateHud()
        {
            score.text = Session.Score.ToString("D7");
            combo.text = Session.Combo >= 2 ? $"{Session.Combo}\n<size=20>COMBO</size>" : "";
            counters.text = $"GOOD {Session.Good}     OK {Session.Ok}     BAD {Session.Bad}     ROLL {Session.Rolls}";
            gaugeFill.fillAmount = (float)Session.Gauge;
        }
        void OnBranchSelected(ChartBranch branch, BranchRoute route) => ShowBranch(route);
        void ShowBranch(BranchRoute route)
        {
            if (branchInfo == null) return;
            branchInfo.text = "BRANCH " + route.ToString().ToUpperInvariant();
            branchInfo.color = route == BranchRoute.Master ? new Color32(255, 170, 255, 255)
                : route == BranchRoute.Expert ? new Color32(110, 220, 255, 255) : Color.white;
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
            string clear = Session.Gauge >= 0.8 ? "CLEAR!" : "FINISHED";
            resultText.text = $"{clear}\n<size=48>{Session.Score:N0}</size>\n<size=24>GOOD {Session.Good}   OK {Session.Ok}   BAD {Session.Bad}\nMAX COMBO {Session.MaxCombo}   DRUMROLL {Session.Rolls}</size>";
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
                var root = Rect(note.Kind.ToString(), noteLayer, 128, 128);
                var view = new NoteView { Root = root, Object = root.gameObject };
                if (note.IsLong && !note.IsBalloon)
                {
                    view.Body = Rect("RollBody", root, 1, 28);
                    view.Body.anchorMin = view.Body.anchorMax = new Vector2(0.5f, 0.5f);
                    view.Body.pivot = new Vector2(0, 0.5f);
                    Image(view.Body, null).color = new Color(1, 0.72f, 0.05f);
                }
                var head = Rect("Head", root, 128, 128); head.anchorMin = head.anchorMax = new Vector2(0.5f, 0.5f);
                Image(head, noteSprites[(int)note.Kind]); notes.Add(view);
                root.gameObject.SetActive(false);
            }
            foreach (var bar in Session.Chart.Bars)
            {
                var root = Rect("Measure", barLayer, bar.IsBranchStart ? 4 : 2, 126);
                Image(root, null).color = bar.IsBranchStart ? new Color(1, 0.8f, 0.2f, 0.8f) : new Color(1, 1, 1, 0.35f);
                bars.Add(root); root.gameObject.SetActive(false);
            }
        }
        // LaneClip begins at x=332; the skin judge is x=414, y=256 (lane y=184).
        const float JudgeLocalX = 82, JudgeLocalY = -72;
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
                bool visible = Session.IsActive(note) && !Session.Resolved[i] && pos.x + Math.Max(0, length) >= -128 && pos.x + Math.Min(0, length) <= 1100;
                view.Object.SetActive(visible);
                if (visible)
                {
                    view.Root.anchoredPosition = pos;
                    if (view.Body != null) { view.Body.sizeDelta = new Vector2(Mathf.Abs(length), 28); view.Body.localScale = new Vector3(Mathf.Sign(length), 1, 1); }
                    if (note.Gogo && note.Time - time < 1) gogo = true;
                    if (note.IsBalloon && time >= note.Time) rollCounter.text = "BALLOON  " + Math.Max(0, note.BalloonHits - Session.LongHits[i]);
                }
            }
            for (int i = 0; i < bars.Count; i++)
            {
                var pos = Position(Session.Chart.Bars[i], time); bars[i].anchoredPosition = pos;
                bars[i].gameObject.SetActive(Session.Chart.Bars[i].Display && Session.IsActive(Session.Chart.Bars[i]) && pos.x >= 0 && pos.x < 1000);
            }
            gogoTint.alpha = gogo ? 0.18f + Mathf.Sin((float)time * 12) * 0.05f : 0;
        }
    }
}
