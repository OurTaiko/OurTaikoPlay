using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // 段位道場's top band (Nijiiro background/collab/dan, YataiDON bg_collabs/dan.lua DanDonBG): a
    // static plate under three cloud layers that slide left at their own speeds and wrap, a row of
    // sakura petals drifting down-left (clipped at the band's bottom edge), and the blossom row in
    // front. Time is the wall clock (bg_fever's accumulator), so it keeps moving through pauses.
    public sealed class DanBackgroundView : MonoBehaviour
    {
        // Each layer's speed (px/s) and its texture's own width, its wrap period.
        [System.Serializable]
        public sealed class Layer
        {
            public RawImage image;
            public float speed;
        }

        // dan.lua: petals PETAL_W×PETAL_H, a 320-frame loop from (2073, -218) by (-4.36875, 2.321875)
        // a frame, two of them 960 px apart, cut at the band's bottom (screen y 278).
        public const float PetalWidth = 480, PetalHeight = 240, PetalLoopFrames = 320;
        public const float PetalX0 = 2073, PetalY0 = -218, PetalDx = -4.36875f, PetalDy = 2.321875f, PetalGap = 960;
        public const float BandOffset = 2, BandBottom = 278;

        [Tooltip("Back to front: layer_a, layer_b, layer_c (behind the petals), then layer_d.")]
        public Layer[] layers;
        [Tooltip("The two petal sprites, left to right.")]
        public RawImage[] petals;

        double started = double.NaN;

        void OnEnable() => started = double.NaN;

        void Update() => Show(GameTimeline.FrameTime);

        public void Show(double now)
        {
            if (double.IsNaN(started)) started = now;
            double elapsed = (now - started) * 1000;
            foreach (var layer in layers) Slide(layer, elapsed);
            ShowPetals(elapsed);
        }

        // DonBGBase:draw_layers: the layer is tiled every texture width, offset by speed × time.
        static void Slide(Layer layer, double elapsedMs)
        {
            var texture = layer.image != null ? layer.image.texture : null;
            if (texture == null) return;
            float period = texture.width, width = layer.image.rectTransform.rect.width;
            double offset = elapsedMs / 1000 * layer.speed % period;
            if (offset > 0) offset -= period;
            // The texture wraps (Repeat), so one image covers every copy.
            layer.image.uvRect = new Rect((float)(-offset / period), 0, width / period, 1);
        }

        // DanDonBG:_draw_petals.
        void ShowPetals(double elapsedMs)
        {
            double f = elapsedMs / (1000.0 / 60) % PetalLoopFrames;
            double top = PetalY0 + PetalDy * f - PetalHeight / 2 + BandOffset;
            double height = System.Math.Min(PetalHeight, BandBottom - top);
            double cx = PetalX0 + PetalDx * f;
            for (int i = 0; i < petals.Length; i++)
            {
                var petal = petals[i];
                petal.enabled = top < BandBottom;
                if (!petal.enabled) continue;
                var rect = petal.rectTransform;
                rect.anchoredPosition = new Vector2((float)(cx - i * PetalGap - PetalWidth / 2), (float)-top);
                rect.sizeDelta = new Vector2(PetalWidth, (float)height);
                // The top of the sprite (UV origin is its bottom).
                float share = (float)(height / PetalHeight);
                petal.uvRect = new Rect(0, 1 - share, 1, share);
            }
        }
    }
}
