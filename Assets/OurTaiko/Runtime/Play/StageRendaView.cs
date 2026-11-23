using System.Collections.Generic;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // What a player's drumroll throws over their header (Nijiiro renda): every hit plays one of the
    // set's clips once, on the wall clock, and several can be out at a time.
    [RequireComponent(typeof(RectTransform))]
    public sealed class StageRendaView : MonoBehaviour
    {
        [Tooltip("Whose drumroll this follows: 0 for 1P, 1 for 2P.")]
        public int player;
        [Tooltip("The most out at once; a hit past that takes over the oldest.")]
        public int most = 10;

        // How many are out now.
        public int Out { get; private set; }

        OtanimAsset file;
        readonly List<OtanimView> views = new List<OtanimView>();
        readonly List<double> startedAt = new List<double>();
        readonly List<Otanim.Clip> playing = new List<Otanim.Clip>();
        readonly List<double> rates = new List<double>();
        int[] good, bad;
        int next;
        int lastHitFrame = -1;
        Vector2[] spread;

        // Null: hits throw nothing.
        public void Use(OtanimAsset set, Vector2[] hitSpread = null)
        {
            file = set;
            spread = hitSpread;
            next = 0;
            good = bad = null;
            lastHitFrame = -1;
            for (int i = 0; i < views.Count; i++) { playing[i] = null; views[i].Show(null, 0); }
            Out = 0;
        }

        public void Hit()
        {
            if (Time.frameCount == lastHitFrame) return;
            lastHitFrame = Time.frameCount;
            Hit(GameTimeline.FrameTime, Random.Range(0, int.MaxValue));
        }

        public void UseCharacters(OtanimAsset set)
        {
            Use(set);
            if (set == null) return;
            var positive = new List<int>();
            var negative = new List<int>();
            for (int i = 0; i < set.File.Clips.Length; i++)
            {
                string label = set.File.Clips[i].Name;
                if (label.StartsWith("good_", System.StringComparison.Ordinal)) positive.Add(i);
                if (label.StartsWith("bad_", System.StringComparison.Ordinal)) negative.Add(i);
            }
            good = positive.ToArray();
            bad = negative.ToArray();
        }

        public void Judged(bool missed, double bpm) => Judged(GameTimeline.FrameTime, missed, bpm);

        // A judgement at wall-clock `now` brings out one of the characters for it.
        public void Judged(double now, bool missed, double bpm)
        {
            var options = missed ? bad : good;
            if (options == null || options.Length == 0) return;
            Hit(now, options[Random.Range(0, options.Length)], bpm > 0 ? bpm / 120 : 1);
        }

        // pick says which of the file's clips this hit plays.
        public void Hit(double now, int pick, double rate = 1)
        {
            if (file == null || file.File.Clips.Length == 0 || most <= 0) return;
            while (views.Count < most)
            {
                var child = new GameObject("Hit" + views.Count, typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
                var rect = (RectTransform)child.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                views.Add(child.AddComponent<OtanimView>());
                startedAt.Add(0);
                rates.Add(1);
                playing.Add(null);
            }
            // The first that is free, else the oldest: they are started in turn.
            int slot = playing.FindIndex(clip => clip == null);
            if (slot < 0) { slot = next; next = (next + 1) % views.Count; }
            var clips = file.File.Clips;
            views[slot].Use(file, null);
            int index = pick % clips.Length;
            playing[slot] = clips[index];
            var range = spread != null && spread.Length > 0 ? spread[index % spread.Length] : Vector2.zero;
            ((RectTransform)views[slot].transform).anchoredPosition = new Vector2(
                Random.Range(-range.x, range.x), Random.Range(-range.y, range.y));
            startedAt[slot] = now;
            rates[slot] = rate;
            // The newest is drawn over the others.
            views[slot].transform.SetAsLastSibling();
            Show(now);
        }

        void Update() => Show(GameTimeline.FrameTime);

        public void Show(double now)
        {
            int count = 0;
            for (int i = 0; i < views.Count; i++)
            {
                var clip = playing[i];
                if (clip == null) continue;
                double seconds = (now - startedAt[i]) * rates[i];
                if (seconds > clip.Frames.Length / clip.Fps) { playing[i] = null; views[i].Show(null, 0); continue; }
                views[i].Show(clip, seconds);
                count++;
            }
            Out = count;
        }

        void OnDestroy()
        {
            foreach (var view in views)
                if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); }
        }
    }
}
