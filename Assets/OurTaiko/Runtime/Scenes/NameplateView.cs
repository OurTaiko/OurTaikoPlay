using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro Scripts/global/nameplate.lua for the local 1P player, on the 408x96 plate canvas. The
    // children are drawn in the original's order: plate, title band, band outline, dan chip, 1P badge,
    // title, name. It follows PlayerInfoController, so an info change updates every plate on screen.
    [RequireComponent(typeof(RectTransform))]
    public sealed class NameplateView : MonoBehaviour
    {
        public Image shadow, bandUnder, band, outline, danBackground, dan, badge;
        public TMP_Text title, playerName;
        [Tooltip("frame_top: the 5 title backgrounds.")]
        public Sprite[] titleBackgrounds;
        [Tooltip("frame_top_rainbow: the 6 rainbow band frames.")]
        public Sprite[] rainbowBackgrounds;
        [Tooltip("dan_emblem / dan_emblem_gold: 初級 .. 達人.")]
        public Sprite[] danEmblems, goldDanEmblems;

        public PlayerInfo Info { get; private set; }
        public int RainbowFrame { get; private set; }
        public RectTransform RectTransform => (RectTransform)transform;

        PlayerInfoController controller;
        double rainbowStart;

        void OnEnable()
        {
            controller = PlayerInfoController.EnsureInstance();
            controller.Changed += Show;
            rainbowStart = Time.unscaledTimeAsDouble;
            Show(controller.Info);
        }

        void OnDisable()
        {
            if (controller != null) controller.Changed -= Show;
        }

        // The rainbow band runs on real time, like the original's current_ms (it keeps cycling in pause).
        void Update() => ShowRainbow((Time.unscaledTimeAsDouble - rainbowStart) * 1000);

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
            Info = info = info ?? new PlayerInfo();
            bool hasBand = !info.IsCoin;
            band.enabled = outline.enabled = hasBand;
            bandUnder.enabled = false;
            if (hasBand && !info.rainbow) band.sprite = titleBackgrounds[info.TitleFrame];
            RainbowFrame = 0;
            ShowRainbow((Time.unscaledTimeAsDouble - rainbowStart) * 1000);

            // *_dani labels only exist in the band family.
            bool hasDan = info.HasDan && hasBand;
            danBackground.enabled = dan.enabled = hasDan;
            if (hasDan) dan.sprite = (info.gold ? goldDanEmblems : danEmblems)[info.dan];
            badge.enabled = true;

            // Np_coin clears the title outright.
            title.gameObject.SetActive(hasBand && info.HasTitle);
            if (title.gameObject.activeSelf) SetText(title, info.title, NameplateLayout.TitleFontSize,
                NameplateLayout.TitleX, NameplateLayout.TitleY, NameplateLayout.TitleBoxWidth);

            var box = NameplateLayout.NameBox(info);
            playerName.fontSize = box.FontSize;
            playerName.OutlineOutsidePixels(3);
            playerName.fontMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            SetText(playerName, info.name, box.FontSize, box.X, box.Y, NameplateLayout.NameBoxWidth);
        }

        void ShowRainbow(double elapsedMs)
        {
            if (Info == null || !Info.rainbow || Info.IsCoin) return;
            int frame = NameplateLayout.RainbowFrame(elapsedMs);
            if (frame == RainbowFrame && band.sprite == rainbowBackgrounds[frame]) return;
            RainbowFrame = frame;
            // Frames after the first are drawn over the previous one.
            bandUnder.enabled = frame > 0;
            if (frame > 0) bandUnder.sprite = rainbowBackgrounds[frame - 1];
            band.sprite = rainbowBackgrounds[frame];
        }

        // draw_in_box: centred in the EditText box and squeezed horizontally, never shrunk, to its width.
        static void SetText(TMP_Text text, string value, float fontSize, float x, float y, float boxWidth)
        {
            text.fontSize = fontSize;
            text.characterSpacing = NameplateLayout.TextSpacing * 100 / fontSize;
            text.text = value ?? "";
            text.rectTransform.Center(x, y);
            text.ForceMeshUpdate();
            text.Squeeze(boxWidth);
        }
    }
}
