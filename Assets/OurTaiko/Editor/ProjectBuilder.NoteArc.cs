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
            view.burstFrames = Enumerable.Range(0, 3).Select(i => Slice("GaugeHitEffect" + i, "game/gauge/hit_effect", i * 232, 0, 232, 232)).ToArray();
            // Burst first, then the note over it (GaugeHitEffect::draw).
            view.burst = EffectImage(layer, "Burst", view.burstFrames[0], 232);
            view.note = EffectImage(layer, "Note", null, 192);
            EditorUtility.SetDirty(view);
            return view;
        }

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
