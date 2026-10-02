using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string HitSoundsPath = Root + "Generated/HitSounds.asset";

        // Targeted upgrade: the 演奏オプション panel in SongSelect, and the hit sounds and lane badges in
        // SinglePlayScene. Re-running only refreshes these references.
        [MenuItem("OurTaiko/Apply Play Options")]
        public static void ApplyPlayOptions()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportSongSelectResultArt();
            ImportSprites(Directory.GetFiles(Root + "Art/game/lane", "mod_*.png"));
            var library = HitSoundLibraryAsset();

            var scene = EditorSceneManager.OpenScene(SongSelectPath);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            select.optionArt = new OptionPanelArt
            {
                board = Sprite("song_select/modifier/top"),
                player = Sprite("song_select/modifier/1p"),
                row = Sprite("song_select/modifier/mod_bg"),
                rowHighlight = Sprite("song_select/modifier/mod_bg_highlight"),
                box = Sprite("song_select/modifier/mod_box"),
                arrow = Sprite("song_select/modifier/blue_arrow"),
                auto = Sprite("song_select/modifier/mod_auto"),
                doron = Sprite("song_select/modifier/mod_doron"),
                abekobe = Sprite("song_select/modifier/mod_abekobe"),
                kimagure = Sprite("song_select/modifier/mod_kimagure"),
                detarame = Sprite("song_select/modifier/mod_detarame"),
                speed = SpeedBadges("song_select/modifier"),
                cursorTimeline = Timeline("option_cursor"),
                hitSounds = library,
                voice = Clip("song_select/voice_options_1p"),
            };
            EditorUtility.SetDirty(select);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            var lane = UnityEngine.Object.FindFirstObjectByType<Canvas>().transform.GetChild(0).Find("NoteLane");
            var badges = lane.Find("ModifierBadges") as RectTransform;
            if (badges == null) badges = Rect("ModifierBadges", lane, 0, 0, 0, 0);
            // Above the lane cover and difficulty tab, like draw_modifiers after draw_lane_cover.
            badges.SetSiblingIndex(lane.Find("Difficulty").GetSiblingIndex() + 1);
            var view = badges.GetComponent<ModifierBadgeView>();
            if (view == null) view = badges.gameObject.AddComponent<ModifierBadgeView>();
            view.speed = SpeedBadges("game/lane");
            view.doron = Sprite("game/lane/mod_doron");
            view.abekobe = Sprite("game/lane/mod_abekobe");
            view.kimagure = Sprite("game/lane/mod_kimagure");
            view.detarame = Sprite("game/lane/mod_detarame");
            play.modifierBadges = view;
            play.hitSounds = library;
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: play options applied to SongSelect and SinglePlayScene.");
        }

        static void ImportSprites(string[] paths)
        {
            foreach (string path in paths)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode != SpriteImportMode.None) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
        }

        // mod_speed_x1_1 .. mod_speed_x4 in PlayOptions.SpeedBadgeValues order.
        static Sprite[] SpeedBadges(string folder) => PlayOptions.SpeedBadgeValues
            .Select(v => Sprite($"{folder}/mod_speed_x{v / 10}" + (v % 10 == 0 ? "" : "_" + v % 10)))
            .Select((sprite, i) => sprite != null ? sprite : throw new FileNotFoundException($"{folder} speed badge {PlayOptions.SpeedBadgeValues[i]}"))
            .ToArray();

        // Names from neiro_list.txt; set i is Audio/hit_sounds/i/don.ogg and ka.ogg.
        static HitSoundLibrary HitSoundLibraryAsset()
        {
            var names = File.ReadAllLines(Root + "Audio/hit_sounds/neiro_list.txt")
                .Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
            var library = AssetDatabase.LoadAssetAtPath<HitSoundLibrary>(HitSoundsPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<HitSoundLibrary>();
                AssetDatabase.CreateAsset(library, HitSoundsPath);
            }
            library.names = names;
            library.don = names.Select((_, i) => Clip($"hit_sounds/{i}/don")).ToArray();
            library.ka = names.Select((_, i) => Clip($"hit_sounds/{i}/ka")).ToArray();
            for (int i = 0; i < names.Length; i++)
                if (library.don[i] == null || library.ka[i] == null) throw new FileNotFoundException("Hit sound set " + i + " is incomplete.");
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            return library;
        }
    }
}
