using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed class ResultAnalysisView : MonoBehaviour
    {
        public CanvasGroup scorePage, detailPage;
        public TimingHistogramGraphic histogram;
        public TextMeshProUGUI mean, deviation, absolute, counts, sides, empty, pageName, actionLabel, meanLegend;
        public TextMeshProUGUI[] yLabels;
        public UnityEngine.UI.Image[] dots;
        public ResultPointerControl previous, next, scoreDot, detailDot, action, swipeArea;
        public CanvasGroup offsetControls;
        public ResultPointerControl adjustAudio, adjustJudge;
        public TextMeshProUGUI audioRecommendation, judgeRecommendation, calibrationStatus;
        [Tooltip("Where the pages rest: 960 for 1P's, 1916 for 2P's on the right half.")]
        public float pageX = 960;
        PlayResult boundResult;
        string calibrationError;
        public bool Details { get; private set; }
        double changedAt = double.NegativeInfinity;
        int direction = 1;
        const double TransitionDuration = .16;
        public bool IsTransitioning => GameTimeline.FrameTime - changedAt < TransitionDuration;
        public void Bind(PlayResult result)
        {
            boundResult = result;
            calibrationError = null;
            var data = result.AutoPlay ? null : result.Timing;
            histogram.Show(data);
            string Number(double? value) => value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "—";
            mean.text = data?.MeanMs is double m ? m.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms" : "—";
            deviation.text = Number(data?.StandardDeviationMs);
            absolute.text = Number(data?.MeanAbsoluteMs);
            counts.text = L10n.Format("Result", "analysis.counts", data?.Count ?? 0, data?.Misses ?? 0)
                + (result.CustomJudge ? "    " + L10n.Get("Result", "analysis.customJudge") : "");
            sides.text = L10n.Format("Result", "analysis.sides", data?.Early ?? 0, data?.Late ?? 0)
                + (data?.Exact > 0 ? "    " + L10n.Format("Result", "analysis.exact", data.Exact) : "");
            empty.text = result.AutoPlay ? L10n.Get("Result", "analysis.autoPlay") : data == null || data.Count == 0 ? L10n.Get("Result", "analysis.noHits") : "";
            meanLegend.text = data?.MeanMs is double average
                ? L10n.Get("Result", "analysis.meanLegend") + " · " + L10n.Get("Result", average < 0 ? "analysis.leanEarly" : average > 0 ? "analysis.leanLate" : "analysis.centered")
                : L10n.Get("Result", "analysis.meanLegend");
            for (int i = 0; i < yLabels.Length; i++) yLabels[i].text = (histogram.AxisMaximum * i / 4).ToString();
            Details = false; changedAt = double.NegativeInfinity;
            DrawPages();
        }
        public bool ApplyOffset(bool audio)
        {
            if (!Details || IsTransitioning || boundResult == null || boundResult.AutoPlay) return false;
            var manager = SettingManager.EnsureInstance();
            try
            {
                calibrationError = null;
                bool applied = boundResult.Calibration?.TryApply(manager.Settings, audio, manager.Set) == true;
                DrawCalibration();
                return applied;
            }
            catch (Exception error)
            {
                calibrationError = L10n.Get("Result", "calibration.saveFailed");
                Log.Warn("Result", "Could not save timing adjustment: " + error.Message);
                DrawCalibration();
                return false;
            }
        }
        static string Milliseconds(int value) => value.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " ms";
        void DrawCalibration()
        {
            if (offsetControls == null) return;
            var calibration = boundResult?.AutoPlay == false ? boundResult.Calibration : null;
            var current = SettingManager.Instance != null ? SettingManager.Instance.Settings.play : new PlaySettings();
            bool canAudio = calibration?.CanApply(current, true) == true;
            bool canJudge = calibration?.CanApply(current, false) == true;
            string Recommendation(int value, int? target) => target.HasValue ? Milliseconds(value) + " → " + Milliseconds(target.Value) : "—";
            audioRecommendation.text = calibration == null ? "—" : calibration.AudioApplied ? L10n.Format("Result", "calibration.adjusted", Milliseconds(calibration.RecommendedAudioMs.Value)) : Recommendation(calibration.AudioOffsetMs, calibration.RecommendedAudioMs);
            judgeRecommendation.text = calibration == null ? "—" : calibration.JudgeApplied ? L10n.Format("Result", "calibration.adjusted", Milliseconds(calibration.RecommendedJudgeMs.Value)) : Recommendation(calibration.JudgeOffsetMs, calibration.RecommendedJudgeMs);
            adjustAudio.GetComponent<CanvasGroup>().alpha = canAudio ? 1 : .45f;
            adjustJudge.GetComponent<CanvasGroup>().alpha = canJudge ? 1 : .45f;
            if (calibrationError != null) calibrationStatus.text = calibrationError;
            else if (boundResult?.AutoPlay == true) calibrationStatus.text = L10n.Get("Result", "calibration.autoPlay");
            else if (boundResult?.Timing == null || boundResult.Timing.Count == 0) calibrationStatus.text = L10n.Get("Result", "calibration.noHits");
            else if (calibration == null) calibrationStatus.text = L10n.Get("Result", "calibration.notRecorded");
            else if (!calibration.Matches(current)) calibrationStatus.text = L10n.Get("Result", "calibration.changed");
            else if (calibration.AudioApplied || calibration.JudgeApplied)
                calibrationStatus.text = L10n.Format("Result", "calibration.saved", calibration.AudioApplied && calibration.JudgeApplied ? "A / B" : calibration.AudioApplied ? "A" : "B");
            else if (calibration.CorrectionMs == 0) calibrationStatus.text = L10n.Get("Result", "calibration.belowOne");
            else calibrationStatus.text = L10n.Get("Result", "calibration.hint");
        }
        public void SetPage(bool details, int moveDirection)
        {
            if (Details == details) return;
            Details = details; direction = moveDirection < 0 ? -1 : 1; changedAt = GameTimeline.FrameTime;
            DrawPages();
        }
        public void SetReady(bool complete) => actionLabel.text = L10n.Get("Result", complete ? "page.backToSelect" : "page.skip");
        void Update() => DrawPages();
        void DrawPages()
        {
            float t = (float)SkinUi.CubicOut((GameTimeline.FrameTime - changedAt) / TransitionDuration);
            void Page(CanvasGroup group, bool entering)
            {
                group.alpha = entering ? t : 1 - t;
                group.blocksRaycasts = group.interactable = false;
                ((RectTransform)group.transform).anchoredPosition = new Vector2(pageX + direction * (entering ? 1 - t : -t) * 65, -540);
                // Hiding the whole root also hides crowns, rank animation and nested canvases.
                group.gameObject.SetActive(entering || t < 1);
            }
            Page(scorePage, !Details); Page(detailPage, Details);
            if (offsetControls != null)
            {
                offsetControls.alpha = detailPage.alpha;
                offsetControls.gameObject.SetActive(Details || t < 1);
                offsetControls.blocksRaycasts = offsetControls.interactable = Details && t >= 1;
                ((RectTransform)offsetControls.transform).anchoredPosition = ((RectTransform)detailPage.transform).anchoredPosition;
                DrawCalibration();
            }
            pageName.text = L10n.Get("Result", Details ? "page.details" : "page.score");
            for (int i = 0; i < dots.Length; i++) dots[i].color = i == (Details ? 1 : 0)
                ? new Color32(238, 82, 39, 255) : new Color32(180, 172, 152, 255);
        }
    }
}
