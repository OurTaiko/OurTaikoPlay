using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // 段位道場's loading cover (Nijiiro dan_loading/loading_dan, YataiDON Transition::draw_dan) in
    // place of the rainbow curtain: the screen goes black over the course select, then the night
    // sky rises slowly out of the black (36..215 frames, 0.625 px a frame) while the course's plaque
    // and its name fade in (44..64). SongLoadingScene prepares the songs under it; it fades away over
    // the play scene (YataiDON just stops drawing it once the songs are ready).
    public sealed class DanLoadingCover : MonoBehaviour
    {
        public const float CloseSeconds = 0.3f, OpenSeconds = 0.4f;
        const double FrameMs = 1000.0 / 60;

        public CanvasGroup group;
        public Image black, night, plaque;
        [Tooltip("By the course's colour 0..6, in the same order as dan_select's box/folder: the skin's plaque set is shifted one place against it (its 5 is a bronze frame no box has), so this array is plaque 0, 0, 1, 2, 3, 4, 6.")]
        public Sprite[] plaques;
        [Tooltip("The course's name, top to bottom on the plaque.")]
        public TMP_Text rank;
        [Tooltip("Download progress and errors.")]
        public TMP_Text status;

        public bool IsClosed { get; private set; }
        public bool IsVisible => gameObject.activeSelf;

        float nightTop;
        double shownAt = double.NaN;
        bool nightSaved;

        public void SetCourse(string title, int color)
        {
            rank.text = string.Join("\n", (title ?? "").Where(c => !char.IsWhiteSpace(c)).Select(c => c.ToString()));
            if (plaques != null && plaques.Length > 0) plaque.sprite = plaques[Mathf.Clamp(color, 0, plaques.Length - 1)];
            SetStatus("");
        }

        public void SetStatus(string text) => SetStatus(text, Color.white);
        public void SetStatus(string text, Color color)
        {
            if (status == null) return;
            status.text = text ?? "";
            status.color = color;
        }

        void Appear()
        {
            if (!nightSaved) { nightTop = night.rectTransform.anchoredPosition.y; nightSaved = true; }
            if (gameObject.activeSelf) return;
            gameObject.SetActive(true);
            shownAt = double.NaN;
            group.alpha = 1;
            Show(0);
            // The black closes over the course select first; the night shows once it has.
            night.enabled = plaque.enabled = rank.enabled = false;
            black.color = new Color(0, 0, 0, 0);
        }

        void Closed()
        {
            IsClosed = true;
            shownAt = GameTimeline.FrameTime;
            night.enabled = plaque.enabled = rank.enabled = true;
            Show(0);
        }

        // Closed at once (SongLoadingScene run directly).
        public void Park()
        {
            Appear();
            Closed();
        }

        public void Hide()
        {
            IsClosed = false;
            shownAt = double.NaN;
            SetStatus("");
            if (group != null) group.alpha = 1;
            gameObject.SetActive(false);
        }

        public async Task PlayAsync(bool closing, CancellationToken cancellation)
        {
            Appear();
            IsClosed = false;
            double started = GameTimeline.FrameTime;
            float seconds = closing ? CloseSeconds : OpenSeconds;
            while (true)
            {
                float t = Mathf.Clamp01((float)(GameTimeline.FrameTime - started) / seconds);
                if (closing) black.color = new Color(0, 0, 0, t);
                else group.alpha = 1 - t;
                if (t >= 1) break;
                await Awaitable.NextFrameAsync(cancellation);
            }
            if (closing) Closed();
            else Hide();
        }

        void Update()
        {
            if (double.IsNaN(shownAt)) return;
            Show((GameTimeline.FrameTime - shownAt) * 1000);
        }

        // draw_dan, `ms` after the screen went black.
        public void Show(double ms)
        {
            double f = 36 + ms / FrameMs;
            night.rectTransform.anchoredPosition = new Vector2(night.rectTransform.anchoredPosition.x, nightTop + 0.625f * (float)Math.Min(179, Math.Max(0, f - 36)));
            float a = (float)Math.Min(1, Math.Max(0, (f - 44) / 20));
            plaque.color = new Color(1, 1, 1, a);
            rank.alpha = a;
            if (!double.IsNaN(shownAt)) black.color = new Color(0, 0, 0, (float)Math.Min(1, Math.Max(0, 1 - (f - 36) / 29)));
        }
    }
}
