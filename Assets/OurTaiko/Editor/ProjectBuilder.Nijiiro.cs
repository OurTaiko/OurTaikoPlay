using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Nijiiro Drumroll Sprites")]
        public static void ApplyDrumrollSprites()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing drumroll sprites.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            ConfigureDrumrollSprites(play);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureDrumrollSprites(PlayScene play)
        {
            // read_tex_obj_data uses point sampling for cropped atlases. Bilinear
            // sampling here leaks the neighboring big-roll strip into the small one.
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/game/notes/notes_atlas.png");
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                importer.SaveAndReimport();
            }
            play.rollBodySprites = new[] {
                Slice("RollBodySmall", "game/notes/notes_atlas", 0, 1544, 72, 192),
                Slice("RollBodyBig", "game/notes/notes_atlas", 72, 1544, 72, 192)
            };
            play.rollTailSprites = new[] {
                Slice("RollTailSmall", "game/notes/notes_atlas", 0, 2120, 80, 192),
                Slice("RollTailBig", "game/notes/notes_atlas", 0, 2312, 120, 192)
            };
        }

        static UnityEngine.UI.Image PlacePicture(Transform parent, string name, string path, float x, float y, float width = 0, float height = 0)
        {
            var child = parent.Find(name);
            if (child == null) return Picture(parent, name, path, x, y, width, height);
            var image = child.GetComponent<UnityEngine.UI.Image>();
            image.sprite = Sprite(path);
            if (image.sprite == null) throw new FileNotFoundException(path);
            var rect = image.rectTransform;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width > 0 ? width : image.sprite.rect.width, height > 0 ? height : image.sprite.rect.height);
            return image;
        }
    }
}
