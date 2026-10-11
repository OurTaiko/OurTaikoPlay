using System;
using UnityEngine;

namespace OurTaiko
{
    // One mesh, no per-column GameObjects or pointer targets. Coordinates are in milliseconds.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TimingHistogramGraphic : UnityEngine.UI.MaskableGraphic
    {
        TimingStatistics statistics;
        public int AxisMaximum { get; private set; } = 4;
        public void Show(TimingStatistics value)
        {
            statistics = value;
            int peak = 0;
            if (value != null) foreach (int count in value.Bins) peak = Math.Max(peak, count);
            int step = 1;
            while (step * 4 < peak)
            {
                int scale = (int)Math.Pow(10, Math.Floor(Math.Log10(step)));
                step = step < 2 * scale ? 2 * scale : step < 5 * scale ? 5 * scale : 10 * scale;
            }
            AxisMaximum = step * 4;
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            float X(double ms) => r.xMin + (float)((ms - TimingStatistics.MinimumMs) /
                (TimingStatistics.MaximumMs - TimingStatistics.MinimumMs)) * r.width;
            void Box(float left, float bottom, float right, float top, Color32 color)
            {
                int i = mesh.currentVertCount;
                mesh.AddVert(new Vector3(left, bottom), color, Vector2.zero);
                mesh.AddVert(new Vector3(left, top), color, Vector2.zero);
                mesh.AddVert(new Vector3(right, top), color, Vector2.zero);
                mesh.AddVert(new Vector3(right, bottom), color, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
            }
            Box(r.xMin, r.yMin, r.xMax, r.yMax, new Color32(232, 228, 222, 255));
            if (statistics != null)
            {
                void Band(double width, Color32 color) => Box(X(-width), r.yMin, X(width), r.yMax, color);
                Band(statistics.BadWindowMs, new Color32(211, 230, 246, 255));
                Band(statistics.OkWindowMs, new Color32(255, 236, 218, 255));
                Band(statistics.GoodWindowMs, new Color32(255, 246, 188, 255));
            }
            for (int i = 0; i <= 4; i++)
            {
                float y = r.yMin + r.height * i / 4;
                Box(r.xMin, y, r.xMax, y + 1, new Color32(120, 112, 98, 60));
            }
            if (statistics != null)
                for (int i = 0; i < TimingStatistics.BinCount; i++)
                {
                    double start = TimingStatistics.MinimumMs + i * TimingStatistics.BinWidthMs;
                    float top = r.yMin + r.height * statistics.Bins[i] / AxisMaximum;
                    if (statistics.Bins[i] > 0)
                        Box(X(start) + 1, r.yMin, X(start + TimingStatistics.BinWidthMs) - 1, top, new Color32(226, 83, 38, 255));
                }
            Box(X(0) - 1, r.yMin, X(0) + 1, r.yMax, new Color32(55, 50, 45, 255));
            if (statistics?.MeanMs != null)
                for (float y = r.yMin; y < r.yMax; y += 18)
                    Box(X(statistics.MeanMs.Value) - 2, y, X(statistics.MeanMs.Value) + 2,
                        Math.Min(r.yMax, y + 10), new Color32(133, 38, 135, 255));
        }
    }
}
