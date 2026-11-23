using System.Collections.Generic;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko
{
    // One band of the play backdrop (the header above the lane, the scene below it, the stand, the
    // crowd), drawn from an .otanim the stage gave it. Its layers (the clips named normal, and
    // normal_*) loop on the wall clock, so they keep moving through pauses; while Cleared, its
    // clear clip plays over them from its start (its opening once, then its loop) until Cleared
    // goes. Each change fades, so neither is a cut. A band of a player in a streak of misses is
    // drawn dark.
    [RequireComponent(typeof(RectTransform))]
    public sealed class StageBandView : MonoBehaviour
    {
        public const string LayerClip = "normal", ClearClip = "clear";

        [Tooltip("Whose gauge this band follows: 0 for 1P, 1 for 2P.")]
        public int player;
        [Tooltip("Seconds the clear clip takes to fade in and out, and the band to darken.")]
        public float fade = 0.25f;
        [Tooltip("How bright the band is while Dark, 0 to 1.")]
        public float darkShade = 0.55f;

        // Set by the stage: the player is over the clear line (for the crowd, the gauge is full).
        public bool Cleared { get; set; }
        // Set by the stage: the player is in a streak of misses.
        public bool Dark { get; set; }
        // How much of the clear clip shows, 0 to 1.
        public float ClearAlpha { get; private set; }
        // How bright the band is drawn: 1, down to darkShade.
        public float Shade { get; private set; } = 1;
        // Seconds the layers have been looping.
        public double Seconds => double.IsNaN(started) ? 0 : shownAt - started;
        // Seconds into the clear clip, while it shows.
        public double ClearSeconds => double.IsNaN(clearStarted) ? 0 : shownAt - clearStarted;
        public IReadOnlyList<OtanimView> Layers => layers;
        // Null when the band has no clear clip.
        public OtanimView Clear => overlays.Count > 0 ? overlays[0] : null;

        readonly List<OtanimView> overlays = new List<OtanimView>();
        readonly List<OtanimView> foreground = new List<OtanimView>();

        readonly List<OtanimView> layers = new List<OtanimView>();
        readonly List<OtanimView> views = new List<OtanimView>();
        bool replaceNormal, syncClear;
        Otanim.Clip release;
        bool crowdWasFull;
        double crowdBeats;
        public double Bpm { get; set; } = 120;
        double started = double.NaN, clearStarted = double.NaN, shownAt;

        // The band's file: every clip of it named normal or normal_* is a layer, back to front in
        // the file's order. Its clear clip is `clearClip` of `clearFile`, or of the same file when
        // none is given; a null clip name leaves the band without one. A null file leaves it empty.
        public void Use(OtanimAsset file, OtanimAsset clearFile = null, string clearClip = ClearClip, bool alignClear = false)
        {
            foreach (var view in views) view.Show(null, 0);
            layers.Clear();
            overlays.Clear();
            foreground.Clear();
            syncClear = alignClear;
            release = clearClip == StageView.CrowdClip ? clearFile?.File.Find("release") : null;
            crowdWasFull = false;
            crowdBeats = 0;
            replaceNormal = file != null && clearFile != null;
            int used = 0;
            AddLayers(file, LayerClip, layers, ref used);
            AddLayers(clearFile != null ? clearFile : file, clearClip, overlays, ref used);
            AddLayers(file, "top", foreground, ref used);
            started = clearStarted = double.NaN;
            ClearAlpha = 0;
            Shade = 1;
        }

        void AddLayers(OtanimAsset asset, string prefix, List<OtanimView> target, ref int used)
        {
            if (asset == null || prefix == null) return;
            foreach (var clip in asset.File.Clips)
            {
                if (clip.Name != prefix && !clip.Name.StartsWith(prefix + "_", System.StringComparison.Ordinal)) continue;
                var view = ViewAt(used++);
                view.Use(asset, clip.Name);
                target.Add(view);
            }
        }

        OtanimView ViewAt(int index)
        {
            while (views.Count <= index)
            {
                var child = new GameObject("Clip" + views.Count, typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
                var rect = (RectTransform)child.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                views.Add(child.AddComponent<OtanimView>());
            }
            // Later clips are drawn over earlier ones.
            views[index].transform.SetSiblingIndex(index);
            return views[index];
        }

        void Update() => Show(GameTimeline.FrameTime);

        public void Show(double now)
        {
            if (double.IsNaN(started)) started = shownAt = now;
            double step = now - shownAt;
            shownAt = now;
            float change = fade > 0 ? (float)(step / fade) : 1;
            Shade = Mathf.MoveTowards(Shade, Dark ? darkShade : 1, change * (1 - darkShade));
            if (release != null)
            {
                if (Cleared != crowdWasFull) crowdBeats = 0;
                else crowdBeats += System.Math.Max(0, step) * Bpm / 60;
                if (Cleared && !crowdWasFull) clearStarted = now;
                var clip = Cleared ? Clear.Clip : release;
                bool visible = Cleared || (!double.IsNaN(clearStarted) && crowdBeats <= release.Duration);
                if (!visible) clearStarted = double.NaN;
                ClearAlpha = visible ? 1 : 0;
                Clear.Show(clip, crowdBeats, ClearAlpha, Shade);
                crowdWasFull = Cleared;
                return;
            }
            if (Clear != null) ClearAlpha = Mathf.Clamp01(ClearAlpha + (Cleared ? change : -change));
            foreach (var layer in layers) layer.Show(Seconds, replaceNormal && ClearAlpha >= 1 ? 0 : 1, Shade);
            foreach (var layer in foreground) layer.Show(Seconds, 1, Shade);
            if (Clear == null) return;
            if (Cleared && double.IsNaN(clearStarted)) clearStarted = now;
            // Faded out: the next clear starts the clip over.
            if (!Cleared && ClearAlpha <= 0) clearStarted = double.NaN;
            foreach (var layer in overlays) layer.Show(syncClear ? Seconds : ClearSeconds, double.IsNaN(clearStarted) ? 0 : ClearAlpha, Shade);
        }

        void OnDestroy()
        {
            foreach (var view in views)
                if (view != null) { if (Application.isPlaying) Destroy(view.gameObject); else DestroyImmediate(view.gameObject); }
        }
    }
}
