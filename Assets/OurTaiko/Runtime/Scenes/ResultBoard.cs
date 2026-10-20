using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // One player's reveal on its ResultBoardView (YataiDON's ResultPlayer): its ResultSequence, the
    // rows and score counting in, the gauge filling, high score, rank, crown and message. ResultScene
    // owns the backdrop, input and audio; a two-player result runs two boards side by side.
    public sealed class ResultBoard
    {
        public int Seat { get; }
        public PlayResult Result { get; }
        public ResultSequence Sequence { get; }
        public ResultBoardView View { get; }
        // When the clear backdrop started fading in (ResultCue.SuccessBackground); -1 before.
        public double SuccessAt { get; private set; } = -1;

        readonly ResultScene scene;
        readonly int art;
        readonly bool twoPlayers;
        Image successImage, unfilledImage, barImage, transitionImage, topImage, bottomImage, overlayImage, captionImage, soulImage, fireImage, sheenImage;
        readonly Image[] rainbowImages = new Image[2];
        Image[][] judgeImages;
        Image[] scoreOutline, scoreFill, highScoreNumber;
        RectTransform highScoreGroup;
        CanvasGroup highScoreAlpha;
        Vector2 highScoreBase, crownBase;
        Image crownImage, crownGhost, burstA, burstB, stars, shine, messageImage;
        TextMeshProUGUI messageText;

        public ResultBoard(ResultScene scene, int seat, ResultBoardView view, PlayResult result, bool twoPlayers = false)
        {
            this.twoPlayers = twoPlayers;
            this.scene = scene; Seat = seat; View = view; Result = result;
            Sequence = new ResultSequence(result);
            int difficulty = Math.Min(Math.Max((int)result.Difficulty, 0), 3);
            art = difficulty == 0 ? 0 : difficulty == 3 ? 2 : 1;
            Bind();
            if (view.scoreRank != null) view.scoreRank.Show(0);
        }

        // How far the clear backdrop has faded in (anim crear_1P_l), 0 for a failed gauge.
        public float Clear(double clock)
        {
            if (Result.GaugeState == GaugeState.Failed || SuccessAt < 0) return 0;
            var clip = scene.SuccessClip;
            return (float)clip.Get("crear_1P_l", clip.Label("success").GetValueOrDefault(5) + (clock - SuccessAt) * 0.06, "a", 1);
        }

        public void ShowSuccess(float clear) => successImage.Alpha(clear);

        public void Advance(double now, double clock)
        {
            foreach (var (cue, row) in Sequence.Update(now)) Play(cue, row, clock);
        }

        // ResultScreen::handle_input's skip: lands everything at once.
        public bool Skip(double now, double clock)
        {
            if (!Sequence.Skip()) return false;
            foreach (var (cue, row) in Sequence.Update(now)) Play(cue, row, clock);
            return true;
        }

        public void Draw(double now)
        {
            DrawRows(now);
            DrawScore(now);
            DrawGauge(now);
            DrawHighScore(now);
            if (View.scoreRank != null)
                View.scoreRank.Show(Sequence.RankAtMs.HasValue && now >= Sequence.RankAtMs.Value
                    ? Result.Rank : 0, Result.Difficulty,
                    Sequence.Skipped ? -1 : (now - Sequence.RankAtMs.GetValueOrDefault()) / 1000);
            DrawCrown(now);
            DrawMessage(now);
        }

        void Play(ResultCue cue, int row, double clock)
        {
            if (cue == ResultCue.SuccessBackground) SuccessAt = clock;
            scene.PlayCue(cue, this);
        }

        // ---------------------------------------------------------------- drawing

        static float Pop(LumenClip clip, string track, double? landMs, double now)
        {
            if (!landMs.HasValue) return 1;
            double f = clip.First + (now - landMs.Value) * 0.06;
            return f > clip.Last ? 1 : (float)clip.Get(track, f, "sx", 1);
        }

        void DrawRows(double now)
        {
            int[] values = { Result.Good, Result.Ok, Result.Bad, Result.Rolls, Result.MaxCombo };
            for (int row = 0; row < values.Length; row++)
            {
                bool landed = Sequence.RowsLanded > row;
                float scale = landed ? Pop(scene.JudgePop, "#147@0", Sequence.RowLandMs[row], now) : 1;
                Digits(judgeImages[row], scene.judgeDigits, landed ? values[row] : -1, scale, 0);
            }
        }

        void DrawScore(double now)
        {
            bool shown = Sequence.FadeInFinished && Sequence.ScoreLandMs.HasValue;
            float scale = shown ? Pop(scene.ScorePop, "#225@0", Sequence.ScoreLandMs, now) : 1;
            // total_score_back_mc (outlines, frames 0..9) under total_score_mc (fills, 10..19).
            Digits(scoreOutline, scene.scoreDigits, shown ? Result.Score : -1, scale, 0);
            Digits(scoreFill, scene.scoreDigits, shown ? Result.Score : -1, scale, 10);
        }

        static void Digits(Image[] images, Sprite[] sprites, int value, float scale, int offset)
        {
            string text = value < 0 ? "" : value.ToString();
            for (int i = 0; i < images.Length; i++)
            {
                bool on = i < text.Length;
                images[i].enabled = on;
                if (!on) continue;
                images[i].sprite = sprites[text[text.Length - 1 - i] - '0' + offset];
                images[i].rectTransform.localScale = new Vector3(scale, scale, 1);
            }
        }

        void DrawGauge(double now)
        {
            bool started = Sequence.GaugeStarted;
            foreach (var image in new[] { unfilledImage, overlayImage, captionImage, soulImage })
                image.enabled = started;
            if (!started)
            {
                foreach (var image in new[] { barImage, transitionImage, topImage, bottomImage, fireImage, sheenImage, rainbowImages[0], rainbowImages[1] }) image.enabled = false;
                return;
            }
            int shown = Sequence.GaugeShown, clearCell = ResultScene.ClearCell[art];
            bool cleared = Result.GaugeState != GaugeState.Failed, full = Result.GaugeState == GaugeState.Full && shown >= PlayResult.GaugeCells;
            barImage.enabled = !full && shown > 0;
            if (barImage.enabled) Width(barImage, Math.Min(shown, clearCell - 1) * ResultScene.GaugeUnit);
            transitionImage.enabled = !full && shown >= clearCell - 1 && cleared;
            topImage.enabled = bottomImage.enabled = !full && shown > clearCell && cleared;
            if (topImage.enabled) { Width(topImage, (shown - clearCell) * ResultScene.GaugeUnit); Width(bottomImage, (shown - clearCell) * ResultScene.GaugeUnit); }
            DrawRainbow(full, now);

            bool lit = shown >= clearCell - 1 && cleared;
            captionImage.sprite = lit ? scene.clearCaption : scene.clearCaptionDark;
            soulImage.sprite = lit ? scene.soul : scene.soulDark;
            // tamashii_gage fever: flame cel, fade-in and sheen sampled from anim/fever_fire.
            fireImage.enabled = sheenImage.enabled = false;
            var fireClip = scene.FireClip;
            if (lit && full && Sequence.RainbowStartMs.HasValue)
            {
                double start = fireClip.Label("fever_start").GetValueOrDefault(1), loopAt = fireClip.Label("fever").GetValueOrDefault(10), end = fireClip.Label("fever_end").GetValueOrDefault(34);
                double f = start + (now - Sequence.RainbowStartMs.Value) * 0.06;
                if (f >= loopAt) f = loopAt + (f - loopAt) % (end - loopAt);
                int fi = (int)Math.Floor(f);
                foreach (var (track, frame) in ResultScene.FireLeaves)
                {
                    if (!fireClip.Window(track, out double first, out double last) || fi < first || fi > last) continue;
                    fireImage.sprite = scene.soulFire[frame];
                    fireImage.Alpha((float)fireClip.Get(track, f, "a", 1));
                    double sheen = fi < loopAt ? fireClip.Get("#39@2", f, "a", 0)
                        : (fi - loopAt) % 4 < 2 ? fireClip.Get("#39@2", (loopAt + end) / 2, "a", 0.5) : 0;
                    sheenImage.Alpha((float)sheen);
                    break;
                }
            }
        }

        void DrawRainbow(bool full, double now)
        {
            rainbowImages[0].enabled = rainbowImages[1].enabled = false;
            if (!full || !Sequence.RainbowStartMs.HasValue) return;
            // anim/gauge_rainbow: each cel holds while the next fades in over 5 frames.
            var rainbowClip = scene.RainbowClip;
            double length = rainbowClip.Last - rainbowClip.First;
            double f = (now - Sequence.RainbowStartMs.Value) * 0.06 % Math.Max(1, length);
            int fi = (int)Math.Floor(f), used = 0;
            foreach (var (track, frame) in ResultScene.RainbowLeaves)
            {
                if (used == 2 || !rainbowClip.Window(track, out double first, out double last) || fi < first || fi > last) continue;
                float alpha = (float)rainbowClip.Get(track, f, "a", 0);
                if (alpha <= 0) continue;
                rainbowImages[used].sprite = scene.rainbow[art * 8 + frame];
                rainbowImages[used].Alpha(alpha);
                used++;
            }
        }

        static void Width(Image image, float width)
            => image.rectTransform.sizeDelta = new Vector2(width, image.rectTransform.sizeDelta.y);

        void DrawHighScore(double now)
        {
            highScoreGroup.gameObject.SetActive(Sequence.HighScoreAtMs.HasValue);
            if (!Sequence.HighScoreAtMs.HasValue) return;
            // best_score_mc 'start': drop in, rebound, overshoot and settle (track #196@0).
            var clip = scene.HighScoreClip;
            double start = clip.Label("start").GetValueOrDefault(5);
            double f = Math.Min(start + (now - Sequence.HighScoreAtMs.Value) * 0.06, Math.Max(start, clip.Last));
            highScoreGroup.anchoredPosition = highScoreBase - new Vector2(0, (float)clip.Get("#196@0", f, "ty", 0));
            highScoreAlpha.alpha = (float)clip.Get("#196@0", f, "a", 1);
        }

        void DrawCrown(double now)
        {
            var crown = Result.ResultCrown;
            bool show = Sequence.CrownAtMs.HasValue && now >= Sequence.CrownAtMs.Value && Result.GaugeState != GaugeState.Failed;
            foreach (var image in new[] { crownImage, crownGhost, burstA, burstB, stars, shine }) image.enabled = false;
            if (!show) return;
            // crown_mc, sampled from its 'start' label on the 60 fps container clock.
            var crownClip = scene.CrownClip;
            double t = (now - Sequence.CrownAtMs.Value) * 0.06;
            double f = crownClip.Label("start").GetValueOrDefault(5) + t, end = crownClip.Label("end").GetValueOrDefault(120);
            crownImage.sprite = scene.crowns[Math.Max(0, (int)crown - 1)];
            Show(crownImage, crownClip.Get("crown", f, "sx", 1), crownClip.Get("crown", f, "a", 1));
            if (t >= 13 && t <= 42) Show(crownGhost, crownClip.Get("crown_effect", f, "sx", 1), crownClip.Get("crown_effect", f, "a", 0));
            if (t >= 13 && t <= 49)
            {
                double a = crownClip.Get("#38@1", f, "a", 0);
                Show(burstA, crownClip.Get("#38@1", f, "sx", 0.1), a);
                Show(burstB, crownClip.Get("#38@0", f, "sx", 0.1), a);
            }
            if (t >= 17 && t <= 119)
            {
                Show(stars, crownClip.Get("#50@4", f, "sx", 0.5), Math.Min(1, Math.Max(0, crownClip.Get("#50@4", f, "a", 1))));
                stars.rectTransform.anchoredPosition = crownBase + new Vector2((float)crownClip.Get("#50@4", f, "tx", 30.2), -(float)crownClip.Get("#50@4", f, "ty", -2.2));
            }
            if (f >= end - 5)
            {
                // crown_loop: a three-step shine sweep, then 171 idle frames.
                var crownLoop = scene.CrownLoop;
                double k = (f - (end - 5)) % Math.Max(1, crownLoop.Last - crownLoop.First + 1);
                foreach (var (track, frame) in ResultScene.ShineLeaves)
                    if (crownLoop.Window(track, out double first, out double last) && Math.Floor(k) >= first && Math.Floor(k) <= last)
                    {
                        shine.sprite = scene.gleam[frame];
                        Show(shine, 1, 1);
                    }
            }
        }

        static void Show(Image image, double scale, double alpha)
        {
            image.rectTransform.localScale = new Vector3((float)scale, (float)scale, 1);
            image.Alpha((float)alpha);
        }

        void DrawMessage(double now)
        {
            bool show = Sequence.RevealEndMs > 0;
            messageImage.enabled = messageText.enabled = show;
            if (!show) return;
            // msg_L_mc: half size to a 1.2 overshoot in 66.7 ms, settled at 150 ms.
            var messageClip = scene.MessageClip;
            double f = Math.Min(messageClip.Label("success").GetValueOrDefault(35) + (now - Sequence.RevealEndMs) * 0.06, messageClip.Last);
            float scale = (float)messageClip.Get("#277@0", f, "sx", 1), alpha = (float)messageClip.Get("#277@0", f, "a", 1);
            Show(messageImage, scale, alpha);
            messageText.rectTransform.localScale = new Vector3(scale, scale, 1);
            messageText.alpha = alpha;
        }

        // ---------------------------------------------------------------- binding

        // Fills the saved board with this run. The clear marks are saved for view.gaugeArt and
        // shifted by the clear-cell difference for the run's gauge art. 2P uses the 2p gauge art
        // and the right-hand message bubbles (crown/message frames 4..7).
        void Bind()
        {
            var view = View;
            successImage = twoPlayers && view.successTwoPlayers != null ? view.successTwoPlayers : view.success;
            if (view.successTwoPlayers != null) (successImage == view.success ? view.successTwoPlayers : view.success).Alpha(0);
            view.difficulty.sprite = scene.difficulties[Math.Min(Math.Max((int)Result.Difficulty, 0), scene.difficulties.Length - 1)];
            judgeImages = new Image[view.judgeRows.Length][];
            for (int row = 0; row < judgeImages.Length; row++) judgeImages[row] = view.judgeRows[row].digits;
            scoreOutline = view.scoreOutline;
            scoreFill = view.scoreFill;

            highScoreGroup = view.highScore;
            highScoreAlpha = view.highScoreGroup;
            highScoreBase = highScoreGroup.anchoredPosition;
            highScoreNumber = view.highScoreDigits;
            string difference = Result.ScoreDifference.ToString();
            for (int i = 0; i < highScoreNumber.Length; i++)
            {
                highScoreNumber[i].enabled = i < difference.Length;
                if (i < difference.Length) highScoreNumber[i].sprite = scene.highScoreDigits[difference[difference.Length - 1 - i] - '0'];
            }
            highScoreGroup.gameObject.SetActive(false);

            crownImage = view.crown; crownGhost = view.crownFade;
            burstA = view.burstA; burstB = view.burstB; stars = view.stars; shine = view.shine;
            crownBase = crownImage.rectTransform.anchoredPosition;
            messageImage = view.message;
            var messages = Seat == 1 && scene.messages2P != null && scene.messages2P.Length > 0 ? scene.messages2P : scene.messages;
            messageImage.sprite = messages[(int)Result.Message];
            messageText = view.messageText;
            messageText.text = ResultScene.MessageTexts[(int)Result.Message];

            unfilledImage = view.unfilled;
            var unfilled = Seat == 1 && scene.unfilled2P != null && scene.unfilled2P.Length > 0 ? scene.unfilled2P : scene.unfilled;
            unfilledImage.sprite = unfilled[art];
            overlayImage = view.overlay;
            overlayImage.sprite = scene.overlays[art];
            barImage = view.bar;
            transitionImage = view.clearTransition;
            topImage = view.clearTop;
            bottomImage = view.clearBottom;
            int savedArt = Math.Min(Math.Max(view.gaugeArt, 0), 2);
            float shift = (ResultScene.ClearCell[art] - ResultScene.ClearCell[savedArt]) * ResultScene.GaugeUnit;
            foreach (var image in new[] { transitionImage, topImage, bottomImage })
                image.rectTransform.anchoredPosition += new Vector2(shift, 0);
            rainbowImages[0] = view.rainbow[0];
            rainbowImages[1] = view.rainbow[1];
            captionImage = view.clearCaption;
            captionImage.rectTransform.anchoredPosition += new Vector2(ResultScene.ClearCaptionX[art] - ResultScene.ClearCaptionX[savedArt], 0);
            fireImage = view.soulFire;
            soulImage = view.soul;
            sheenImage = view.soulSheen;
        }
    }
}
