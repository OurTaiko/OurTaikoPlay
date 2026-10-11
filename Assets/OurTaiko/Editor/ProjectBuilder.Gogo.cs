using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        // Source: YataiDONNijiiro Graphics/game/gogo_time/texture.json, game/animation.json
        // (23, 24, 68, 76), skin_config gogo_fire_fade / gogo_explosion_slots.
        // The original PNGs and importer slices are retained; this only builds clips and UI.
        public static void ApplyGogoEffects()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing GOGO.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scene before applying GOGO.");
            var fire = GogoFrames("fire", 8);
            var bursts = GogoFrames("explosion", 30);
            var entryClip = GogoEntryClip();
            var fireClip = SaveClip("GogoFire", 1000, true, clip =>
            {
                SpriteKeys(clip, "", typeof(UnityEngine.UI.Image), fire,
                    Enumerable.Range(0, 8).Select(i => i * 0.03334f).ToArray());
                // Exact loop duration, independent of the sprite curve's last-key frame padding.
                LinearCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Color.a", (0, 0.699f), (0.26672f, 0.699f));
            });
            var burstClip = SaveClip("GogoExplosion", 1000, false, clip =>
            {
                // Frame 3 begins at exactly 50 ms in the source, rather than 50.01 ms.
                var times = Enumerable.Range(0, 30).Select(i => i == 3 ? 0.05f : i * 0.01667f).ToArray();
                for (int i = 0; i < 6; i++)
                    SpriteKeys(clip, "Burst" + i, typeof(UnityEngine.UI.Image), bursts, times);
                LinearCurve(clip, "", typeof(CanvasGroup), "m_Alpha", (0, 1), (0.5f, 1));
            });
            try
            {
                foreach (string path in new[] { "Assets/Scenes/SinglePlayScene.unity", "Assets/Scenes/PracticeScene.unity" })
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                    var lane = play.branchLane.transform.parent;
                    var old = lane.Find("GogoTint");
                    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                    var root = lane.Find("GogoEffects") as RectTransform ?? Rect("GogoEffects", lane, 0, 0, 0, 0);
                    var view = root.GetComponent<GogoView>() ?? root.gameObject.AddComponent<GogoView>();
                    view.lane = PlacePicture(root, "Lane", "game/gogo_time/lane_effect", 496, 12, 1424, 195);
                    // Expansion around the vertical centre of the skin's rectangle.
                    view.lane.rectTransform.pivot = new Vector2(0, 0.5f);
                    view.lane.rectTransform.anchoredPosition = new Vector2(496, -109.5f);
                    view.lane.color = new Color(1, 1, 1, 0.5f);
                    var fireRect = root.Find("Fire") as RectTransform ?? Rect("Fire", root, 0, 0, 440, 440);
                    view.fire = fireRect.GetComponent<UnityEngine.UI.Image>() ?? fireRect.gameObject.AddComponent<UnityEngine.UI.Image>();
                    view.fire.sprite = fire[0]; view.fire.raycastTarget = false;
                    fireRect.pivot = new Vector2(0.5f, 0.5f);
                    // Source centered draw: (406,-111) + half of the 440x440 cel.
                    fireRect.anchoredPosition = new Vector2(626, -109);
                    view.fire.color = new Color(1, 1, 1, 0.699f);
                    view.entry = AttachClip(root.gameObject, entryClip);
                    view.fireLoop = AttachClip(fireRect.gameObject, fireClip);

                    var explosionRoot = root.Find("Explosions") as RectTransform ?? Rect("Explosions", root, 0, 0, 0, 0);
                    // Fireworks use screen coordinates, unlike the lane-relative fire and light.
                    explosionRoot.anchoredPosition = -((RectTransform)lane).anchoredPosition;
                    if (explosionRoot.GetComponent<CanvasGroup>() == null) explosionRoot.gameObject.AddComponent<CanvasGroup>();
                    var positions = new[] { new Vector2(-46,336), new Vector2(276,333), new Vector2(599,333),
                        new Vector2(921,333), new Vector2(1244,333), new Vector2(1566,336) };
                    view.explosions = new UnityEngine.UI.Image[6];
                    for (int i = 0; i < 6; i++)
                    {
                        var r = explosionRoot.Find("Burst" + i) as RectTransform ?? Rect("Burst" + i, explosionRoot, 0, 0, 392, 762);
                        var image = r.GetComponent<UnityEngine.UI.Image>() ?? r.gameObject.AddComponent<UnityEngine.UI.Image>();
                        image.sprite = bursts[0]; image.raycastTarget = false;
                        r.pivot = new Vector2(0.5f, 0.5f);
                        r.anchoredPosition = new Vector2(positions[i].x + 196, -positions[i].y - 381);
                        r.localScale = new Vector3(i >= 4 ? -1 : 1, 1, 1);
                        view.explosions[i] = image;
                    }
                    view.explosion = AttachClip(explosionRoot.gameObject, burstClip);
                    root.SetSiblingIndex(lane.Find("JudgeCircle").GetSiblingIndex() + 1);
                    view.ResetDisplay();
                    play.gogo = view;
                    EditorUtility.SetDirty(play);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.DeleteAsset(ClipFolder + "GogoPulse.anim");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static Sprite[] GogoFrames(string name, int count)
        {
            var frames = AssetDatabase.LoadAllAssetsAtPath(Root + "Art/game/gogo_time/" + name + ".png")
                .OfType<Sprite>().ToDictionary(s => s.name);
            return Enumerable.Range(0, count).Select(i => frames[name + "_" + i]).ToArray();
        }

        static AnimationClip GogoEntryClip() => SaveClip("GogoEntry", 60, false, clip =>
        {
            // TextureResizeAnimation is linear unless easing is explicitly configured.
            // ID 24 holds 3.474 for 66.68 ms, then reaches 1.351 after another 266.72 ms.
            foreach (string axis in new[] { "x", "y" })
                LinearCurve(clip, "Fire", typeof(Transform), "m_LocalScale." + axis,
                    (0, 3.474f), (0.06668f, 3.474f), (0.3334f, 1.351f));
            LinearCurve(clip, "Lane", typeof(Transform), "m_LocalScale.y", (0, 0), (0.13336f, 1));
        });
    }
}
