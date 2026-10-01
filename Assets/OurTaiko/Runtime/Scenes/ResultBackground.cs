using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Nijiiro animated 1P result backdrop (Scripts/result/result_bg_layers.lua): two sky halves,
    // Mt Fuji and eleven drifting cloud bars on the exported 720-frame loop, the header band on top,
    // and the clear variant cross-faded in by `clear` (0..1).
    public sealed class ResultBackground
    {
        const double Loop = 720, MsToFrame = 0.06;
        const float FujiX = 583, FujiY = 280, FujiClearX = 584.65f, FujiHeight = 800;
        const string FujiTrack = "huji_1p_l_mc/#84@0";

        // cloud index, y, scale, mirrored, source width, track, frame-0 tx, frame-0 alpha
        static readonly (int Cloud, float Y, float Scale, bool Mirror, float Width, string Track, float X, float A)[] Layers = {
            (0, 162.60f, 1.0f, false, 592, "#67@3", 1705.70f, 0),
            (1, 905.25f, 1.0f, false, 960, "#69@4", 1240.00f, 0),
            (1, 934.00f, 1.2f, true, 960, "#69@5", 1106.30f, 0),
            (2, 622.00f, 1.0f, false, 840, "#71@6", 1382.90f, 1),
            (3, 873.25f, 1.0f, false, 824, "#73@7", -272.10f, 1),
            (4, 873.25f, 1.0f, false, 800, "#75@8", 668.65f, 0),
            (5, 595.25f, 1.0f, false, 848, "#77@9", 468.00f, 0),
            (6, -17.70f, 1.0f, false, 616, "#79@10", 1645.00f, 1),
            (2, 75.10f, 0.6f, true, 840, "#71@11", 346.40f, 0),
            (6, 214.30f, 1.0f, false, 616, "#79@12", 767.80f, 1),
            (7, 400.75f, 1.0f, false, 600, "#81@13", -190.35f, 0),
        };

        readonly LumenClip clip, fuji;
        readonly Image[] sky = new Image[2], skyClear = new Image[2], clouds, cloudsClear;
        readonly Image fujiBase, fujiClear;
        double? fujiStart;
        public CanvasGroup Group { get; }

        public ResultBackground(Transform parent, string name, ResultScene art, LumenClip clip, LumenClip fuji)
        {
            this.clip = clip; this.fuji = fuji;
            var root = SkinUi.Rect(name, parent);
            Group = root.gameObject.AddComponent<CanvasGroup>();
            Group.interactable = Group.blocksRaycasts = false;
            // Base sky: shape 3 at x=0 and mirrored at x=960; clear sky the other way round.
            for (int i = 0; i < 2; i++)
            {
                sky[i] = SkinUi.Image("Sky" + i, root, art.sky);
                sky[i].rectTransform.TopLeft(i * 960, 0);
                sky[i].rectTransform.localScale = new Vector3(i == 1 ? -1 : 1, 1, 1);
            }
            for (int i = 0; i < 2; i++)
            {
                skyClear[i] = SkinUi.Image("SkyClear" + i, root, art.skyClear);
                skyClear[i].rectTransform.TopLeft(i == 0 ? 960 : 0, 0);
                skyClear[i].rectTransform.localScale = new Vector3(i == 1 ? -1 : 1, 1, 1);
            }
            fujiBase = SkinUi.Image("Fuji", root, art.fuji);
            fujiBase.rectTransform.TopLeft(FujiX, FujiY);
            fujiClear = SkinUi.Image("FujiClear", root, art.fujiClear);
            fujiClear.rectTransform.pivot = new Vector2(0.5f, 1);
            clouds = new Image[Layers.Length];
            cloudsClear = new Image[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
            {
                clouds[i] = Cloud("Cloud" + i, root, art.clouds[Layers[i].Cloud], Layers[i]);
                cloudsClear[i] = Cloud("CloudClear" + i, root, art.cloudsClear[Layers[i].Cloud], Layers[i]);
            }
            SkinUi.Image("Header", root, art.header).rectTransform.TopLeft(0, 0);
        }

        static Image Cloud(string name, Transform root, Sprite sprite, (int Cloud, float Y, float Scale, bool Mirror, float Width, string Track, float X, float A) layer)
        {
            var image = SkinUi.Image(name, root, sprite);
            image.rectTransform.pivot = new Vector2(0, 1);
            image.rectTransform.localScale = new Vector3(layer.Mirror ? -layer.Scale : layer.Scale, layer.Scale, 1);
            return image;
        }

        public void Draw(double nowMs, float clear)
        {
            double frame = nowMs * MsToFrame % Loop;
            for (int i = 0; i < 2; i++) { sky[i].Alpha(1); skyClear[i].Alpha(clear); }
            fujiBase.Alpha(1 - clear);
            // huji_1p_l_mc: a one-shot squash-and-stretch from the frame the clear state starts,
            // then parked on its last row.
            if (clear <= 0) fujiStart = null;
            else fujiStart ??= nowMs;
            double fujiFrame = fujiStart.HasValue ? (nowMs - fujiStart.Value) * MsToFrame : 0;
            float ty = (float)fuji.Get(FujiTrack, fujiFrame, "ty", FujiY), sy = (float)fuji.Get(FujiTrack, fujiFrame, "sy", 1);
            fujiClear.rectTransform.sizeDelta = new Vector2(fujiClear.sprite.rect.width, FujiHeight * sy);
            fujiClear.rectTransform.anchoredPosition = new Vector2(FujiClearX + fujiClear.sprite.rect.width / 2, -ty);
            fujiClear.Alpha(clear);
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                float x = (float)clip.Get(layer.Track, frame, "tx", layer.X), a = (float)clip.Get(layer.Track, frame, "a", layer.A);
                // A negative x-scale makes the arcade tx the sprite's right edge.
                float left = layer.Mirror ? x - layer.Width * layer.Scale : x;
                Place(clouds[i], layer.Mirror ? left + layer.Width * layer.Scale : left, layer.Y, a * (1 - clear));
                Place(cloudsClear[i], layer.Mirror ? left + layer.Width * layer.Scale : left, layer.Y, a * clear);
            }
        }

        static void Place(Image image, float x, float y, float alpha)
        {
            image.rectTransform.anchoredPosition = new Vector2(x, -y);
            image.Alpha(alpha > 0.004f ? alpha : 0);
        }
    }
}
