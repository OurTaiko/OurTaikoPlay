using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Clips for the play scene's soul gauge.
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Soul Fire Clips")]
        public static void ApplySoulFireClips() => EditPlayScene(play => AttachSoulFireClips(play.soulGauge));

        // Gauge::draw while full: tamashii_fire cycles 8 frames of 50 ms, and tamashii_overlay is
        // drawn on fire frames 0, 1, 4 and 5.
        static void AttachSoulFireClips(SoulGaugeView view)
        {
            var frames = Enumerable.Range(0, 8).Select(i => Sprite("game/gauge/tamashii_fire/" + i)).ToArray();
            AttachClip(view.fire.gameObject, SaveClip("SoulFire", 20, true, clip =>
                SpriteKeys(clip, "", typeof(UnityEngine.UI.Image), frames, frames.Select((_, i) => i * 0.05f).ToArray())));
            AttachClip(view.soulOverlay.gameObject, SaveClip("SoulOverlay", 20, true, clip =>
                SteppedCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Enabled", (0, 1), (0.1f, 0), (0.2f, 1), (0.3f, 0), (0.4f, 0))));
        }

        [MenuItem("OurTaiko/Apply Soul Rainbow Clips")]
        public static void ApplySoulRainbowClips() => EditPlayScene(play => AttachSoulRainbowClips(play.soulGauge));

        static readonly string[] GaugeTiers = { "easy", "normal", "hard" };

        // Groups RainbowA/RainbowB under a full-size Rainbow object (positions unchanged) that
        // plays the style's clip.
        static void AttachSoulRainbowClips(SoulGaugeView view)
        {
            var gauge = (RectTransform)view.transform;
            var group = view.rainbowA.transform.parent as RectTransform;
            if (group == gauge)
            {
                int index = view.rainbowA.transform.GetSiblingIndex();
                group = new GameObject("Rainbow", typeof(RectTransform)).GetComponent<RectTransform>();
                group.SetParent(gauge, false);
                group.anchorMin = Vector2.zero; group.anchorMax = Vector2.one;
                group.offsetMin = group.offsetMax = Vector2.zero;
                group.SetSiblingIndex(index);
                view.rainbowA.transform.SetParent(group, false);
                view.rainbowB.transform.SetParent(group, false);
            }
            for (int i = 0; i < GaugeTiers.Length; i++) view.styles[i].rainbow = SoulRainbowClip(GaugeTiers[i]);
            view.rainbowSampler = AttachClip(group.gameObject, view.styles[2].rainbow);
            EditorUtility.SetDirty(view);
        }

        // Gauge::draw while full: rainbow cell k is drawn with cell k+1 fading in over its 75 ms,
        // and both fade in together over the first 450 ms. 0-0.6 s is that intro, 0.6-1.2 s one
        // plain loop; the B crossfade is baked every 5 ms while the intro fade multiplies it.
        static AnimationClip SoulRainbowClip(string tier)
        {
            var cells = Enumerable.Range(0, 8).Select(frame => Slice("Rainbow" + tier + frame, "game/gauge/rainbow_" + tier, 0, frame * 78, 1057, 78)).ToArray();
            string name = "SoulRainbow" + char.ToUpperInvariant(tier[0]) + tier.Substring(1);
            return SaveClip(name, 60, false, clip =>
            {
                const float step = 0.075f, fadeIn = 0.45f, end = 1.2f, gap = 0.0001f;
                var image = typeof(UnityEngine.UI.Image);
                var times = Enumerable.Range(0, 16).Select(k => k * step).ToArray();
                SpriteKeys(clip, "RainbowA", image, times.Select((_, k) => cells[k % 8]).ToArray(), times);
                SpriteKeys(clip, "RainbowB", image, times.Select((_, k) => cells[(k + 1) % 8]).ToArray(), times);
                LinearCurve(clip, "RainbowA", image, "m_Color.a", (0, 0), (fadeIn, 1), (end, 1));
                float Fade(float t) => Mathf.Min(1, t / fadeIn);
                var keys = new System.Collections.Generic.List<(float, float)>();
                for (int k = 0; k < 16; k++)
                {
                    float start = k * step;
                    int samples = start < fadeIn ? 15 : 1;
                    for (int j = 0; j < samples; j++)
                    {
                        float t = start + step * j / samples;
                        keys.Add((t, Fade(t) * (t - start) / step));
                    }
                    float last = start + step - gap;
                    keys.Add((last, Fade(last) * (last - start) / step));
                }
                keys.Add((end, 0));
                LinearCurve(clip, "RainbowB", image, "m_Color.a", keys.ToArray());
            });
        }

        [MenuItem("OurTaiko/Apply Cell Fade Clip")]
        public static void ApplyCellFadeClip() => EditPlayScene(play => AttachCellFadeClip(play.soulGauge));

        // Nijiiro enables gauge_cell_fade_in: only the newly filled cell fades in, over 450 ms.
        static void AttachCellFadeClip(SoulGaugeView view)
        {
            view.cellFade.color = new Color(1, 1, 1, view.cellFade.color.a);
            EditorUtility.SetDirty(view.cellFade);
            AttachClip(view.cellFade.gameObject, SaveClip("CellFade", 60, false, clip =>
                LinearCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Color.a", (0, 0), (0.45f, 1))));
        }
    }
}
