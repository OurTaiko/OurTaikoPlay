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
        const string SongLoadingPath = "Assets/Scenes/SongLoadingScene.unity";
        const string RainbowArt = "global/rainbow_transition/";

        // Adds the Nijiiro song-loading curtain to the global SceneSwitcher prefab and creates
        // SongLoadingScene when it is missing. Re-running only refreshes the curtain object.
        [MenuItem("OurTaiko/Apply Song Loading Curtain")]
        public static void ApplySongLoadingCurtain()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the curtain.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportRainbowArt();
            BuildCurtain();
            if (!File.Exists(SongLoadingPath)) CreateSongLoadingScene();
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == SongLoadingPath)) scenes.Add(new EditorBuildSettingsScene(SongLoadingPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: the song loading curtain and SongLoadingScene are ready.");
        }

        static void ImportRainbowArt()
        {
            foreach (string path in Directory.GetFiles(Root + "Art/" + RainbowArt, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                // Sheets cut into frames (Multiple) keep their sub-sprites.
                if (importer.spriteImportMode != SpriteImportMode.Multiple) importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }

        static Material AdditiveUiMaterial()
        {
            // transition.lua draws the bottom glow with blend = "additive" (SrcAlpha, One).
            const string path = Root + "Generated/UI Additive.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Mobile/Particles/Additive")) { name = "UI Additive" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void BuildCurtain()
        {
            // Sub-rects of the two atlases transition.lua packs its layers into.
            var curtainLeft = Slice("RainbowCurtainLeft", RainbowArt + "rainbow_bg_top", 0, 0, 960, 1080);
            var curtainRight = Slice("RainbowCurtainRight", RainbowArt + "rainbow_bg_top", 960, 0, 960, 1080);
            var bandSprite = Slice("RainbowBand", RainbowArt + "rainbow_bg_bottom", 0, 0, 1600, 256);
            var glowSprite = Slice("RainbowGlow", RainbowArt + "rainbow_bg_bottom", 0, 288, 1600, 512);
            var starSprite = Slice("RainbowStar", RainbowArt + "rainbow_bg_bottom", 0, 832, 128, 128);
            var additive = AdditiveUiMaterial();
            var uiFont = UiFont();
            var titleMaterial = UiOutlineMaterial();
            var subtitleMaterial = UiOutlineMaterial();
            var timeline = Timeline("loading_song");
            if (timeline == null) throw new FileNotFoundException("Animations/loading_song.txt is missing.");

            var root = PrefabUtility.LoadPrefabContents(SwitcherPrefab);
            try
            {
                var switcher = root.GetComponent<SceneSwitcher>();
                var old = root.transform.Find("SongTransition");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

                var cover = new GameObject("SongTransition", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
                cover.SetParent(root.transform, false);
                cover.anchorMin = Vector2.zero; cover.anchorMax = Vector2.one;
                cover.offsetMin = cover.offsetMax = Vector2.zero;
                // Invisible, but blocks every click while the curtain is up.
                var blocker = cover.GetComponent<UnityEngine.UI.Image>();
                blocker.color = new Color(0, 0, 0, 0); blocker.raycastTarget = true;

                var stage = new GameObject("Viewport1920x1080", typeof(RectTransform)).GetComponent<RectTransform>();
                stage.SetParent(cover, false);
                stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
                stage.sizeDelta = new Vector2(1920, 1080);
                // The curtains slide in from outside the stage; keep them out of wider letterboxes.
                stage.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

                var view = cover.gameObject.AddComponent<SongTransition>();
                view.timeline = timeline;
                view.rainbow = SkinUi.Image("Rainbow", stage, Sprite(RainbowArt + "rainbow_bg"), 1920, 1080);
                view.rainbow.rectTransform.TopLeft(0, 0);
                view.curtainLeft = SkinUi.Image("CurtainLeft", stage, curtainLeft);
                view.curtainRight = SkinUi.Image("CurtainRight", stage, curtainRight);
                view.glow = SkinUi.Image("Glow", stage, glowSprite, 1920, 512);
                view.glow.material = additive;
                view.glow.rectTransform.TopLeft(0, 568);
                var starGroup = SkinUi.Rect("Stars", stage);
                starGroup.TopLeft(0, 0);
                view.stars = Enumerable.Range(0, 11).Select(i => SkinUi.Image("Star" + i, starGroup, starSprite)).ToArray();
                view.don = SkinUi.Image("Don", stage, Sprite(RainbowArt + "chara_left"), 560, 560);
                view.katsu = SkinUi.Image("Katsu", stage, Sprite(RainbowArt + "chara_right"), 560, 560);
                view.band = SkinUi.Image("TitleBand", stage, bandSprite);
                view.hint = SkinUi.Image("Hint", stage, Sprite(RainbowArt + "chara_center"), 1920, 512);
                view.hint.rectTransform.TopLeft(0, 568);
                // skin_config transition_title / _subtitle (64 / 40 px) minus rainbow_up 1224 (816 x 1.5).
                view.title = CurtainText("Title", stage, uiFont, titleMaterial, 64, 382);
                view.subtitle = CurtainText("Subtitle", stage, uiFont, subtitleMaterial, 40, 462);

                var serialized = new SerializedObject(switcher);
                serialized.FindProperty("songTransition").objectReferenceValue = view;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                cover.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, SwitcherPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static TMP_Text CurtainText(string name, Transform parent, TMP_FontAsset uiFont, Material outline, float size, float centerY)
        {
            var rect = SkinUi.Rect(name, parent, 1920, size * 1.6f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            SetOutlinedUiText(text, uiFont, outline, Color.white);
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = "";
            rect.Center(960, centerY);
            return text;
        }

        static void CreateSongLoadingScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            // Everything visible is the global curtain parked over this scene.
            var loader = new GameObject("SongLoading").AddComponent<SongLoadingScene>();
            loader.defaultSong = AssetDatabase.LoadAssetAtPath<SongDefinition>(Root + "Songs/TripleHelix.asset");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SongLoadingPath);
        }
    }
}
