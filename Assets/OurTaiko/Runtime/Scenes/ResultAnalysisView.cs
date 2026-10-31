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
            counts.text = $"有效击打 {data?.Count ?? 0}    漏打 {data?.Misses ?? 0}";
            sides.text = $"早 {data?.Early ?? 0}    晚 {data?.Late ?? 0}" + (data?.Exact > 0 ? $"    零偏差 {data.Exact}" : "");
            empty.text = result.AutoPlay ? "自动演奏不统计手动偏差" : data == null || data.Count == 0 ? "暂无有效击打" : "";
            meanLegend.text = data?.MeanMs is double average
                ? "虚线：平均偏差 · " + (average < 0 ? "偏早" : average > 0 ? "偏晚" : "居中") : "虚线：平均偏差";
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
                calibrationError = "保存失败，请重试";
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
            audioRecommendation.text = calibration == null ? "—" : calibration.AudioApplied ? "已调整 · " + Milliseconds(calibration.RecommendedAudioMs.Value) : Recommendation(calibration.AudioOffsetMs, calibration.RecommendedAudioMs);
            judgeRecommendation.text = calibration == null ? "—" : calibration.JudgeApplied ? "已调整 · " + Milliseconds(calibration.RecommendedJudgeMs.Value) : Recommendation(calibration.JudgeOffsetMs, calibration.RecommendedJudgeMs);
            adjustAudio.GetComponent<CanvasGroup>().alpha = canAudio ? 1 : .45f;
            adjustJudge.GetComponent<CanvasGroup>().alpha = canJudge ? 1 : .45f;
            if (calibrationError != null) calibrationStatus.text = calibrationError;
            else if (boundResult?.AutoPlay == true) calibrationStatus.text = "自动演奏不提供偏移建议";
            else if (boundResult?.Timing == null || boundResult.Timing.Count == 0) calibrationStatus.text = "暂无有效击打，无法推荐";
            else if (calibration == null) calibrationStatus.text = "本局未记录偏移设置，无法推荐";
            else if (!calibration.Matches(current)) calibrationStatus.text = "偏移设置已变化，请重新游玩";
            else if (calibration.AudioApplied || calibration.JudgeApplied)
                calibrationStatus.text = "已保存 " + (calibration.AudioApplied && calibration.JudgeApplied ? "A / B" : calibration.AudioApplied ? "A" : "B") + "，下次游玩生效";
            else if (calibration.CorrectionMs == 0) calibrationStatus.text = "平均偏差不足 1 ms，无需调整";
            else calibrationStatus.text = "每次修正平均偏差的 ½ · 每项限本局一次";
        }
        public void SetPage(bool details, int moveDirection)
        {
            if (Details == details) return;
            Details = details; direction = moveDirection < 0 ? -1 : 1; changedAt = GameTimeline.FrameTime;
            DrawPages();
        }
        public void SetReady(bool complete) => actionLabel.text = complete ? "返回选曲" : "跳过演出";
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
            pageName.text = Details ? "判定详情" : "成绩";
            for (int i = 0; i < dots.Length; i++) dots[i].color = i == (Details ? 1 : 0)
                ? new Color32(238, 82, 39, 255) : new Color32(180, 172, 152, 255);
        }
    }
}
