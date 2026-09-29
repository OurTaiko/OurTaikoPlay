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
    public static class ProjectBuilder
    {
        const string Root = "Assets/OurTaiko/";
        static TMP_FontAsset font;

        public static void ImportTextResources()
        {
            AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += () => EditorApplication.Exit(0);
            AssetDatabase.importPackageFailed += (_, error) => { Debug.LogError(error); EditorApplication.Exit(1); };
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        // Deliberate explicit command: never regenerates on reload or overwrites scene edits automatically.
        [MenuItem("OurTaiko/Generate Initial Scenes")]
        public static void Generate()
        {
            if (File.Exists("Assets/Scenes/PlayScene.unity"))
                throw new InvalidOperationException("Scenes already exist. Edit the existing hierarchy instead of regenerating it.");
            Directory.CreateDirectory(Root + "Generated");
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Root + "Art", "*.png", SearchOption.AllDirectories))
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
            font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(Root + "Art/Taiko.ttf"));
            font.name = "Taiko SDF";
            AssetDatabase.CreateAsset(font, Root + "Generated/Taiko SDF.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.,!?+-()_★");
            EditorUtility.SetDirty(font);
            var triple = Song("TripleHelix", "Oni", true);
            var calibration = Song("Calibration", "Hard", false);
            CreatePlay(triple);
            CreateMenu(new[] { triple, calibration });
            EditorBuildSettings.scenes = new[] {
                new EditorBuildSettingsScene("Assets/Scenes/SceneSwitcher.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/PlayScene.unity", true)
            };
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            PlayerSettings.companyName = "OurTaiko";
            PlayerSettings.productName = "OurTaikoPlayerUnity";
            PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/SceneSwitcher.unity");
            Debug.Log("OurTaiko: Created SceneSwitcher and PlayScene, songs, sprite slices and font.");
        }

        static SongDefinition Song(string name, string course, bool audio)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Songs/" + name + ".txt");
            song.music = audio ? AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Songs/" + name + ".ogg") : null;
            song.course = course; song.Parse();
            AssetDatabase.CreateAsset(song, Root + "Songs/" + name + ".asset"); return song;
        }

        static Transform NewScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 3.6f;
            camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            var viewport = Rect("Viewport1280x720", canvas.transform, 0, 0, 1280, 720);
            viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(0.5f, 0.5f); viewport.anchoredPosition = Vector2.zero;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            return viewport;
        }
        static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
        }
        static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/" + path + ".png");
        static UnityEngine.UI.Image Picture(Transform parent, string name, string path, float x, float y, float w = 0, float h = 0)
        {
            var sprite = Sprite(path);
            if (sprite == null) throw new FileNotFoundException(path);
            var r = Rect(name, parent, x, y, w > 0 ? w : sprite.rect.width, h > 0 ? h : sprite.rect.height);
            var image = r.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = sprite; image.raycastTarget = false; return image;
        }
        static UnityEngine.UI.Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        static TMP_Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = value; label.fontSize = size; label.alignment = align;
            label.color = Color.white; label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.outlineWidth = 0.15f; label.outlineColor = new Color32(25, 17, 27, 255);
            return label;
        }
        static UnityEngine.UI.Button Button(Transform parent, string name, string caption, float x, float y, float w, float h, Color color)
        {
            var background = Panel(parent, name, x, y, w, h, color); background.raycastTarget = true;
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
            var colors = button.colors; colors.highlightedColor = new Color(1, 0.87f, 0.68f); colors.pressedColor = new Color(0.7f, 0.7f, 0.7f); button.colors = colors;
            Label(background.transform, "Label", caption, 0, 0, w, h, 20); return button;
        }
        static Sprite Slice(string name, string path, int x, int y, int width, int height)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/" + path + ".png");
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(x, texture.height - y - height, width, height), Vector2.one * 0.5f, 100);
            sprite.name = name; AssetDatabase.CreateAsset(sprite, Root + "Generated/" + name + ".asset"); return sprite;
        }
        static void Background(Transform root)
        {
            Panel(root, "Backdrop", 0, 0, 1280, 720, new Color32(165, 44, 36, 255));
            for (int i = 0; i < 4; i++) Picture(root, "HeaderPattern" + i, "background/donbg/0_1/background/0", i * 328, 0);
            Picture(root, "FestivalBackground", "background/bg_normal/bg_0/background", 0, 360);
            Picture(root, "FestivalLights", "background/bg_normal/bg_0/overlay", 0, 360);
            Picture(root, "Footer", "background/footer/0", 0, 656);
        }

        static void CreatePlay(SongDefinition defaultSong)
        {
            var root = NewScene(); Background(root);
            var controller = new GameObject("PlayScene").AddComponent<PlayScene>(); controller.defaultSong = defaultSong;
            controller.music = new GameObject("Music").AddComponent<AudioSource>(); controller.music.transform.SetParent(controller.transform); controller.music.playOnAwake = false; controller.music.volume = 0.8f;
            controller.hitAudio = new GameObject("Hitsounds").AddComponent<AudioSource>(); controller.hitAudio.transform.SetParent(controller.transform); controller.hitAudio.playOnAwake = false;
            controller.don = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/don.ogg"); controller.ka = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/ka.ogg");
            controller.title = Label(root, "SongTitle", "TRIPLE HELIX", 510, 18, 742, 48, 38, TextAlignmentOptions.Right);
            controller.subtitle = Label(root, "SongSubtitle", "Yonokid", 510, 68, 742, 32, 22, TextAlignmentOptions.Right);
            Label(root, "PlayerName", "OURTAIKO  /  PLAYER 1", 24, 30, 400, 46, 28, TextAlignmentOptions.Left);
            controller.state = Label(root, "PlayState", "READY", 24, 83, 290, 38, 25, TextAlignmentOptions.Left);
            var gauge = Rect("SoulGauge", root, 0, 184, 1280, 80);
            Picture(gauge, "Border", "game/gauge/border_hard", 327, -52);
            Picture(gauge, "Empty", "game/gauge/1p_unfilled_hard", 483, -60);
            controller.gaugeFill = Picture(gauge, "Fill", "game/gauge/1p_bar", 491, -24, 694, 24);
            controller.gaugeFill.type = UnityEngine.UI.Image.Type.Filled; controller.gaugeFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; controller.gaugeFill.fillAmount = 0;
            Picture(gauge, "Grid", "game/gauge/overlay_hard", 483, -60);
            Picture(gauge, "ClearMarker", "game/gauge/clear_en", 1038, -43);
            Picture(gauge, "Soul", "game/gauge/tamashii", 1187, -54);
            var lane = Rect("NoteLane", root, 0, 184, 1280, 176);
            Picture(lane, "LaneBackground", "game/lane/lane_background", 332, 0, 948, 176);
            var gogo = Panel(lane, "GogoTint", 332, 8, 948, 128, new Color(1, 0.25f, 0)); controller.gogoTint = gogo.gameObject.AddComponent<CanvasGroup>(); controller.gogoTint.alpha = 0;
            Picture(lane, "JudgeCircle", "game/lane/lane_hit_circle", 342, 0);
            var clip = Rect("LaneClip", lane, 332, 0, 948, 144); clip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            controller.barLayer = Rect("MeasureLines", clip, 0, 0, 948, 144);
            controller.noteLayer = Rect("Notes", clip, 0, 0, 948, 144);
            controller.noteSprites = new Sprite[10];
            for (int i = 1; i <= 7; i++) controller.noteSprites[i] = Slice("Note" + i, "game/notes/notes_atlas", 0, 136 + (i - 1) * 128, 128, 128);
            controller.noteSprites[9] = Slice("Note9", "game/notes/notes_atlas", 0, 1160, 128, 128);
            Picture(lane, "PlayerCover", "game/lane/1p_lane_cover", 0, 0);
            Picture(lane, "PlayerIcon", "game/lane/1p_icon", 0, 41);
            var diff = Picture(lane, "Difficulty", "game/lane/lane_difficulty", 50, 38, 88, 64); diff.sprite = Slice("OniDifficulty", "game/lane/lane_difficulty", 0, 192, 88, 64);
            Picture(lane, "Drum", "game/lane/drum", 211, 22);
            string[] drumNames = { "drum_don_l", "drum_don_r", "drum_kat_l", "drum_kat_r" };
            controller.drumFlashes = drumNames.Select(n => Picture(lane, n, "game/lane/" + n, 211, 22)).ToArray();
            foreach (var flash in controller.drumFlashes) flash.enabled = false;
            controller.score = Label(lane, "Score", "0000000", 7, 137, 195, 36, 29);
            controller.combo = Label(lane, "Combo", "", 143, 22, 64, 96, 31);
            controller.hitFlash = Picture(lane, "HitFlash", "game/hit_effect/hit_effect_good", 342, 0); controller.hitFlash.color = Color.clear;
            controller.judgmentSprites = new[] { Sprite("game/hit_effect/judge_good"), Sprite("game/hit_effect/judge_ok"), Sprite("game/hit_effect/judge_bad") };
            controller.judgment = Picture(lane, "Judgment", "game/hit_effect/judge_good", 370, -40); controller.judgment.color = Color.clear;
            controller.rollCounter = Label(root, "RollCounter", "", 930, 365, 330, 40, 25);
            controller.counters = Label(root, "JudgmentCounters", "GOOD 0   OK 0   BAD 0", 340, 336, 912, 26, 18);
            var frames = Enumerable.Range(0, 14).Select(i => Sprite("background/dancer/dancer_0/0_loop/" + i)).ToArray();
            controller.dancers = new SpriteFlipbook[5];
            for (int i = 0; i < 5; i++)
            {
                var dancer = Picture(root, "Dancer" + (i + 1), "background/dancer/dancer_0/0_loop/0", 30 + i * 250, 415);
                controller.dancers[i] = dancer.gameObject.AddComponent<SpriteFlipbook>(); controller.dancers[i].frames = frames;
            }
            Label(root, "KeyHelp", "D / K  KA     F / J  DON     SPACE  PAUSE     F1  RESTART     ESC  BACK", 0, 674, 1280, 30, 18);
            controller.pauseButton = Button(root, "PauseButton", "PAUSE", 26, 370, 110, 34, new Color32(33, 37, 39, 235));
            controller.restartButton = Button(root, "RestartButton", "RESTART", 146, 370, 125, 34, new Color32(33, 37, 39, 235));
            controller.backButton = Button(root, "BackButton", "BACK", 281, 370, 100, 34, new Color32(33, 37, 39, 235));
            for (int i = 0; i < 4; i++)
            {
                bool isKa = i == 0 || i == 3;
                var pad = Button(root, "TouchPad" + i, new[] { "D / KA", "F / DON", "J / DON", "K / KA" }[i], 394 + i * 130, 612, 120, 40, isKa ? new Color32(28, 106, 125, 245) : new Color32(185, 50, 35, 245));
                var input = pad.gameObject.AddComponent<DrumPad>(); input.controller = controller; input.ka = isKa; input.right = i >= 2;
            }
            controller.pausePanel = Overlay(root, "PausePanel", "PAUSED", out _, out var resume, out var unused);
            controller.resumeButton = resume; resume.GetComponentInChildren<TMP_Text>().text = "RESUME";
            unused.gameObject.SetActive(false); controller.pausePanel.SetActive(false);
            controller.resultPanel = Overlay(root, "ResultPanel", "FINISHED", out controller.resultText, out controller.resultRestart, out controller.resultBack);
            controller.resultPanel.SetActive(false);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/PlayScene.unity");
        }

        static GameObject Overlay(Transform root, string name, string caption, out TMP_Text label, out UnityEngine.UI.Button primary, out UnityEngine.UI.Button secondary)
        {
            var shade = Panel(root, name, 0, 0, 1280, 720, new Color(0.03f, 0.03f, 0.04f, 0.87f)); shade.raycastTarget = true;
            label = Label(shade.transform, "Title", caption, 150, 180, 980, 260, 52);
            primary = Button(shade.transform, "Primary", "RESTART", 390, 484, 240, 58, new Color32(216, 65, 42, 255));
            secondary = Button(shade.transform, "Back", "BACK", 650, 484, 240, 58, new Color32(35, 121, 141, 255));
            return shade.gameObject;
        }

        static void CreateMenu(SongDefinition[] songs)
        {
            var root = NewScene(); Background(root);
            var menu = new GameObject("LaunchMenu").AddComponent<LaunchMenu>(); menu.songs = songs;
            new GameObject("SceneSwitcher").AddComponent<SceneSwitcher>();
            Panel(root, "MenuPanel", 220, 90, 840, 520, new Color32(28, 29, 32, 238));
            Label(root, "Heading", "OURTAIKO", 280, 120, 720, 75, 64);
            Label(root, "Caption", "PLAY SCENE", 280, 195, 720, 34, 24);
            menu.selection = Label(root, "SelectedSong", "", 280, 262, 720, 94, 36);
            menu.playButton = Button(root, "PlayButton", "PLAY  /  ENTER", 390, 383, 500, 62, new Color32(216, 65, 42, 255));
            menu.songButton = Button(root, "SongButton", "CHANGE SONG  /  TAB", 310, 468, 320, 48, new Color32(43, 98, 117, 255));
            menu.autoButton = Button(root, "AutoButton", "", 650, 468, 320, 48, new Color32(74, 86, 66, 255));
            menu.mode = menu.autoButton.GetComponentInChildren<TMP_Text>();
            Label(root, "Controls", "F / J  DON     D / K  KA     SPACE  PAUSE\nF1  RESTART     ESC  BACK     A  AUTO", 270, 535, 740, 55, 19);
            Label(root, "FooterCredit", "OurTaikoPlayer  /  PyTaikoGreen", 0, 674, 1280, 30, 19);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/SceneSwitcher.unity");
        }

        public static void BuildMac()
        {
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, "Builds/OurTaikoPlayerUnity.app", BuildTarget.StandaloneOSX, BuildOptions.Development);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed.");
        }
    }
}
