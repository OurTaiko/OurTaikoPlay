using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string HitFacePrefabPath = Root + "Generated/HitFace.prefab";

        // Replaces the old HitFlash picture with an instance of the HitFace prefab in place.
        [MenuItem("OurTaiko/Apply Hit Face Prefab")]
        public static void ApplyHitFace()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the hit face.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            var lane = play.judgment.transform.parent;
            var old = lane.Find("HitFlash") as RectTransform;
            var existing = lane.Find("HitFace") as RectTransform;
            var source = old != null ? old : existing;
            // Nijiiro hit_effect_* sits at skin x=510, y=2 on the lane.
            Vector2 position = source != null ? source.anchoredPosition : new Vector2(510, -2);
            int sibling = source != null ? source.GetSiblingIndex() : play.judgment.transform.GetSiblingIndex();
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            play.hitFace = PlaceHitFace(lane, position.x, -position.y);
            play.hitFace.transform.SetSiblingIndex(sibling);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // Places (or moves) the HitFace prefab instance at skin coordinates on the lane.
        static HitFaceView PlaceHitFace(Transform lane, float x, float y)
        {
            var rect = lane.Find("HitFace") as RectTransform;
            if (rect == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(HitFacePrefab().gameObject, lane);
                instance.name = "HitFace";
                rect = (RectTransform)instance.transform;
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            return rect.GetComponent<HitFaceView>();
        }

        static HitFaceView HitFacePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<HitFaceView>(HitFacePrefabPath);
            if (prefab != null) return prefab;
            var good = Sprite("game/hit_effect/hit_effect_good");
            var root = new GameObject("HitFace", typeof(RectTransform));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.sizeDelta = good.rect.size;
                var image = root.AddComponent<UnityEngine.UI.Image>();
                image.sprite = good; image.raycastTarget = false;
                var view = root.AddComponent<HitFaceView>();
                view.image = image;
                view.good = good;
                view.ok = Sprite("game/hit_effect/hit_effect_ok");
                view.goodBig = Sprite("game/hit_effect/hit_effect_good_big");
                view.okBig = Sprite("game/hit_effect/hit_effect_ok_big");
                return PrefabUtility.SaveAsPrefabAsset(root, HitFacePrefabPath).GetComponent<HitFaceView>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
