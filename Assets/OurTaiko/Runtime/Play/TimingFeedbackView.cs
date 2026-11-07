using System;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed class TimingFeedbackView : MonoBehaviour
    {
        public TextMeshProUGUI label;
        public const double Duration = .45;
        double startedAt = double.NegativeInfinity;
        public void Show(double offsetMs)
        {
            label.text = TimingOffsetText.Format(offsetMs) + "ms";
            label.color = TimingOffsetText.ColorOf(offsetMs);
            label.UseUiFont();
            startedAt = GameTimeline.FrameTime;
            label.alpha = 1;
        }
        public void Clear() { startedAt = double.NegativeInfinity; label.alpha = 0; }
        void Awake() => Clear();
        void Update()
        {
            double elapsed = GameTimeline.FrameTime - startedAt;
            label.alpha = (float)Math.Clamp((Duration - elapsed) / .12, 0, 1);
        }
    }
}
