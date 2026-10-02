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

        // Creates Entry only when missing, then (re)binds its art, timelines and sounds and makes it
        // the first build scene (SceneSwitcher.MenuScene).
        [MenuItem("OurTaiko/Create Entry Scene")]
        public static void CreateEntryScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportEntryArt();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Generated/Nijiiro SDF.asset");
            if (!File.Exists(EntryPath))
            {
                var root = NewStage();
                var controller = new GameObject("Entry").AddComponent<EntryScene>();
                controller.stage = Rect("Stage", root, 0, 0, 1920, 1080);
                AddStageFps(root);
                controller.bgm = Source(controller.transform, "Bgm", 0.8f);
                controller.sfx = Source(controller.transform, "Sounds");
                controller.voice = Source(controller.transform, "Voice");
                controller.timerVoice = Source(controller.transform, "TimerVoice");
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), EntryPath);
            }
            var scene = EditorSceneManager.OpenScene(EntryPath);
            ConfigureEntry(UnityEngine.Object.FindFirstObjectByType<EntryScene>());
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
            // The guide sheet is 4576x6900: above the usual 4096 cap, and ~126 MB uncompressed.
            var guide = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/" + GuideSheet + ".png");
            if (guide.maxTextureSize != 8192 || guide.textureCompression != TextureImporterCompression.CompressedHQ
                || guide.textureType != TextureImporterType.Sprite)
            {
                guide.textureType = TextureImporterType.Sprite;
                guide.spriteImportMode = SpriteImportMode.Single;
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
            entry.font = font;
            entry.outlineMaterial = OutlineMaterial();
            entry.background = Required("entry/background/bg");
            entry.streetLit = Required("entry/background/street_lit");
            entry.glow = new[] { Required("entry/background/glow/0"), Required("entry/background/glow/1") };
            entry.twinkle = new[] { Required("entry/background/twinkle/0"), Required("entry/background/twinkle/1") };
            entry.creditPill = Required("entry/side_select/credit_pill");
            entry.creditFlash = Required("entry/side_select/credit_flash");
            // mode_select/box frames: 0 = 演奏ゲーム open, 1 = closed, 8 = the white `choose` silhouette
            entry.boardOn = Required("entry/mode_select/box/0");
            entry.boardOff = Required("entry/mode_select/box/1");
            entry.boardFlash = Required("entry/mode_select/box/8");
            entry.boardCursor = Required("entry/mode_select/box_highlight_center");
            entry.overlay = new ArcadeOverlayArt
            {
                timerBackground = Required("global/timer/bg"),
                timerBackgroundRed = Required("global/timer/bg_red"),
                timerHighlight = Required("global/timer/highlight"),
                timerDigitsBlack = Enumerable.Range(0, 10).Select(i => Slice("TimerDigitBlack" + i, "global/timer/counter_black", i * 64, 0, 64, 96)).ToArray(),
                timerDigitsWhite = Enumerable.Range(0, 10).Select(i => Slice("TimerDigitWhite" + i, "global/timer/counter_white", i * 64, 0, 64, 96)).ToArray(),
                guideDecideFrames = Enumerable.Range(GuideFirstDecide, GuideCells - GuideFirstDecide)
                    .Select(c => Slice($"ControlGuide{c:000}", GuideSheet, c % GuideColumns * 352, c / GuideColumns * 276, 352, 276)).ToArray(),
                qrChip = Required("global/overlay/banapass_osaifu_keitai/0"),
                cardChip = Required("global/overlay/banapass_card/0"),
                stageChip = Required("global/overlay/banapass_or/0"),
                danChip = Required("global/overlay/banapass_no"),
                inviteBubble = Required("global/overlay/camera/0"),
                creditSideTimeline = RequiredTimeline("credit_side"),
            };
            entry.nameplatePrefab = AssetDatabase.LoadAssetAtPath<NameplateView>(NameplatePrefabPath);
            entry.backgroundTimeline = RequiredTimeline("entry_bg");
            entry.creditRowTimeline = RequiredTimeline("credit_row");
            entry.creditFadeTimeline = RequiredTimeline("credit_fade");
            entry.modeBoardTimeline = RequiredTimeline("mode_board");
            entry.cursorGlowTimeline = RequiredTimeline("cursor_glow");
            entry.don = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/don.ogg");
            entry.ka = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/ka.ogg");
            entry.cloud = Clip("entry/cloud");
            entry.entryStart = Clip("entry/entry_start_1p");
            entry.selectMode = Clip("entry/select_mode");
            entry.timerBlip = Clip("global/timer_blip");
            entry.timerVoice30 = Clip("global/voice_timer_30");
            entry.timerVoice10 = Clip("global/voice_timer_10");
            entry.timerVoice5 = Clip("global/voice_timer_5");
            entry.bgm.clip = Clip("entry/bgm");
            foreach (var clip in new[] { entry.don, entry.ka, entry.cloud, entry.entryStart, entry.selectMode, entry.timerBlip,
                entry.timerVoice30, entry.timerVoice10, entry.timerVoice5, entry.bgm.clip })
                if (clip == null) throw new FileNotFoundException("An Entry sound is missing.");
            EditorUtility.SetDirty(entry);
        }

        static Sprite Required(string path) => Sprite(path) ?? throw new FileNotFoundException(path);
        static TextAsset RequiredTimeline(string name) => Timeline(name) ?? throw new FileNotFoundException("Animations/" + name + ".txt");
    }
}
