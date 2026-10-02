using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string SongSelectPath = "Assets/Scenes/SongSelect.unity", ResultPath = "Assets/Scenes/Result.unity";

        // Creates the two scenes only when missing, so later Inspector / hierarchy edits survive re-runs.
        [MenuItem("OurTaiko/Create Song Select And Result Scenes")]
        public static void CreateSongSelectAndResult()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportSongSelectResultArt();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Generated/Nijiiro SDF.asset");
            var songs = new[] { "TripleHelix", "Calibration", "BranchTraining" }
                .Select(n => AssetDatabase.LoadAssetAtPath<SongDefinition>(Root + "Songs/" + n + ".asset")).ToArray();
            if (songs.Any(s => s == null)) throw new FileNotFoundException("A song asset is missing.");
            if (!File.Exists(SongSelectPath)) CreateSongSelectScene(songs);
            if (!File.Exists(ResultPath)) CreateResultScene();
            var outline = OutlineMaterial();
            UpgradeStage(SongSelectPath, outline); UpgradeStage(ResultPath, outline);
            // SongSelect is the first scene (SceneSwitcher.MenuScene) until an Entry scene is ported.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != SongSelectPath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(SongSelectPath, true));
            if (!scenes.Any(s => s.path == ResultPath)) scenes.Add(new EditorBuildSettingsScene(ResultPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: SongSelect and Result scenes are ready and listed in Build Settings.");
        }

        static void ImportSongSelectResultArt()
        {
            foreach (var folder in new[] { "song_select", "result" })
                foreach (string path in Directory.GetFiles(Root + "Art/" + folder, "*.png", SearchOption.AllDirectories))
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    string name = path.Replace('\\', '/');
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.maxTextureSize = 4096;
                    // Board 3-slices: 56 px caps around the 240 px centre; the cursor glow has 80 px caps.
                    importer.spriteBorder = name.Contains("/box/bar_genre/") ? new Vector4(0, 56, 0, 56)
                        : name.EndsWith("/bar_genre_overlay.png") ? new Vector4(0, 80, 0, 80) : Vector4.zero;
                    // The original draws every cropped sheet with point filtering (texture.cpp).
                    bool sheet = new[] { "difficulty_star", "score_num", "judge_num", "high_score_num", "result/score/difficulty", "gleam", "rainbow_", "s_crown_dfc", "s_crown_fc" }
                        .Any(name.Contains);
                    importer.filterMode = sheet ? FilterMode.Point : FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }
        }

        static Sprite[] Frames(string directory, int count) => Enumerable.Range(0, count).Select(i => Sprite(directory + "/" + i)).ToArray();
        static Sprite[] Cells(string name, string path, int count, int width, int height, bool vertical = false)
            => Enumerable.Range(0, count).Select(i => Slice(name + i, path, vertical ? 0 : i * width, vertical ? i * height : 0, width, height)).ToArray();
        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/" + path + ".ogg");
        static TextAsset Timeline(string name) => AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Animations/" + name + ".txt");
        static Texture2D Texture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/" + path + ".png");

        static RectTransform NewStage()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            var viewport = Rect("Viewport1920x1080", canvas.transform, 0, 0, 1920, 1080);
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = Vector2.zero;
            // Wider or taller windows keep the design area centred; the 2880-px strips stay inside it.
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            return viewport;
        }

        static AudioSource Source(Transform parent, string name, float volume = 1)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(parent);
            source.playOnAwake = false;
            source.volume = volume;
            return source;
        }

        static void AddStageFps(Transform root)
        {
            // Bottom-right: the top-left corner holds the result difficulty tab and the course mark.
            var panel = Panel(root, "FpsPanel", 1662, 1011, 222, 45, new Color32(28, 29, 32, 225));
            var label = Label(panel.transform, "FpsCounter", "FPS --", 12, 0, 198, 45, 27, TextAlignmentOptions.Left);
            label.gameObject.AddComponent<FpsCounter>();
        }

        static Material OutlineMaterial()
        {
            const string path = Root + "Generated/Nijiiro SDF Outline.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(font.material) { name = "Nijiiro SDF Outline" };
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // Targeted upgrade of scenes built earlier: the outline material and the bottom-right FPS panel.
        static void UpgradeStage(string path, Material outline)
        {
            var scene = EditorSceneManager.OpenScene(path);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            var result = UnityEngine.Object.FindFirstObjectByType<ResultScene>();
            if (select != null) { select.outlineMaterial = outline; EditorUtility.SetDirty(select); }
            if (result != null) { result.outlineMaterial = outline; EditorUtility.SetDirty(result); }
            var panel = UnityEngine.Object.FindFirstObjectByType<FpsCounter>()?.transform.parent as RectTransform;
            if (panel != null) panel.anchoredPosition = new Vector2(1662, -1011);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void CreateSongSelectScene(SongDefinition[] songs)
        {
            var root = NewStage();
            var controller = new GameObject("SongSelect").AddComponent<SongSelectScene>();
            controller.songs = songs;
            controller.font = font;
            controller.nameplatePrefab = AssetDatabase.LoadAssetAtPath<NameplateView>(NameplatePrefabPath);
            var background = Rect("Background", root, 0, 0, 1920, 1080);
            controller.backgroundTiles = new UnityEngine.UI.Image[4];
            for (int i = 0; i < 4; i++)
            {
                var tile = Picture(background, (i < 2 ? "PreviousGenre" : "Genre") + i % 2, "song_select/box/background/0", i % 2 * 2880, 0);
                controller.backgroundTiles[i] = tile;
            }
            controller.wheel = Rect("Wheel", root, 0, 0, 1920, 1080);
            controller.coursePanel = Rect("CoursePanel", root, 0, 0, 1920, 1080);
            Label(root, "KeyHelp", "D / K  えらぶ     F / J  けってい     A  オート", 0, 1020, 1920, 48, 26);
            AddStageFps(root);

            controller.genreBackgrounds = Frames("song_select/box/background", 10);
            controller.boards = Frames("song_select/box/bar_genre", 13);
            controller.cursorGlow = Sprite("song_select/box/bar_genre_overlay");
            controller.branch = Sprite("song_select/yellow_box/branch_indicator");
            controller.plates = Frames("song_select/yellow_box/frame_score", 5);
            controller.levels = Frames("song_select/yellow_box/level_number", 55);
            controller.stars = Frames("song_select/yellow_box/star", 5);
            controller.crownClear = Frames("song_select/yellow_box/crown_clear", 5);
            controller.crownFullCombo = Frames("song_select/yellow_box/crown_fc", 5);
            controller.crownDonderful = Frames("song_select/yellow_box/crown_dfc", 5);
            controller.backboards = Frames("song_select/diff_select/difficulty_back", 10);
            controller.courseBoards = Frames("song_select/diff_select/difficulty_bar", 5);
            controller.courseMarks = Frames("song_select/diff_select/diff_tower_shadow", 5);
            controller.smallCrowns = Frames("song_select/diff_select/difficulty_crown", 4);
            controller.smallStars = Cells("CourseLevel", "song_select/diff_select/difficulty_star", 12, 56, 40);
            controller.courseFrame = Sprite("song_select/diff_select/difficulty_select_bar");
            controller.playerBalloon = Sprite("song_select/diff_select/1p_balloon");
            controller.backButton = Sprite("song_select/diff_select/back_ja");
            controller.optionButton = Sprite("song_select/diff_select/option_ja");
            controller.buttonGlow = Sprite("song_select/yellow_box/s_crown_clear");
            controller.levelBar = Sprite("song_select/diff_select/ura_oni_plate");
            controller.levelDot = Sprite("song_select/diff_select/disable");
            controller.courseBranch = Sprite("song_select/yellow_box/branch_indicator_diff");
            controller.autoIcon = Sprite("song_select/modifier/mod_auto");
            controller.uraChangeToUra = Texture("song_select/yellow_box/s_crown_dfc");
            controller.uraChangeToOni = Texture("song_select/yellow_box/s_crown_fc");
            controller.songBoardTimeline = Timeline("song_board");
            controller.cursorGlowTimeline = Timeline("cursor_glow");
            controller.uraLoopTimeline = Timeline("song_board_ura");
            controller.bgm = Source(controller.transform, "Bgm", 0.8f);
            controller.preview = Source(controller.transform, "Preview", 0.8f);
            controller.sfx = Source(controller.transform, "Sounds");
            controller.voice = Source(controller.transform, "Voice");
            controller.bgm.clip = Clip("song_select/bgm");
            controller.don = Clip("don"); controller.ka = Clip("ka");
            controller.uraSwitch = Clip("song_select/ura_switch");
            controller.voiceEnter = Clip("song_select/voice_enter");
            controller.voiceStartSong = Clip("song_select/voice_start_song_1p");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SongSelectPath);
        }

        static void CreateResultScene()
        {
            var root = NewStage();
            var controller = new GameObject("Result").AddComponent<ResultScene>();
            controller.font = font;
            controller.nameplatePrefab = AssetDatabase.LoadAssetAtPath<NameplateView>(NameplatePrefabPath);
            var stage = Rect("Stage", root, 0, 0, 1920, 1080);
            controller.stage = stage;
            AddStageFps(root);
            const string bg = "result/background/";
            controller.sky = Sprite(bg + "background_1p");
            controller.skyClear = Sprite(bg + "bg_sky_clear");
            controller.fuji = Sprite(bg + "bg_fuji");
            controller.fujiClear = Sprite(bg + "bg_fuji_clear");
            controller.header = Sprite(bg + "bg_header");
            controller.success = Sprite(bg + "footer_1p");
            controller.clouds = Enumerable.Range(0, 8).Select(i => Sprite(bg + "bg_cloud_" + i)).ToArray();
            controller.cloudsClear = Enumerable.Range(0, 8).Select(i => Sprite(bg + "bg_cloud_clear_" + i)).ToArray();
            controller.board = Sprite("result/score/overlay/0");
            controller.donBack = Sprite("result/bottom/chara_0/0");
            controller.judgeLabels = Sprite("result/score/max_combo_ja");
            controller.scoreLabel = Sprite("result/score/score_shinuchi_ja");
            controller.difficulties = Cells("ResultDifficulty", "result/score/difficulty", 5, 320, 96);
            controller.judgeDigits = Cells("ResultJudgeDigit", "result/score/judge_num", 10, 36, 48);
            controller.scoreDigits = Cells("ResultScoreDigit", "result/score/score_num", 20, 64, 82);
            controller.highScore = Sprite("result/score/high_score");
            controller.highScoreDigits = Cells("ResultHighScoreDigit", "result/score/high_score_num", 10, 16, 20);
            string[] arts = { "easy", "normal", "hard" };
            controller.unfilled = arts.Select(a => Sprite("result/gauge/1p_unfilled_" + a)).ToArray();
            controller.overlays = arts.Select(a => Sprite("result/gauge/overlay_" + a)).ToArray();
            controller.rainbow = arts.SelectMany(a => Cells("ResultRainbow" + char.ToUpperInvariant(a[0]) + a.Substring(1), "result/gauge/rainbow_" + a, 8, 1057, 78, true)).ToArray();
            controller.bar = Sprite("result/gauge/1p_bar");
            controller.clearTop = Sprite("result/gauge/bar_clear_top");
            controller.clearBottom = Sprite("result/gauge/bar_clear_bottom");
            controller.clearTransition = Sprite("result/gauge/bar_clear_transition");
            controller.clearCaption = Sprite("result/gauge/clear_ja");
            controller.clearCaptionDark = Sprite("result/gauge/clear_dark_ja");
            controller.soul = Sprite("result/gauge/tamashii");
            controller.soulDark = Sprite("result/gauge/tamashii_dark");
            controller.soulOverlay = Sprite("result/gauge/tamashii_overlay");
            controller.soulFire = Frames("result/gauge/tamashii_fire", 8);
            controller.crowns = new[] { "crown_clear", "crown_fc", "crown_dfc" }.Select(n => Sprite("result/crown/" + n)).ToArray();
            controller.crownFade = Sprite("result/crown/crown_fade");
            controller.gleam = Cells("ResultCrownGleam", "result/crown/gleam", 5, 320, 320);
            controller.messages = Frames("result/crown/message", 4);
            controller.backgroundTimeline = Timeline("result_bg");
            controller.fujiTimeline = Timeline("result_bg_fuji");
            controller.successTimeline = Timeline("result_bg_success");
            controller.crownTimeline = Timeline("crown_mc");
            controller.crownLoopTimeline = Timeline("crown_loop");
            controller.messageTimeline = Timeline("result_msg");
            controller.judgeDigitTimeline = Timeline("result_digit_judge");
            controller.scoreDigitTimeline = Timeline("result_digit_score");
            controller.fireTimeline = Timeline("fever_fire");
            controller.rainbowTimeline = Timeline("gauge_rainbow");
            controller.highScoreTimeline = Timeline("best_score");
            controller.bgm = Source(controller.transform, "Bgm", 0.8f);
            controller.loop = Source(controller.transform, "CountUpLoop");
            controller.sfx = Source(controller.transform, "Sounds");
            controller.voice = Source(controller.transform, "Voice");
            controller.bgm.clip = Clip("result/bgm");
            controller.don = Clip("don");
            controller.donBig = Clip("result/don_big");
            controller.countStop = Clip("result/count_stop");
            controller.countLoop = Clip("result/count_up_loop_c");
            controller.achieve = Clip("result/achieve_tamashii_l");
            controller.atmosClear = Clip("result/atmos_clear_c");
            controller.crownSilver = Clip("result/crown_silver_c");
            controller.crownGold = Clip("result/crown_gold_c");
            controller.crownRainbow = Clip("result/crown_rainbow_c");
            controller.highScoreVoice = Clip("result/high_score_voice_1p");
            controller.fullComboVoice = Clip("result/full_combo_voice_1p");
            controller.messageVoices = new[] { "max_fail_voice_1p", "fail_voice_1p", "clear_voice_1p", "max_clear_voice_1p" }
                .Select(n => Clip("result/" + n)).ToArray();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ResultPath);
        }
    }
}
