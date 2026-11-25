using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro Scripts/global/nameplate.lua on the 408x96 plate canvas. The children are drawn in the
    // original's order: plate, title band, band outline, dan chip (its ring, then its text), 1P/2P
    // badge, title, name. The layers the player wears (NameplateLayers) go in among them: the band
    // and effect items over the title band, the icon over the outline. The 1P
    // plate follows PlayerInfoController, so an info change updates every plate on screen; the 2P
    // plate follows its SecondInfo (2P's account, else the "2P" guest plate) with the 2p badge.
    // The art is not in the game: NameplateArt fetches it at sign-in. Without it a plate is its text.
    [RequireComponent(typeof(RectTransform))]
    public sealed class NameplateView : MonoBehaviour
    {
        public Image shadow, bandUnder, band, outline, danBackground, danRing, dan, badge;
        public TMP_Text title, playerName;
        // The 段位 emblem is two layers, like a song's crown: dan_ring_{silver,gold,rainbow} under the
        // rank, by the pass's combo (銀粋 / 金粋 full combo / 虹粋 all 良), then dan_text[_gold]'s rank
        // (初級 .. 達人, 0..5 blank), white for a red pass and gold for a gold pass.
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
        public int RainbowFrame { get; private set; }
        // The worn layers; null until the plate first wears one.
        public NameplateLayers Layers => layers;
        public RectTransform RectTransform => (RectTransform)transform;

        PlayerInfoController controller;
        NameplateLayers layers;
        PlateComposition composition;
        double rainbowStart;
        float renderedCanvasScale;

        void OnEnable()
        {
            controller = PlayerInfoController.EnsureInstance();
            controller.Changed += ShowFirst;
            controller.SecondChanged += ShowSecond;
            NameplateArt.Changed += ApplyShown;
            rainbowStart = GameTimeline.FrameTime;
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
            ShowAt(GameTimeline.FrameTime - rainbowStart);
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
            NameplateArt.Changed -= ApplyShown;
            Canvas.willRenderCanvases -= RefreshCanvasScale;
        }

        void RefreshCanvasScale()
        {
            var canvas = composition?.Active == true ? composition.TextCanvas : playerName.canvas;
            if (canvas == null || Mathf.Approximately(renderedCanvasScale, canvas.scaleFactor)) return;
            renderedCanvasScale = canvas.scaleFactor;
            // CanvasScaler runs in preWillRenderCanvases. TMP's incremental scale update only
            // accounts for lossyScale, missing the cancelling Canvas factor in Overlay mode.
            // Rebuild on first render / Canvas resize only, after that factor has settled.
            if (composition?.Active == true) { composition.RefreshText(); return; }
            playerName.ForceMeshUpdate();
            if (title.isActiveAndEnabled) title.ForceMeshUpdate();
        }

        public bool PlayLayerClip(NameplateSlot slot, string clip, bool foreground = false) =>
            PlayLayerClipAt(slot, clip, GameTimeline.FrameTime - rainbowStart, foreground);

        public bool PlayLayerClipAt(NameplateSlot slot, string clip, double seconds, bool foreground = false) =>
            composition?.Active == true ? slot == NameplateSlot.Icon && composition.PlayRank(clip, seconds)
                : layers != null && layers.Play(slot, clip, seconds, foreground);

        // The rainbow band runs on real time, including in pause.
        void Update()
        {
            // The players sat down (SceneSwitcher.Seated) after this plate was shown.
            if (applied != Shown) ApplyShown();
            ShowAt(GameTimeline.FrameTime - rainbowStart);
        }

        // The rainbow band and the worn layers `seconds` after the plate was shown (the Editor's
        // preview drives a plate outside play with this).
        public void ShowAt(double seconds)
        {
            if (composition?.Active == true) { composition.Show(seconds); return; }
            ShowRainbow(seconds);
            layers?.Show(seconds);
        }

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
            ShowAt(GameTimeline.FrameTime - rainbowStart);
        }

        void Apply(PlayerInfo info)
        {
            Info = info = info ?? new PlayerInfo();
            var catalog = string.IsNullOrEmpty(info.plateState) ? null : PlateComposition.ReadCatalog();
            if (catalog?.States[PlateComposition.StateFor(info, Shown)] is Newtonsoft.Json.Linq.JArray rows)
            {
                shadow.enabled = bandUnder.enabled = band.enabled = outline.enabled = false;
                danBackground.enabled = danRing.enabled = dan.enabled = badge.enabled = false;
                title.enabled = playerName.enabled = false;
                layers?.Apply(info, false);
                composition ??= new PlateComposition(RectTransform);
                composition.Apply(catalog, rows, info, Shown);
                return;
            }
            composition?.Hide();
            title.enabled = playerName.enabled = true;
            bool art = NameplateArt.Ready;
            bool hasBand = !info.IsCoin;
            shadow.enabled = badge.enabled = art;
            band.enabled = outline.enabled = hasBand && art;
            bandUnder.enabled = false;
            RainbowFrame = 0;
            // *_dani labels only exist in the band family.
            bool hasDan = info.HasDan && hasBand;
            danBackground.enabled = danRing.enabled = dan.enabled = hasDan && art;
            if (art)
            {
                shadow.sprite = NameplateArt.Shadow;
                outline.sprite = NameplateArt.Outline;
                band.sprite = info.rainbow ? NameplateArt.Rainbow[0] : NameplateArt.Bands[info.TitleFrame];
                danBackground.sprite = NameplateArt.DanBackground;
                if (hasDan)
                {
                    danRing.sprite = NameplateArt.DanRings[info.DanComboTier];
                    dan.sprite = (info.gold ? NameplateArt.GoldDanTexts : NameplateArt.DanTexts)[info.dan];
                }
                badge.sprite = Shown == 1 ? NameplateArt.Badge2P : NameplateArt.Badge1P;
            }
            layers ??= new NameplateLayers(RectTransform, band, outline.transform);
            layers.Apply(info, hasBand && art);
            if (layers.CoversBand) band.enabled = bandUnder.enabled = false;

            // Np_coin clears the title outright.
            title.gameObject.SetActive(hasBand && info.HasTitle);
            if (title.gameObject.activeSelf)
            {
                SetText(title, info.title, NameplateLayout.TitleFontSize,
                    NameplateLayout.TitleX, NameplateLayout.TitleY, NameplateLayout.TitleBoxWidth);
            }

            var box = NameplateLayout.NameBox(info);
            playerName.fontSize = box.FontSize;
            SetText(playerName, info.name, box.FontSize, box.X, box.Y, NameplateLayout.NameBoxWidth);
        }

        // Global animation 12 (texture_change): frame_top_rainbow 0-5 for 50 ms each, looping every
        // 300 ms; frames after the first are drawn over the previous one (BandUnder).
        // nameplate.lua never starts it, so the original stays on frame 0; the plate is meant to cycle.
        public const double RainbowFrameSeconds = 0.05;

        void ShowRainbow(double elapsed)
        {
            if (Info == null || !Info.rainbow || Info.IsCoin || !NameplateArt.Ready || (layers != null && layers.CoversBand)) return;
            var frames = NameplateArt.Rainbow;
            double t = System.Math.Max(0, elapsed) % (frames.Length * RainbowFrameSeconds);
            RainbowFrame = System.Math.Min(frames.Length - 1, (int)(t / RainbowFrameSeconds));
            band.sprite = frames[RainbowFrame];
            bandUnder.enabled = RainbowFrame > 0;
            if (RainbowFrame > 0) bandUnder.sprite = frames[RainbowFrame - 1];
        }

        // draw_in_box: centred in the EditText box and squeezed horizontally, never shrunk, to its width.
        static void SetText(TMP_Text text, string value, float fontSize, float x, float y, float boxWidth)
        {
            text.fontSize = fontSize;
            text.characterSpacing = NameplateLayout.TextSpacing * 100 / fontSize;
            text.text = value ?? "";
            text.rectTransform.Center(x, y);
            // preferredWidth measures without rendering. Leave mesh generation until CanvasScaler
            // has settled: forcing it during a saved scene's OnEnable makes TMP apply the initial
            // Canvas scale twice to the SDF, washing the black outline out into a grey rectangle.
            text.Squeeze(boxWidth);
        }
    }
}
