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
            if (File.Exists("Assets/Scenes/SinglePlayScene.unity"))
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
            font.name = "Nijiiro SDF";
            AssetDatabase.CreateAsset(font, Root + "Generated/Nijiiro SDF.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.,!?+-()_★");
            EditorUtility.SetDirty(font);
            var triple = Song("TripleHelix", "Oni", true);
            var calibration = Song("Calibration", "Hard", false);
            var branchTraining = Song("BranchTraining", "Oni", false);
            CreatePlay(triple);
            CreateSceneSwitcherPrefab();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/SinglePlayScene.unity", true) };
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            PlayerSettings.companyName = "OurTaiko";
            PlayerSettings.productName = "OurTaikoPlayerUnity";
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            Debug.Log("OurTaiko: Created SinglePlayScene, global SceneSwitcher, songs, sprite slices and font.");
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
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            // Menu controls are authored on the original grid, then expanded once before saving.
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
            sprite.name = name;
            var pathName = Root + "Generated/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(pathName);
            if (existing == null) { AssetDatabase.CreateAsset(sprite, pathName); return sprite; }
            EditorUtility.CopySerialized(sprite, existing);
            UnityEngine.Object.DestroyImmediate(sprite);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
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
            var gauge = Rect("SoulGauge", root, 0, 184, 1280, 80);
            Picture(gauge, "Border", "game/gauge/border_hard", 327, -52);
            Picture(gauge, "Empty", "game/gauge/1p_unfilled_hard", 483, -60);
            Picture(gauge, "Fill", "game/gauge/1p_bar", 491, -24, 694, 24);
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
            controller.combo = Label(lane, "Combo", "", 143, 22, 64, 96, 31);
            controller.hitFace = PlaceHitFace(lane, 342, 0);
            controller.hitRing = PlaceHitRing(lane, 282, -60);
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
            Label(root, "KeyHelp", "D / K  KA     F / J  DON     SPACE / ESC  PAUSE     F1  RESTART", 0, 674, 1280, 30, 18);
            controller.pauseButton = Button(root, "PauseButton", "PAUSE", 26, 370, 110, 34, new Color32(33, 37, 39, 235));
            controller.restartButton = Button(root, "RestartButton", "RESTART", 146, 370, 125, 34, new Color32(33, 37, 39, 235));
            controller.backButton = Button(root, "BackButton", "BACK", 281, 370, 100, 34, new Color32(33, 37, 39, 235));
            controller.pausePanel = Overlay(root, "PausePanel", "PAUSED", out _, out var resume, out var unused);
            controller.resumeButton = resume; resume.GetComponentInChildren<TMP_Text>().text = "RESUME";
            unused.gameObject.SetActive(false); controller.pausePanel.SetActive(false);
            controller.resultPanel = Overlay(root, "ResultPanel", "FINISHED", out controller.resultText, out controller.resultRestart, out controller.resultBack);
            controller.resultPanel.SetActive(false);
            AddBranchIndicator(root, controller);
            AddFpsCounter(root);
            ConfigureNijiiroLayout(root, controller);
            ConfigureTouchDrum(controller);
            ConfigurePauseMenu(controller);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/SinglePlayScene.unity");
        }

        static GameObject Overlay(Transform root, string name, string caption, out TMP_Text label, out UnityEngine.UI.Button primary, out UnityEngine.UI.Button secondary)
        {
            var shade = Panel(root, name, 0, 0, 1280, 720, new Color(0.03f, 0.03f, 0.04f, 0.87f)); shade.raycastTarget = true;
            label = Label(shade.transform, "Title", caption, 150, 180, 980, 260, 52);
            primary = Button(shade.transform, "Primary", "RESTART", 390, 484, 240, 58, new Color32(216, 65, 42, 255));
            secondary = Button(shade.transform, "Back", "BACK", 650, 484, 240, 58, new Color32(35, 121, 141, 255));
            return shade.gameObject;
        }

        public static void AddBranchIndicator(Transform root, PlayScene controller)
        {
            var lane = root.Find("NoteLane");
            if (lane == null) throw new InvalidOperationException("NoteLane must exist before adding branch visuals.");
            var existing = lane.Find("BranchIndicator");
            if (existing != null) { controller.branchLane = existing.GetComponent<BranchLaneView>(); return; }
            var branch = Rect("BranchIndicator", lane, 0, 0, 1280, 176);
            branch.SetSiblingIndex(lane.Find("LaneBackground").GetSiblingIndex() + 1);
            var view = branch.gameObject.AddComponent<BranchLaneView>();
            view.normalLabel = Sprite("game/branch/normal");
            view.expertLabel = Sprite("game/branch/expert");
            view.masterLabel = Sprite("game/branch/master");
            view.expertBackground = Sprite("game/branch/expert_bg");
            view.masterBackground = Sprite("game/branch/master_bg");
            view.levelUp = Sprite("game/branch/level_up");
            view.levelDown = Sprite("game/branch/level_down");
            view.background = Picture(branch, "RouteBackground", "game/branch/expert_bg", 332, 1, 948, 136);
            view.levelChange = Picture(branch, "LevelChange", "game/branch/level_up", 163, -56);
            // Match draw_texture(center=true): scaling keeps the unscaled sprite center fixed.
            view.levelChange.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.levelChange.rectTransform.anchoredPosition += new Vector2(88, -28);
            view.previousLabel = Picture(branch, "PreviousRoute", "game/branch/normal", 1071, 43);
            view.currentLabel = Picture(branch, "CurrentRoute", "game/branch/normal", 1071, 43);
            view.background.enabled = view.previousLabel.enabled = view.levelChange.enabled = false;
            controller.branchLane = view;
            branch.gameObject.SetActive(false);
        }

        public static void AddFpsCounter(Transform root)
        {
            if (root.Find("FpsPanel") != null) return;
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Generated/Nijiiro SDF.asset");
            var panel = Panel(root, "FpsPanel", 24, 132, 148, 30, new Color32(28, 29, 32, 225));
            var label = Label(panel.transform, "FpsCounter", "FPS --", 8, 0, 132, 30, 18, TextAlignmentOptions.Left);
            label.gameObject.AddComponent<FpsCounter>();
        }

        public static void BuildMac()
        {
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, "Builds/OurTaikoPlayerUnity.app", BuildTarget.StandaloneOSX, BuildOptions.Development);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed.");
        }
    }
}
