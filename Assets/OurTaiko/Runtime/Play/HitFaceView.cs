using UnityEngine;

namespace OurTaiko
{
    // The face at the judge point. Judgment only loads hit_effect_* for 良 and 可;
    // 不可, timeouts and roll hits draw no face.
    public sealed class HitFaceView : MonoBehaviour
    {
        public UnityEngine.UI.Image image;
        public Sprite good, ok, goodBig, okBig;

        public bool IsPlaying { get; private set; }
        double start;

        void Awake() => image.enabled = false;

        public void Play(Judgment result, bool big, double time)
        {
            if (result != Judgment.Good && result != Judgment.Ok) return;
            image.sprite = result == Judgment.Good ? (big ? goodBig : good) : (big ? okBig : ok);
            start = time;
            IsPlaying = true;
        }

        public void ShowTime(double time)
        {
            double elapsed = (time - start) * 1000;
            if (IsPlaying && !HitFaceTiming.IsVisible(elapsed)) IsPlaying = false;
            image.enabled = IsPlaying;
            if (IsPlaying) image.color = new Color(1, 1, 1, (float)HitFaceTiming.Opacity(elapsed));
        }
    }
}
