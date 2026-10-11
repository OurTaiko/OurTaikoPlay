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
        public bool Details { get; private set; }
        double changedAt = double.NegativeInfinity;
        int direction = 1;
        const double TransitionDuration = .16;
        public bool IsTransitioning => GameTimeline.FrameTime - changedAt < TransitionDuration;
        public void Bind(PlayResult result)
        {
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
                ((RectTransform)group.transform).anchoredPosition = new Vector2(960 + direction * (entering ? 1 - t : -t) * 65, -540);
                // Hiding the whole root also hides crowns, rank animation and nested canvases.
                group.gameObject.SetActive(entering || t < 1);
            }
            Page(scorePage, !Details); Page(detailPage, Details);
            pageName.text = Details ? "判定详情" : "成绩";
            for (int i = 0; i < dots.Length; i++) dots[i].color = i == (Details ? 1 : 0)
                ? new Color32(238, 82, 39, 255) : new Color32(180, 172, 152, 255);
        }
    }
}
