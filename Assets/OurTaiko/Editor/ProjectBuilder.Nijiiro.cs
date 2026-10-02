using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Nijiiro Layout")]
        public static void ApplyNijiiroSkin()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            foreach (var path in Directory.GetFiles(Root + "Art", "*.png", SearchOption.AllDirectories))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Generated/Nijiiro SDF.asset");
            if (font == null)
            {
                font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(Root + "Art/Taiko.ttf"));
                font.name = "Nijiiro SDF";
                AssetDatabase.CreateAsset(font, Root + "Generated/Nijiiro SDF.asset");
                AssetDatabase.AddObjectToAsset(font.material, font);
                foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
                font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.,!?+-()_★％");
            }
            foreach (var path in new[] { "Assets/Scenes/SinglePlayScene.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path);
                var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
                var root = canvas.transform.GetChild(0);
                ConfigureNijiiroLayout(root, UnityEngine.Object.FindFirstObjectByType<PlayScene>());
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            AssetDatabase.SaveAssets();
        }

        static void ConfigureNijiiroLayout(Transform root, PlayScene play)
        {
            // Opening another scene can unload a font retained only by a managed reference.
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Generated/Nijiiro SDF.asset");
            font.material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.15f);
            font.material.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(25, 17, 27, 255));
            if (root.name == "Viewport1280x720")
            {
                foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    rect.anchoredPosition *= 1.5f;
                    rect.sizeDelta *= 1.5f;
                }
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.fontSize *= 1.5f;
                    label.fontSizeMin *= 1.5f;
                    label.fontSizeMax *= 1.5f;
                }
                root.name = "Viewport1920x1080";
            }
            root.GetComponentInParent<UnityEngine.UI.CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                label.font = font;
                label.fontSharedMaterial = font.material;
                // Remove cached material instances referencing the previous font atlas.
                var serialized = new SerializedObject(label);
                serialized.FindProperty("m_fontMaterial").objectReferenceValue = null;
                serialized.FindProperty("m_fontSharedMaterials").ClearArray();
                serialized.FindProperty("m_fontMaterials").ClearArray();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            for (int i = 0; i < 4; i++) PlacePicture(root, "HeaderPattern" + i, "background/donbg/0_1/background/0", i * 496, 0);
            PlacePicture(root, "FestivalBackground", "background/bg_normal/bg_0/background", 0, 540);
            PlacePicture(root, "FestivalLights", "background/bg_normal/bg_0/overlay", 0, 540);
            PlacePicture(root, "Footer", "background/footer/0", 0, 984);
            var credit = root.Find("FooterCredit");
            if (credit != null) credit.GetComponent<TMP_Text>().text = "OurTaikoPlayer  /  Nijiiro";
            if (play == null) return;

            var lane = root.Find("NoteLane");
            PlacePicture(lane, "LaneBackground", "game/lane/lane_background", 498, 0, 1422, 264);
            PlacePicture(lane, "JudgeCircle", "game/lane/lane_hit_circle", 510, 2);
            PlacePicture(lane, "PlayerCover", "game/lane/1p_lane_cover", 0, 0);
            PlacePicture(lane, "PlayerIcon", "game/lane/1p_icon", 0, 0);
            foreach (var name in new[] { "drum", "drum_don_l", "drum_don_r", "drum_kat_l", "drum_kat_r" })
                PlacePicture(lane, name == "drum" ? "Drum" : name, "game/lane/" + name, 262, -18);
            var difficulty = PlacePicture(lane, "Difficulty", "game/lane/lane_difficulty", 29, 70, 132, 96);
            difficulty.sprite = Slice("OniDifficulty", "game/lane/lane_difficulty", 0, 288, 132, 96);
            for (int i = 1; i <= 7; i++) play.noteSprites[i] = Slice("Note" + i, "game/notes/notes_atlas", 0, 200 + (i - 1) * 192, 192, 192);
            play.noteSprites[9] = Slice("Note9", "game/notes/notes_atlas", 0, 1736, 192, 192);
            ConfigureDrumrollSprites(play);
            PlaceHitFace(lane, 510, 2);
            PlacePicture(lane, "Judgment", "game/hit_effect/judge_good", 546, -60);
            var frames = Enumerable.Range(0, 19).Select(i => Sprite("background/dancer/dancer_0/0_loop/" + i)).ToArray();
            for (int i = 0; i < play.dancers.Length; i++)
            {
                var dancer = PlacePicture(root, "Dancer" + (i + 1), "background/dancer/dancer_0/0_loop/0", 45 + i * 375, 622.5f);
                dancer.GetComponent<SpriteFlipbook>().frames = frames;
            }
            var branch = play.branchLane;
            branch.background.rectTransform.anchoredPosition = new Vector2(498, -7);
            branch.background.rectTransform.sizeDelta = new Vector2(1422, 200);
            ConfigureSoulGauge(root, play);
            ConfigureBalloonCounter(play);
            ConfigureMoji(play);
            EditorUtility.SetDirty(play);
        }

        public static void ApplySoulGauge()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the gauge.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            ConfigureSoulGauge(play.noteLayer.parent.parent.parent, play);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

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

        static void ConfigureSoulGauge(Transform root, PlayScene play)
        {
            var gauge = root.Find("SoulGauge");
            var lane = root.Find("NoteLane");
            gauge.SetSiblingIndex(gauge.GetSiblingIndex() < lane.GetSiblingIndex() ? lane.GetSiblingIndex() : lane.GetSiblingIndex() + 1);
            var view = gauge.GetComponent<SoulGaugeView>();
            if (view == null) view = gauge.gameObject.AddComponent<SoulGaugeView>();
            view.border = PlacePicture(gauge, "Border", "game/gauge/border_hard", 720, -72);
            view.empty = PlacePicture(gauge, "Empty", "game/gauge/1p_unfilled_hard", 720, -72);
            view.red = PlacePicture(gauge, "Fill", "game/gauge/1p_bar", 738, -27);
            view.red.type = UnityEngine.UI.Image.Type.Simple;
            view.clearCap = PlacePicture(gauge, "ClearCap", "game/gauge/bar_clear_transition", 1557, -63);
            view.goldTop = PlacePicture(gauge, "GoldTop", "game/gauge/bar_clear_top", 1578, -60);
            view.goldBottom = PlacePicture(gauge, "GoldBottom", "game/gauge/bar_clear_bottom", 1578, -26);
            view.rainbowA = PlacePicture(gauge, "RainbowA", "game/gauge/rainbow_hard", 731, -67, 1057, 78);
            view.rainbowB = PlacePicture(gauge, "RainbowB", "game/gauge/rainbow_hard", 731, -67, 1057, 78);
            view.cellFade = PlacePicture(gauge, "CellFade", "game/gauge/1p_bar_fade", 738, -27);
            view.grid = PlacePicture(gauge, "Grid", "game/gauge/overlay_hard", 720, -72);
            view.clearLabel = PlacePicture(gauge, "ClearMarker", "game/gauge/clear_ja", 1560, -74);
            view.fire = PlacePicture(gauge, "SoulFire", "game/gauge/tamashii_fire/0", 1717, -233);
            view.fire.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.fire.rectTransform.anchoredPosition += new Vector2(160, -160);
            view.fire.rectTransform.localScale = Vector3.one * 0.75f;
            view.soul = PlacePicture(gauge, "Soul", "game/gauge/tamashii", 1790, -74);
            view.soulOverlay = PlacePicture(gauge, "SoulOverlay", "game/gauge/tamashii_overlay", 1790, -74);
            var ordered = new[] { view.border, view.empty, view.red, view.clearCap, view.goldTop, view.goldBottom,
                view.rainbowA, view.rainbowB, view.cellFade, view.grid, view.clearLabel, view.fire, view.soul, view.soulOverlay };
            for (int i = 0; i < ordered.Length; i++) ordered[i].transform.SetSiblingIndex(i);
            view.redFade = Sprite("game/gauge/1p_bar_fade");
            view.capFade = Sprite("game/gauge/bar_clear_transition_fade");
            view.goldFade = Sprite("game/gauge/bar_clear_fade");
            view.clearLit = Sprite("game/gauge/clear_ja"); view.clearDark = Sprite("game/gauge/clear_dark_ja");
            view.soulLit = Sprite("game/gauge/tamashii"); view.soulDark = Sprite("game/gauge/tamashii_dark");
            view.fireFrames = Enumerable.Range(0, 8).Select(i => Sprite("game/gauge/tamashii_fire/" + i)).ToArray();
            view.styles = new[] { "easy", "normal", "hard" }.Select((tier, i) => new SoulGaugeView.Style {
                border = Sprite("game/gauge/border_" + tier), empty = Sprite("game/gauge/1p_unfilled_" + tier),
                grid = Sprite("game/gauge/overlay_" + tier), clearLabelX = 1350 + i * 105,
                rainbow = Enumerable.Range(0, 8).Select(frame => Slice("Rainbow" + tier + frame, "game/gauge/rainbow_" + tier, 0, frame * 78, 1057, 78)).ToArray()
            }).ToArray();
            view.Initialize(0.8);
            play.soulGauge = view;
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
