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
        const string EntryPath = "Assets/Scenes/Entry.unity";
        const string GuideSheet = "global/indicator/background";
        // indicator/background: 325 cells of 352x276, 13 per row; cells 210..324 are the decide loop.
        const int GuideColumns = 13, GuideFirstDecide = 210, GuideCells = 325;

        // Creates Entry only when missing, then (re)binds its timelines and sounds, saves the screen
        // hierarchy if the scene has none yet (an existing layout is never rebuilt) and makes it the
        // first build scene (SceneSwitcher.MenuScene).
        [MenuItem("OurTaiko/Create Entry Scene")]
        public static void CreateEntryScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportEntryArt();
            UiFont();
            if (!File.Exists(EntryPath))
            {
                var root = NewStage();
                var controller = new GameObject("Entry").AddComponent<EntryScene>();
                controller.stage = Rect("Stage", root, 0, 0, 1920, 1080);
                AddStageFps(root);
                controller.bgm = Source(controller.transform, "Bgm", 0.8f);
                controller.sfx = Source(controller.transform, "Sounds");
                controller.voice = Source(controller.transform, "Voice");
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), EntryPath);
            }
            var scene = EditorSceneManager.OpenScene(EntryPath);
            var entry = UnityEngine.Object.FindFirstObjectByType<EntryScene>();
            ConfigureEntry(entry);
            if (entry.view == null) BuildEntryLayout(entry);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != EntryPath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(EntryPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: Entry scene is ready and first in Build Settings.");
        }

        static void ImportEntryArt()
        {
            var paths = Directory.GetFiles(Root + "Art/entry", "*.png", SearchOption.AllDirectories)
                .Concat(new[] { "global/timer", "global/overlay/banapass_or", "global/overlay/banapass_card",
                    "global/overlay/banapass_osaifu_keitai", "global/overlay/camera" }
                    .SelectMany(folder => Directory.GetFiles(Root + "Art/" + folder, "*.png")))
                .Append(Root + "Art/global/overlay/banapass_no.png").ToArray();
            ImportSprites(paths);
            // Board frames copied later (9 / 10, ゲーム設定) arrive auto-sliced into trimmed sprites by
            // Unity's default import; each frame is one whole 1160x460 plate.
            foreach (string frame in Directory.GetFiles(Root + "Art/entry/mode_select/box", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(frame.Replace('\\', '/'));
                if (importer.spriteImportMode == SpriteImportMode.Single) continue;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            // The guide sheet is 4576x6900: above the usual 4096 cap, and ~126 MB uncompressed.
            var guide = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/" + GuideSheet + ".png");
            if (guide.maxTextureSize != 8192 || guide.textureCompression != TextureImporterCompression.CompressedHQ
                || guide.textureType != TextureImporterType.Sprite)
            {
                guide.textureType = TextureImporterType.Sprite;
                guide.spritePixelsPerUnit = 100;
                guide.mipmapEnabled = false;
                guide.alphaIsTransparency = true;
                guide.maxTextureSize = 8192;
                guide.textureCompression = TextureImporterCompression.CompressedHQ;
                guide.SaveAndReimport();
            }
        }

        static void ConfigureEntry(EntryScene entry)
        {
            entry.overlay = OverlayArt();
            entry.backgroundTimeline = RequiredTimeline("entry_bg");
            entry.creditRowTimeline = RequiredTimeline("credit_row");
            entry.creditFadeTimeline = RequiredTimeline("credit_fade");
            entry.modeBoardTimeline = RequiredTimeline("mode_board");
            entry.cursorGlowTimeline = RequiredTimeline("cursor_glow");
            entry.modeListTimeline = RequiredTimeline("mode_list");
            entry.don = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/don.ogg");
            entry.ka = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/ka.ogg");
            entry.cloud = Clip("entry/cloud");
            entry.entryStart = Clip("entry/entry_start_1p");
            entry.selectMode = Clip("entry/select_mode");
            entry.bgm.clip = Clip("entry/bgm");
            foreach (var clip in new[] { entry.don, entry.ka, entry.cloud, entry.entryStart, entry.selectMode, entry.bgm.clip })
                if (clip == null) throw new FileNotFoundException("An Entry sound is missing.");
            // The timer is a placeholder (no countdown), so its voice source is gone.
            var timerVoice = entry.transform.Find("TimerVoice");
            if (timerVoice != null) UnityEngine.Object.DestroyImmediate(timerVoice.gameObject);
            EditorUtility.SetDirty(entry);
        }

        // Global chrome art shared by Entry, SongSelect and Result.
        static ArcadeOverlayArt OverlayArt() => new ArcadeOverlayArt
        {
            timerBackground = Required("global/timer/bg"),
            timerDigitsBlack = Enumerable.Range(0, 10).Select(i => Slice("TimerDigitBlack" + i, "global/timer/counter_black", i * 64, 0, 64, 96)).ToArray(),
            guideClip = ControlGuideClip(),
            qrChip = Required("global/overlay/banapass_osaifu_keitai/0"),
            cardChip = Required("global/overlay/banapass_card/0"),
            stageChip = Required("global/overlay/banapass_or/0"),
            danChip = Required("global/overlay/banapass_no"),
            inviteBubble = Required("global/overlay/camera/0"),
            creditSideTimeline = RequiredTimeline("credit_side"),
        };

        // Puts the global chrome on SongSelect (timer placeholder, QR chip, 2P invite) and Result (credit line).
        [MenuItem("OurTaiko/Apply Global Overlays")]
        public static void ApplyGlobalOverlays()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportEntryArt();
            foreach (var path in new[] { SongSelectPath, ResultPath })
            {
                var scene = EditorSceneManager.OpenScene(path);
                var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
                var result = UnityEngine.Object.FindFirstObjectByType<ResultScene>();
                if (select != null) { select.overlay = OverlayArt(); EditorUtility.SetDirty(select); }
                if (result != null) { result.overlay = OverlayArt(); EditorUtility.SetDirty(result); }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: global overlays applied to SongSelect and Result.");
        }

        static Sprite Required(string path) => Sprite(path) ?? throw new FileNotFoundException(path);
        static TextAsset RequiredTimeline(string name) => Timeline(name) ?? throw new FileNotFoundException("Animations/" + name + ".txt");
    }
}
