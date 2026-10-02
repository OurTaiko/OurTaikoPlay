using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Nijiiro Note Moji")]
        public static void ApplyMoji()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the moji lane.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            ConfigureMoji(UnityEngine.Object.FindFirstObjectByType<PlayScene>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureMoji(PlayScene play)
        {
            // notes/moji is a cropped atlas, which read_tex_obj_data samples with point filtering.
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/game/notes/moji.png");
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }
            play.mojiSprites = Enumerable.Range(0, 12).Select(i => Slice("Moji" + i, "game/notes/moji", 0, i * 48, 256, 48)).ToArray();
            play.mojiRollSprite = Sprite("game/notes/moji_drumroll_mid");

            // draw_notes scissors only X, from the lane cover's right edge; the text row
            // (skin moji.y=209, 48 high) lies below the note clip, so it gets its own mask.
            var noteClip = play.noteLayer.parent;
            var lane = noteClip.parent;
            var clip = lane.Find("MojiClip") as RectTransform;
            if (clip == null)
            {
                clip = Rect("MojiClip", lane, 0, 0, 0, 0);
                clip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            }
            var noteRect = (RectTransform)noteClip;
            clip.anchoredPosition = noteRect.anchoredPosition;
            clip.sizeDelta = new Vector2(noteRect.sizeDelta.x, ((RectTransform)lane).rect.height);
            // Drawn right after the notes, before judgment effects and the drum overlays.
            clip.SetSiblingIndex(noteClip.GetSiblingIndex() + 1);
            var layer = clip.Find("Moji") as RectTransform;
            if (layer == null) layer = Rect("Moji", clip, 0, 0, 0, 0);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            play.mojiLayer = layer;
            EditorUtility.SetDirty(play);
        }
    }
}
