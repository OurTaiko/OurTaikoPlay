using UnityEngine;

namespace OurTaiko
{
    // gauge_hit_effect: one burst at a time on the soul badge, drawn over the flying notes.
    public sealed class GaugeHitEffectView : MonoBehaviour
    {
        public RectTransform lane;
        public UnityEngine.UI.Image burst, note;
        public Sprite[] burstFrames;

        public bool IsPlaying { get; private set; }
        public bool IsBig { get; private set; }
        double start;

        // A new landing clears the previous burst and starts over.
        public void Play(Sprite noteSprite, bool big, double time)
        {
            note.sprite = noteSprite;
            IsBig = big;
            start = time;
            IsPlaying = true;
        }

        public void ShowTime(double time)
        {
            double t = time - start;
            if (IsPlaying && GaugeHitEffectTiming.IsFinished(t)) IsPlaying = false;
            burst.enabled = note.enabled = IsPlaying;
            if (!IsPlaying) return;
            var centre = NoteArcView.LaneToLocal(lane, (RectTransform)transform, GaugeHitEffectTiming.CentreX, GaugeHitEffectTiming.CentreY);
            float alpha = (float)GaugeHitEffectTiming.Opacity(t);
            double scale = GaugeHitEffectTiming.Scale(t);
            var (r, g, b) = GaugeHitEffectTiming.Tint(scale);
            burst.sprite = burstFrames[GaugeHitEffectTiming.Frame(t)];
            burst.rectTransform.anchoredPosition = centre;
            burst.rectTransform.sizeDelta = Vector2.one * (float)(GaugeHitEffectTiming.BurstSize * scale);
            burst.color = new Color32(r, g, b, (byte)Mathf.RoundToInt(alpha * 255));
            note.rectTransform.anchoredPosition = centre;
            note.color = new Color(1, 1, 1, alpha);
        }
    }
}
