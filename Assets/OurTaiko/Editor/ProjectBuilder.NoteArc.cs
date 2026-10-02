using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Nijiiro Note Arcs")]
        public static void ApplyNoteArcs()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the note arcs.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            ConfigureNoteArcs(UnityEngine.Object.FindFirstObjectByType<PlayScene>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureNoteArcs(PlayScene play)
        {
            var gauge = play.soulGauge.transform;
            var viewport = gauge.parent;
            var layer = viewport.Find("NoteArcs") as RectTransform;
            if (layer == null)
            {
                layer = Rect("NoteArcs", viewport, 0, 0, 0, 0);
                // The sweep rises past the top of the design area; keep it out of the letterbox.
                layer.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            }
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            // Player::draw paints the gauge first and draw_overlays' arcs later, so the notes fly over it.
            layer.SetSiblingIndex(gauge.GetSiblingIndex() + 1);
            var view = layer.GetComponent<NoteArcView>();
            if (view == null) view = layer.gameObject.AddComponent<NoteArcView>();
            view.lane = (RectTransform)play.noteLayer.parent.parent;
            play.noteArcs = view;
            view.gaugeHitEffect = ConfigureGaugeHitEffect(viewport, layer, view.lane);
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(play);
        }

        static GaugeHitEffectView ConfigureGaugeHitEffect(Transform viewport, RectTransform arcs, RectTransform lane)
        {
            var layer = viewport.Find("GaugeHitEffect") as RectTransform;
            if (layer == null) layer = Rect("GaugeHitEffect", viewport, 0, 0, 0, 0);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            // draw_overlays draws gauge_hit_effect after draw_arc_list.
            layer.SetSiblingIndex(arcs.GetSiblingIndex() + 1);
            var view = layer.GetComponent<GaugeHitEffectView>();
            if (view == null) view = layer.gameObject.AddComponent<GaugeHitEffectView>();
            view.lane = lane;
            // gauge/hit_effect: three 232x232 frames side by side.
            var frames = Enumerable.Range(0, 3).Select(i => Slice("GaugeHitEffect" + i, "game/gauge/hit_effect", i * 232, 0, 232, 232)).ToArray();
            // Burst first, then the note over it (GaugeHitEffect::draw).
            view.burst = EffectImage(layer, "Burst", frames[0], 232);
            view.note = EffectImage(layer, "Note", null, 192);
            AttachClip(layer.gameObject, GaugeHitEffectClip(frames));
            EditorUtility.SetDirty(view);
            return view;
        }

        // GaugeHitEffect: Nijiiro animation.json 2 (frames at 33.33 and 66.66 ms), 32 (0.8 until
        // 116.67 ms, then linear to 1.5 over 266 ms) and 33 (opaque until 300 ms, gone 83 ms later,
        // when the effect is erased). GaugeHitEffect::update tints the burst by its size: raylib
        // YELLOW up to 0.8, ORANGE up to 0.9, then RED (1P). 31 (circle fade-in) drives Nijiiro's
        // transparent hit_effect_circle* placeholders and 34 (rotation) is pinned to 0.
        static AnimationClip GaugeHitEffectClip(Sprite[] frames) => SaveClip("GaugeHitEffect", 60, false, clip =>
        {
            const float resizeDelay = 0.11667f, resize = 0.266f, fadeDelay = 0.3f, end = 0.383f, size = 232;
            var image = typeof(UnityEngine.UI.Image);
            var rect = typeof(RectTransform);
            SpriteKeys(clip, "Burst", image, frames, new[] { 0, 0.03333f, 0.06666f });
            foreach (var axis in new[] { "x", "y" })
                LinearCurve(clip, "Burst", rect, "m_SizeDelta." + axis, (0, size * 0.8f), (resizeDelay, size * 0.8f), (resizeDelay + resize, size * 1.5f));
            // Size reaches 0.9 at 116.67 + 266 x 0.1 / 0.7 ms.
            float red = resizeDelay + resize * 0.1f / 0.7f;
            var yellow = new Color32(253, 249, 0, 255); var orange = new Color32(255, 161, 0, 255); var redColor = new Color32(230, 41, 55, 255);
            foreach (var (channel, pick) in new (string, Func<Color, float>)[] { ("r", c => c.r), ("g", c => c.g), ("b", c => c.b) })
                SteppedCurve(clip, "Burst", image, "m_Color." + channel, (0, pick(yellow)), (resizeDelay, pick(orange)), (red, pick(redColor)));
            foreach (var path in new[] { "Burst", "Note" })
            {
                LinearCurve(clip, path, image, "m_Color.a", (0, 1), (fadeDelay, 1), (end, 0));
                SteppedCurve(clip, path, image, "m_Enabled", (0, 1), (end, 0));
            }
        });

        static UnityEngine.UI.Image EffectImage(RectTransform layer, string name, Sprite sprite, float size)
        {
            var rect = layer.Find(name) as RectTransform;
            if (rect == null) rect = Rect(name, layer, 0, 0, size, size);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetAsLastSibling();
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.raycastTarget = false; image.enabled = false;
            return image;
        }
    }
}
