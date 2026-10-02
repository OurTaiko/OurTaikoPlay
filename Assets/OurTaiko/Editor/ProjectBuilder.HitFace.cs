using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string HitFacePrefabPath = Root + "Generated/HitFace.prefab";
        const string HitRingPrefabPath = Root + "Generated/HitRing.prefab";

        // Replaces the old HitFlash picture with the HitFace prefab, adds the HitRing prefab,
        // and puts both in Player::draw order. Existing instances keep their positions.
        [MenuItem("OurTaiko/Apply Hit Effects")]
        public static void ApplyHitEffects()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the hit effects.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            var lane = play.judgment.transform.parent;
            var old = lane.Find("HitFlash") as RectTransform;
            var face = lane.Find("HitFace") as RectTransform;
            // Nijiiro hit_effect_* sits at skin x=510, y=2 on the lane.
            Vector2 facePosition = face != null ? face.anchoredPosition : old != null ? old.anchoredPosition : new Vector2(510, -2);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            play.hitFace = PlaceHitFace(lane, facePosition.x, -facePosition.y);
            AttachHitFaceClip();
            AttachHitRingClips();
            var ring = lane.Find("HitRing") as RectTransform;
            // outer_* sits at skin x=450, y=-58: the same centre as the face.
            Vector2 ringPosition = ring != null ? ring.anchoredPosition : new Vector2(450, 58);
            play.hitRing = PlaceHitRing(lane, ringPosition.x, -ringPosition.y);
            OrderHitEffects(play);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // Player::draw: lane cover, face, draw_notes (notes and their text), ring, judgment
        // text, then draw_overlays starting with the drum.
        static void OrderHitEffects(PlayScene play)
        {
            PlaceBefore(play.hitFace.transform, play.noteLayer.parent);
            var lane = play.judgment.transform.parent;
            PlaceBefore(play.hitRing.transform, lane.Find("Drum") ?? play.judgment.transform);
        }

        static void PlaceBefore(Transform item, Transform next)
        {
            int target = next.GetSiblingIndex();
            // Moving an earlier sibling shifts the target down by one.
            item.SetSiblingIndex(item.GetSiblingIndex() < target ? target - 1 : target);
        }

        // Places (or moves) the HitFace prefab instance at skin coordinates on the lane.
        static HitFaceView PlaceHitFace(Transform lane, float x, float y) =>
            PlaceInstance(lane, "HitFace", HitFacePrefab().gameObject, x, y).GetComponent<HitFaceView>();

        static HitRingView PlaceHitRing(Transform lane, float x, float y) =>
            PlaceInstance(lane, "HitRing", HitRingPrefab().gameObject, x, y).GetComponent<HitRingView>();

        static RectTransform PlaceInstance(Transform lane, string name, GameObject prefab, float x, float y)
        {
            var rect = lane.Find(name) as RectTransform;
            if (rect == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lane);
                instance.name = name;
                rect = (RectTransform)instance.transform;
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            return rect;
        }

        static HitFaceView HitFacePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<HitFaceView>(HitFacePrefabPath);
            if (prefab != null) return prefab;
            var good = Sprite("game/hit_effect/hit_effect_good");
            var root = EffectRoot("HitFace", good, out var image);
            try
            {
                var view = root.AddComponent<HitFaceView>();
                AttachClip(root, HitFaceClip());
                view.image = image;
                view.good = good;
                view.ok = Sprite("game/hit_effect/hit_effect_ok");
                view.goodBig = Sprite("game/hit_effect/hit_effect_good_big");
                view.okBig = Sprite("game/hit_effect/hit_effect_ok_big");
                return PrefabUtility.SaveAsPrefabAsset(root, HitFacePrefabPath).GetComponent<HitFaceView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // Judgment::draw_effect uses Nijiiro animation 28: fade 0.5 -> 1 over 66.7 ms, hold 216.6 ms,
        // back to 0.5 over 66.7 ms, then the Judgment is removed (the face switches off at 350 ms).
        static AnimationClip HitFaceClip() => SaveClip("HitFace", 60, false, clip =>
        {
            const float fade = 0.0667f, hold = 0.2166f, end = fade + hold + fade;
            LinearCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Color.a", (0, 0.5f), (fade, 1), (fade + hold, 1), (end, 0.5f));
            SteppedCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Enabled", (0, 1), (end, 0));
        });

        // The HitFace prefab plays HitFace.anim through its own ClipSampler.
        static void AttachHitFaceClip()
        {
            var root = PrefabUtility.LoadPrefabContents(HitFacePrefabPath);
            try
            {
                var sampler = root.GetComponent<ClipSampler>();
                var clip = HitFaceClip();
                if (sampler != null && sampler.clip == clip) return;
                AttachClip(root, clip);
                PrefabUtility.SaveAsPrefabAsset(root, HitFacePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static HitRingView HitRingPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<HitRingView>(HitRingPrefabPath);
            if (prefab != null) return prefab;
            var root = EffectRoot("HitRing", RingFrames("outer_good")[0], out var image);
            try
            {
                // draw_outer_effect uses BLEND_ADDITIVE.
                image.material = AdditiveUiMaterial();
                var view = root.AddComponent<HitRingView>();
                view.image = image;
                SetRingClips(view);
                return PrefabUtility.SaveAsPrefabAsset(root, HitRingPrefabPath).GetComponent<HitRingView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // The HitRing prefab plays one of four clips through its own ClipSampler.
        static void AttachHitRingClips()
        {
            var root = PrefabUtility.LoadPrefabContents(HitRingPrefabPath);
            try
            {
                var view = root.GetComponent<HitRingView>();
                SetRingClips(view);
                PrefabUtility.SaveAsPrefabAsset(root, HitRingPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void SetRingClips(HitRingView view)
        {
            view.good = HitRingClip("HitRingGood", "outer_good");
            view.ok = HitRingClip("HitRingOk", "outer_ok");
            view.goodBig = HitRingClip("HitRingGoodBig", "outer_good_big");
            view.okBig = HitRingClip("HitRingOkBig", "outer_ok_big");
            AttachClip(view.gameObject, view.good);
        }

        // outer_*: four 336x336 frames side by side.
        static Sprite[] RingFrames(string name) => Enumerable.Range(0, 4)
            .Select(i => Slice("HitRing_" + name + i, "game/hit_effect/" + name, i * 336, 0, 336, 336)).ToArray();

        // Judgment::draw_outer_effect: frames from animation 30 (switching after 54.5, 72.7 and
        // 90.9 ms, then holding the last), opacity from animation 27 (opaque until 166.7 ms, gone
        // 33.3 ms later), and the ring is not drawn after that.
        static AnimationClip HitRingClip(string clipName, string strip) => SaveClip(clipName, 60, false, clip =>
        {
            SpriteKeys(clip, "", typeof(UnityEngine.UI.Image), RingFrames(strip), new[] { 0, 0.0545f, 0.0727f, 0.0909f });
            LinearCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Color.a", (0, 1), (0.1667f, 1), (0.2f, 0));
            SteppedCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Enabled", (0, 1), (0.2f, 0));
        });

        static GameObject EffectRoot(string name, Sprite sprite, out UnityEngine.UI.Image image)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = sprite.rect.size;
            image = root.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.raycastTarget = false;
            return root;
        }
    }
}
