using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro result screen (scenes/result.cpp + Scripts/result/*.lua), one ResultBoard per player:
    // 1P's board on the left, 2P's on the right after a two-player play (result_2p.cpp). The 3D Don,
    // score-rank clip and option icons are not ported. The screen is saved in the scene
    // (ResultView); Awake binds it and fills in the run, and the sprites below are the per-run art.
    public sealed class ResultScene : MonoBehaviour
    {
        public RectTransform stage;
        public ResultView view;
        public ArcadeOverlayArt overlay;

        [Header("Board")]
        public Sprite[] difficulties;
        public Sprite[] judgeDigits, scoreDigits;

        [Header("Gauge")]
        public Sprite[] unfilled;   // easy, normal, hard art
        public Sprite[] unfilled2P; // 2p_unfilled_*: 2P's board
        public Sprite[] overlays;   // easy, normal, hard art
        public Sprite[] rainbow;    // 8 cells per art, easy / normal / hard
        public Sprite clearCaption, clearCaptionDark, soul, soulDark;
        public Sprite[] soulFire;

        [Header("Crown and message")]
        public Sprite[] crowns;     // clear, full combo, donderful combo
        public Sprite[] gleam, messages;
        [Tooltip("crown/message 4..7: 2P's right-hand bubbles.")]
        public Sprite[] messages2P;

        [Header("High score")]
        public Sprite[] highScoreDigits;

        [Header("Timelines")]
        public TextAsset backgroundTimeline;
        public TextAsset fujiTimeline, successTimeline, crownTimeline, crownLoopTimeline, messageTimeline,
            judgeDigitTimeline, scoreDigitTimeline, fireTimeline, rainbowTimeline, highScoreTimeline;

        [Header("Audio")]
        public AudioSource bgm;
        public AudioSource loop, sfx, voice;
        public AudioClip rankSound;
        public AudioClip don, donBig, countStop, countLoop, achieve, atmosClear, crownSilver, crownGold, crownRainbow, highScoreVoice, fullComboVoice;
        public AudioClip[] messageVoices; // miss, near, success, perfect
        [Header("2P audio")]
        public AudioClip highScoreVoice2P, fullComboVoice2P;
        public AudioClip[] messageVoices2P;

        public const float GaugeScale = 0.7f, GaugeUnit = 21 * GaugeScale;
        public static readonly string[] MessageTexts = { "もう少し\nがんばるドン!", "おしかったドン", "ノリノリだドン", "よくできたドン！" };
        // per gauge art (easy, normal, hard): the first clear cell and the クリア caption's x
        public static readonly int[] ClearCell = { 30, 35, 40 };
        public static readonly float[] ClearCaptionX = { 518, 585, 663 };
        internal static readonly (string Track, int Frame)[] RainbowLeaves = {
            ("#15@0", 0), ("#17@1", 1), ("#19@2", 2), ("#21@3", 3), ("#23@4", 4), ("#25@5", 5), ("#27@6", 6), ("#29@7", 7), ("#15@8", 0) };
        internal static readonly (string Track, int Frame)[] FireLeaves = {
            ("#40@0", 0), ("#44@0", 1), ("#46@0", 2), ("#48@0", 3), ("#50@0", 4), ("#52@0", 5), ("#54@0", 6), ("#56@0", 7) };
        internal static readonly (string Track, int Frame)[] ShineLeaves = { ("#65@1", 0), ("#67@1", 1), ("#69@1", 2) };

        // 1P's run; Boards[1] holds 2P's after a two-player play.
        public PlayResult Result { get; private set; }
        public ResultSequence Sequence => boards[0].Sequence;
        public IReadOnlyList<ResultBoard> Boards => boards;
        public bool IsLeaving { get; private set; }
        public CoinOverlayView Coins { get; private set; }

        SceneSwitcher switcher;
        internal LumenClip SuccessClip, CrownClip, CrownLoop, MessageClip, JudgePop, ScorePop, FireClip, RainbowClip, HighScoreClip;
        LumenClip bgClip;
        ResultBackground background, fadeIn;
        readonly List<ResultBoard> boards = new List<ResultBoard>();
        double sceneStart, revealStart = -1;
        int countLoops;
        // The frame each seat last acted on (1P and 2P can turn their own pages in the same frame).
        readonly int[] lastActionFrame = { -1, -1 };

        double Clock => GameTimeline.FrameTime * 1000;
        double Reveal => revealStart < 0 ? -1 : Clock - revealStart;

        void Awake()
        {
            switcher = SceneSwitcher.EnsureInstance();
            Result = switcher.LastResult ?? Placeholder();
            // A two-player result needs 2P's board in the scene; without it only 1P's is shown.
            var result2P = view.secondBoard != null ? switcher.LastResult2P : null;
            SaveScores();
            bgClip = Clip(backgroundTimeline); SuccessClip = Clip(successTimeline); CrownClip = Clip(crownTimeline);
            CrownLoop = Clip(crownLoopTimeline); MessageClip = Clip(messageTimeline); JudgePop = Clip(judgeDigitTimeline);
            ScorePop = Clip(scoreDigitTimeline); FireClip = Clip(fireTimeline); RainbowClip = Clip(rainbowTimeline);
            HighScoreClip = Clip(highScoreTimeline);
            sceneStart = Clock;
            Bind();
            boards.Add(new ResultBoard(this, 0, view, Result, result2P != null));
            if (view.secondBoard != null) view.secondBoard.gameObject.SetActive(result2P != null);
            if (result2P != null) boards.Add(new ResultBoard(this, 1, view.secondBoard, result2P, true));
            if (view.secondAnalysis != null)
            {
                view.secondAnalysis.gameObject.SetActive(result2P != null);
                if (result2P != null) BindAnalysis(view.secondAnalysis, result2P, 1);
                else view.secondAnalysis.detailPage.gameObject.SetActive(false);
            }
            // 2P's page controls take the bottom right, where the credit line sits.
            if (result2P != null && view.freePlay != null) view.freePlay.gameObject.SetActive(false);
            switcher.SceneChanging += OnSceneChanging;
        }

        // Only entering ResultScene commits a completed play. Practice never enters this scene.
        // Consume before saving so scene reloads and Editor previews cannot submit a run twice.
        void SaveScores()
        {
            if (!switcher.TakeCompletedPlay(out var song, out var record, out var record2P)) return;
            SaveScore(song, Result, record, 0);
            if (switcher.LastResult2P != null) SaveScore(song, switcher.LastResult2P, record2P, 1);
        }

        // Local charts keep every seat's best in the one score database. Online, each seat uploads
        // its own course and block with its own account; a seat without a login is skipped (2P has
        // none yet). A play from the offline cache is queued for the seat's account on that server
        // and goes up once the server connects again.
        internal static void SaveScore(SongDefinition song, PlayResult result, Online.PlayRecord record, int seat)
        {
            if (result.AutoPlay) return;
            if (!SongScores.IsOnline(song)) { ScoreStore.Shared.Save(result); return; }
            var online = Online.OnlineManager.Instance;
            var chart = online != null ? online.ChartOf(song) : null;
            if (chart == null) return;
            int difficulty = (int)result.Difficulty;
            var best = online.Client.Best(chart, difficulty, seat, result.Side);
            if (best != null) result.PreviousBest = (int)Math.Min(int.MaxValue, Math.Max(result.PreviousBest, best.Score));
            online.Submit(chart, difficulty, new Online.FanmadeScore
            {
                Good = result.Good, Ok = result.Ok, Bad = result.Bad, Score = result.Score,
                Drumroll = result.Rolls, MaxCombo = result.MaxCombo, ClearStatus = (int)result.StoredCrown,
            }, record, seat, result.Side);
        }


        static LumenClip Clip(TextAsset asset) => asset != null ? LumenClip.Parse(asset.text) : LumenClip.Empty;

        // Opening the Result scene on its own shows a sample failed run instead of an empty board.
        static PlayResult Placeholder() => new PlayResult
        {
            Title = "TRIPLE HELIX", Subtitle = "Yonokid", Course = "Oni", Difficulty = Difficulty.Oni, Level = 9,
            Score = 754320, Good = 612, Ok = 98, Bad = 47, MaxCombo = 233, Rolls = 41, GaugePoints = 6420, AutoPlay = true,
        };

        void Start()
        {
            bgm.SetAudioGroup(AudioGroup.Bgm);
            voice.SetAudioGroup(AudioGroup.Voice);
            sfx.PrepareAudioEffects(don, donBig, countStop, achieve, atmosClear, crownSilver, crownGold, crownRainbow, rankSound);
            voice.PrepareAudioTracks(highScoreVoice, fullComboVoice, highScoreVoice2P, fullComboVoice2P);
            voice.PrepareAudioTracks(messageVoices);
            if (messageVoices2P != null) voice.PrepareAudioTracks(messageVoices2P);
            loop.PrepareAudioTracks(countLoop);
            bgm.loop = true;
            bgm.PlayAudio();
        }

        void OnDestroy()
        {
            if (switcher != null) switcher.SceneChanging -= OnSceneChanging;
        }

        void OnSceneChanging(string scene)
        {
            IsLeaving = true;
            loop.StopAudio();
        }

        void Update()
        {
            double clock = Clock;
            // The reveal clock starts once the global cover has opened, like the play scene.
            if (revealStart < 0 && !switcher.IsInputBlocked) revealStart = clock;
            // The sky clears with whichever board cleared first.
            float clear = 0;
            foreach (var board in boards)
            {
                float own = board.Clear(clock);
                board.ShowSuccess(own);
                clear = Math.Max(clear, own);
            }
            background.Draw(clock - sceneStart, clear);
            if (revealStart < 0) { fadeIn.Group.alpha = 1; fadeIn.Draw(clock - sceneStart, 0); return; }

            double now = Reveal;
            foreach (var board in boards) board.Advance(now, clock);
            HandleInput();
            if (view.analysis != null) view.analysis.SetReady(CanAdvance);
            if (boards.Count > 1 && view.secondAnalysis != null) view.secondAnalysis.SetReady(CanAdvance);

            // result animation 15: the backdrop drawn on top, faded out after 100 ms over 316.67 ms.
            float cover = 1 - (float)Math.Min(1, Math.Max(0, (now - ResultSequence.FadeInDelayMs) / ResultSequence.FadeInMs));
            fadeIn.Group.alpha = cover;
            fadeIn.Group.gameObject.SetActive(cover > 0);
            if (cover > 0) fadeIn.Draw(clock - sceneStart, clear);
            foreach (var board in boards) board.Draw(now);
        }

        // Result2PScreen::reveal_end_ms: the screen settles once every board has.
        bool RevealEnded => boards.All(board => board.Sequence.RevealEndMs > 0);
        bool CanAdvance => boards.All(board => board.Sequence.CanAdvance);

        // Each seat's first drum press of the frame: its ka turns its own board's pages, a don from
        // either seat skips or leaves.
        void HandleInput()
        {
            for (int seat = 0; seat < InputManager.Players.Count; seat++)
                foreach (var press in InputManager.Players[seat].PressesThisFrame)
                {
                    if (press.Key.IsKa()) ChangePage(seat, press.Key.IsRight() ? 1 : -1);
                    else Don(seat);
                    break;
                }
        }

        ResultAnalysisView AnalysisOf(int seat) => seat == 0 ? view.analysis : boards.Count > 1 ? view.secondAnalysis : null;

        // ResultScreen::handle_input: a don press skips the reveal, or leaves once it has settled.
        public void Don() => Don(0);

        void Don(int seat)
        {
            if (!CanAct(seat)) return;
            lastActionFrame[0] = lastActionFrame[1] = Time.frameCount;
            if (!RevealEnded)
            {
                if (!SkipAll()) return;
                sfx.PlayAudioOneShot(this.don);
                sfx.PlayAudioOneShot(donBig);
                return;
            }
            if (!CanAdvance) return;
            sfx.PlayAudioOneShot(this.don);
            Leave();
        }

        bool CanAct(int seat) => !IsLeaving && !switcher.IsInputBlocked && revealStart >= 0
            && Sequence.FadeInFinished && lastActionFrame[seat] != Time.frameCount;

        // Lands every board's reveal at once; false when none could skip.
        bool SkipAll()
        {
            bool skipped = false;
            foreach (var board in boards) skipped |= board.Skip(Reveal, Clock);
            if (skipped) { countLoops = 0; loop.StopAudio(); }
            return skipped;
        }

        public void ChangePage(int direction) => ChangePage(0, direction);
        public void ChangePage(int seat, int direction) => ShowPage(seat, !(AnalysisOf(seat)?.Details ?? false), direction);

        void AdjustTiming(int seat, bool audio)
        {
            var analysis = AnalysisOf(seat);
            if (!CanAct(seat) || analysis == null || !analysis.ApplyOffset(audio)) return;
            lastActionFrame[seat] = Time.frameCount;
            sfx.PlayAudioOneShot(don);
        }

        public void ShowPage(bool details, int direction = 1) => ShowPage(0, details, direction);

        // One board's score / timing-detail pages; the first turn also lands every reveal.
        public void ShowPage(int seat, bool details, int direction = 1)
        {
            var analysis = AnalysisOf(seat);
            if (!CanAct(seat) || analysis == null || analysis.IsTransitioning || analysis.Details == details) return;
            lastActionFrame[seat] = Time.frameCount;
            if (!RevealEnded && !SkipAll()) return;
            analysis.SetPage(details, direction);
        }

        void Leave()
        {
            if (IsLeaving) return;
            IsLeaving = true;
            switcher.ReturnToMenu();
        }

        // A board's cue: shared sounds, and that seat's voices (the 2p voices for 2P).
        internal void PlayCue(ResultCue cue, ResultBoard board)
        {
            bool second = board.Seat == 1;
            switch (cue)
            {
                // Two boards count at once on the one loop: it stops when the last one lands.
                case ResultCue.CountLoopStart:
                    if (countLoops++ == 0) { loop.clip = countLoop; loop.loop = true; loop.PlayAudio(); }
                    break;
                case ResultCue.CountLoopStop: if (--countLoops <= 0) { countLoops = 0; loop.StopAudio(); } break;
                case ResultCue.AchieveSoul: sfx.PlayAudioOneShot(achieve); break;
                case ResultCue.RowLanded: sfx.PlayAudioOneShot(countStop); break;
                case ResultCue.ScoreLanded: sfx.PlayAudioOneShot(donBig); break;
                case ResultCue.HighScore: PlayVoice(second ? highScoreVoice2P : highScoreVoice); break;
                case ResultCue.ScoreRank: sfx.PlayAudioOneShot(rankSound); break;
                case ResultCue.Crown:
                    var crown = board.Result.ResultCrown;
                    sfx.PlayAudioOneShot(crown == Crown.DonderfulCombo ? crownRainbow : crown == Crown.FullCombo ? crownGold : crownSilver);
                    if (crown == Crown.FullCombo || crown == Crown.DonderfulCombo) PlayVoice(second ? fullComboVoice2P : fullComboVoice);
                    break;
                case ResultCue.Message:
                    var voices = second && messageVoices2P != null && messageVoices2P.Length > 0 ? messageVoices2P : messageVoices;
                    PlayVoice(voices[(int)board.Result.Message]);
                    break;
                case ResultCue.SuccessBackground: sfx.PlayAudioOneShot(atmosClear); break;
            }
        }

        void PlayVoice(AudioClip clip)
        {
            if (clip == null) return;
            voice.StopAudio();
            voice.clip = clip;
            voice.PlayAudio();
        }

        // ---------------------------------------------------------------- binding

        // The shared screen: backdrop, song title and number, credit line and the analysis pages (1P's).
        void Bind()
        {
            background = new ResultBackground(view.background, bgClip, Clip(fujiTimeline));
            fadeIn = new ResultBackground(view.fadeIn, bgClip, Clip(fujiTimeline));
            view.songTitle.text = Result.Title;
            view.songTitle.Squeeze(view.songTitle.rectTransform.sizeDelta.x);
            view.songNumber.text = Math.Max(1, switcher.SongsPlayed) + "曲目";

            // ResultScreen::draw_overlay: coin_overlay's credit line (result shows no chip or invite), over the wipe.
            if (overlay != null) Coins = new CoinOverlayView(overlay, view.freePlay, null, null, null, null);
            if (view.analysis != null) BindAnalysis(view.analysis, Result, 0);
        }

        // A board's pages: its own result, page controls and timing adjustments.
        void BindAnalysis(ResultAnalysisView analysis, PlayResult result, int seat)
        {
            analysis.Bind(result);
            analysis.action.Clicked = Don;
            analysis.previous.Clicked = () => ChangePage(seat, -1);
            analysis.next.Clicked = () => ChangePage(seat, 1);
            analysis.scoreDot.Clicked = () => ShowPage(seat, false, -1);
            analysis.detailDot.Clicked = () => ShowPage(seat, true, 1);
            analysis.swipeArea.Swiped = direction => ChangePage(seat, direction);
            if (analysis.adjustAudio != null)
            {
                analysis.adjustAudio.Clicked = () => AdjustTiming(seat, true);
                analysis.adjustJudge.Clicked = () => AdjustTiming(seat, false);
                analysis.adjustAudio.Swiped = analysis.adjustJudge.Swiped = direction => ChangePage(seat, direction);
            }
        }
    }
}
