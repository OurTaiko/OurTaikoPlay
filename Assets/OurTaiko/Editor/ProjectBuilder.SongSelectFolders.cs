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
        public const string FolderBoardPrefabPath = Root + "Generated/FolderBoard.prefab";
        const string FolderArt = "song_select/box/";

        // Online category folders in SongSelect: imports the Nijiiro folder art (folder_graphic and
        // bar_genre_back as 3-slice boards, box_chara cut into its left/right halves), creates
        // Generated/FolderBoard.prefab when missing, and binds both to the saved SongSelect scene.
        // Repeated runs change nothing.
        [MenuItem("OurTaiko/Apply Song Select Folders")]
        public static void ApplySongSelectFolders()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            foreach (string path in Directory.GetFiles(Root + "Art/" + FolderArt + "folder_graphic", "*.png").Append(Root + "Art/" + FolderArt + "bar_genre_back.png"))
                ImportBoardArt(path.Replace('\\', '/'), new Vector4(0, 56, 0, 56));
            foreach (string path in Directory.GetFiles(Root + "Art/" + FolderArt + "box_chara", "*.png"))
                ImportBoardArt(path.Replace('\\', '/'), Vector4.zero, true);
            var left = new Sprite[10];
            var right = new Sprite[10];
            for (int i = 0; i < 10; i++)
            {
                var halves = SliceSheet(FolderArt + "box_chara/" + i, new[] { ("BoxChara" + i + "L", 0, 0, 960, 480), ("BoxChara" + i + "R", 960, 0, 960, 480) });
                left[i] = halves[0]; right[i] = halves[1];
            }
            var folderBoards = Frames(FolderArt + "folder_graphic", 13);
            var backBoard = Sprite(FolderArt + "bar_genre_back") ?? throw new FileNotFoundException("bar_genre_back");
            var timeline = Timeline("folder_board") ?? throw new FileNotFoundException("Animations/folder_board.txt is missing.");

            var scene = EditorSceneManager.OpenScene(SongSelectPath);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            bool changed = select.backBoard != backBoard || select.folderBoardTimeline != timeline
                || select.folderBoards == null || !select.folderBoards.SequenceEqual(folderBoards)
                || select.charaLeft == null || !select.charaLeft.SequenceEqual(left) || select.charaRight == null || !select.charaRight.SequenceEqual(right);
            select.folderBoards = folderBoards;
            select.charaLeft = left; select.charaRight = right;
            select.backBoard = backBoard;
            select.folderBoardTimeline = timeline;
            var prefab = AssetDatabase.LoadAssetAtPath<FolderBoardView>(FolderBoardPrefabPath) ?? CreateFolderBoardPrefab(select);
            // Old assets may have been created from Unity's automatic trimmed Multiple sprites,
            // which lose the full-image borders. Persist the corrected art and image modes too.
            var contents = PrefabUtility.LoadPrefabContents(FolderBoardPrefabPath);
            try
            {
                var folderView = contents.GetComponent<FolderBoardView>();
                if (folderView.panelOpen.sprite != folderBoards[0] || folderView.panelOpen.type != UnityEngine.UI.Image.Type.Sliced
                    || folderView.panelClosed.type != UnityEngine.UI.Image.Type.Sliced)
                {
                    folderView.panelOpen.sprite = folderBoards[0];
                    folderView.panelOpen.type = folderView.panelClosed.type = UnityEngine.UI.Image.Type.Sliced;
                    PrefabUtility.SaveAsPrefabAsset(contents, FolderBoardPrefabPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            changed |= select.view.folderPrefab != prefab;
            if (changed)
            {
                select.view.folderPrefab = prefab;
                EditorUtility.SetDirty(select.view);
                EditorUtility.SetDirty(select);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: SongSelect folders are ready.");
        }

        static void ImportBoardArt(string path, Vector4 border, bool multiple = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new FileNotFoundException(path);
            // Only character sheets are Multiple. Panel artwork must keep its full 960x352
            // extent and 56-unit caps, including when Unity auto-sliced it on its first import.
            var mode = multiple ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == mode && importer.spriteBorder == border
                && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = mode;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.spriteBorder = border;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        // Same footprint as SongBoard.prefab (960x164 closed, +188 open): glow, folder_graphic under
        // bar_genre, the two characters, the title and the song count.
        static FolderBoardView CreateFolderBoardPrefab(SongSelectScene select)
        {
            var root = SkinUi.Rect("FolderBoard", null);
            var board = root.gameObject.AddComponent<FolderBoardView>();
            board.group = root.GetComponent<CanvasGroup>();
            board.group.blocksRaycasts = true;
            board.glow = SkinUi.Image("CursorGlow", root, select.cursorGlow, 1024, 212);
            board.glow.enabled = false;
            board.panelOpen = SkinUi.Image("FolderBoard", root, select.folderBoards[0], 960, 164);
            board.panelOpen.enabled = false;
            board.panelClosed = SkinUi.Image("Board", root, select.boards[0], 960, 164);
            // Either panel takes the tap (the closed one fades out on an open folder); the relay is on the root.
            board.panelOpen.raycastTarget = board.panelClosed.raycastTarget = true;
            board.click = root.gameObject.AddComponent<PointerRelay>();
            board.charaLeft = SkinUi.Image("CharacterLeft", root, select.charaLeft[0], 960, 480);
            board.charaRight = SkinUi.Image("CharacterRight", root, select.charaRight[0], 960, 480);
            board.charaLeft.enabled = board.charaRight.enabled = false;
            board.title = SkinUi.Text("Title", root, 42);
            board.title.text = "フォルダ";
            board.count = SkinUi.Text("Count", root, 32);
            board.count.text = "0 songs";
            board.count.enabled = false;
            PersistSongSelectTextMaterials(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, FolderBoardPrefabPath);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return prefab.GetComponent<FolderBoardView>();
        }
    }
}
