using UnityEngine;

namespace OurTaiko
{
    // The additive ring drawn over the notes at the judge point. Like the face, Judgment only
    // loads outer_* for 良 and 可; 不可, timeouts and roll hits draw no ring.
    public sealed class HitRingView : MonoBehaviour
    {
        public UnityEngine.UI.Image image;
        // Four frames each, from outer_good / outer_ok and their _big strips.
        public Sprite[] good, ok, goodBig, okBig;

        public bool IsPlaying { get; private set; }
        Sprite[] frames;
        double start;

        void Awake() => image.enabled = false;

        public void Play(Judgment result, bool big, double time)
        {
            if (result != Judgment.Good && result != Judgment.Ok) return;
            frames = result == Judgment.Good ? (big ? goodBig : good) : (big ? okBig : ok);
            start = time;
            IsPlaying = true;
        }

        public void ShowTime(double time)
        {
            double elapsed = (time - start) * 1000;
            if (IsPlaying && !HitRingTiming.IsVisible(elapsed)) IsPlaying = false;
            image.enabled = IsPlaying;
            if (!IsPlaying) return;
            image.sprite = frames[HitRingTiming.Frame(elapsed)];
            image.color = new Color(1, 1, 1, (float)HitRingTiming.Opacity(elapsed));
        }
    }
}
