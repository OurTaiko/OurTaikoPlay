using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // All scenes present the same built-in composition on a 408 x 96 canvas.
    [RequireComponent(typeof(RectTransform))]
    public sealed class NameplateView : MonoBehaviour
    {
        // The prefab, for a plate made at run time: Resources.Load<NameplateView>(Resource).
        public const string Resource = "Nameplate";

        public TMP_Text title => composition?.Title;
        public TMP_Text playerName => composition?.PlayerName;
        [Tooltip("0 = 1P, 1 = 2P.")]
        public int seat;
        // The side whose player the plate shows: a single-player screen's plate (seat 0) shows the
        // lone player, 1P or a lone 2P (InputManager.Lead). A pinned plate shows a given player on a given side
        // (a ghost battle) instead.
        int Shown => pinned != null ? pinnedSide : seat == 0 ? InputManager.Lead.Index() : seat;
        PlayerInfo pinned;
        int pinnedSide;
        int applied = -1;

        public PlayerInfo Info { get; private set; }
        // The worn layers; null until the plate first wears one.
        public NameplateLayers Layers => composition?.Layers;
        public RectTransform RectTransform => (RectTransform)transform;

        PlayerInfoController controller;
        PlateComposition composition;
        double animationStart;
        float renderedCanvasScale;

        void OnEnable()
        {
            controller = PlayerInfoController.EnsureInstance();
            controller.Changed += ShowFirst;
            controller.SecondChanged += ShowSecond;
            animationStart = GameTimeline.FrameTime;
            renderedCanvasScale = float.NaN;
            Canvas.willRenderCanvases += RefreshCanvasScale;
            ApplyShown();
        }

        void ShowFirst(PlayerInfo info) { if (pinned == null && Shown == 0) Show(info); }
        void ShowSecond(PlayerInfo info) { if (pinned == null && Shown == 1) Show(info); }
        void ApplyShown()
        {
            applied = Shown;
            Apply(pinned ?? (applied == 0 ? controller.Info : controller.SecondInfo));
            ShowAt(GameTimeline.FrameTime - animationStart);
        }

        // Shows `info` with the badge of `side` (0 1P, 1 2P) from now on, whoever sits at the seat.
        public void Pin(PlayerInfo info, int side)
        {
            pinned = info ?? new PlayerInfo();
            pinnedSide = side == 1 ? 1 : 0;
            ApplyShown();
        }

        void OnDisable()
        {
            if (controller != null) { controller.Changed -= ShowFirst; controller.SecondChanged -= ShowSecond; }
            Canvas.willRenderCanvases -= RefreshCanvasScale;
        }

        void RefreshCanvasScale()
        {
            var canvas = composition?.TextCanvas;
            if (canvas == null || Mathf.Approximately(renderedCanvasScale, canvas.scaleFactor)) return;
            renderedCanvasScale = canvas.scaleFactor;
            // CanvasScaler runs in preWillRenderCanvases. TMP's incremental scale update only
            // accounts for lossyScale, missing the cancelling Canvas factor in Overlay mode.
            // Rebuild on first render / Canvas resize only, after that factor has settled.
            composition.RefreshText();
        }

        public bool PlayLayerClip(NameplateSlot slot, string clip, bool foreground = false) =>
            PlayLayerClipAt(slot, clip, GameTimeline.FrameTime - animationStart, foreground);

        // The rank-up effect answers to the icon slot first: a title that wears an icon has "idle"
        // too, and "idle" is how the rank-up is put back.
        public bool PlayLayerClipAt(NameplateSlot slot, string clip, double seconds, bool foreground = false) =>
            (slot == NameplateSlot.Icon && composition?.PlayRank(clip, seconds) == true) ||
            Layers?.Play(slot, clip, seconds, foreground) == true;

        // Nameplate animations continue while gameplay is paused.
        void Update()
        {
            // The players sat down (SceneSwitcher.Seated) after this plate was shown.
            if (applied != Shown) ApplyShown();
            ShowAt(GameTimeline.FrameTime - animationStart);
        }

        // The editor drives the same animation clock explicitly when seeking.
        public void ShowAt(double seconds) => composition?.Show(seconds);

        // Top-left of the plate canvas, in its stage-anchored parent's skin pixels.
        public void Place(float x, float y)
        {
            var rect = RectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(NameplateLayout.Width, NameplateLayout.Height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        public void Show(PlayerInfo info)
        {
            Apply(info);
            ShowAt(GameTimeline.FrameTime - animationStart);
        }

        void Apply(PlayerInfo info)
        {
            Info = info = info ?? new PlayerInfo();
            var catalog = PlateComposition.ReadCatalog();
            composition ??= new PlateComposition(RectTransform);
            composition.Apply(catalog, (Newtonsoft.Json.Linq.JArray)catalog.States[PlateComposition.StateFor(info, Shown)], info, Shown);
        }
    }
}
