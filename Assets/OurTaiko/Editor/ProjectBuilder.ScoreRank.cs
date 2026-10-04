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
        public const string ScoreRankFolder = Root + "Generated/ScoreRank/";
        static readonly int[] RankShapes = { 74, 76, 78, 80, 82, 84, 86 };
        static readonly string[] RankNames = { "White", "Bronze", "Silver", "Gold", "Sui", "Miyabi", "Kiwami" };

        [MenuItem("OurTaiko/Apply Score Rank")]
        public static void ApplyScoreRank()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                ImportScoreRanks();
                EnsureRankFolder(ScoreRankFolder.TrimEnd('/'));
                for (int rank = 1; rank <= ScoreRank.Count; rank++) BuildRankIcon(rank);
                string boardPath = Root + "Generated/SongBoard.prefab";
                var boardRoot = PrefabUtility.LoadPrefabContents(boardPath);
                try
                {
                    var board = boardRoot.GetComponent<SongBoardView>();
                    if (board.scoreRank == null)
                    {
                        AttachBoardRank(board);
                        PrefabUtility.SaveAsPrefabAsset(boardRoot, boardPath);
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(boardRoot); }
                var songScene = EditorSceneManager.OpenScene(SongSelectPath);
                var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
                bool changed = false;
                foreach (var board in select.view.songBoards)
                    if (board.scoreRank == null) { AttachBoardRank(board); changed = true; }
                foreach (var card in select.view.cards)
                    if (card.scoreRank == null)
                    {
                        card.scoreRank = CreateRankView(card.board.transform, 40);
                        ((RectTransform)card.scoreRank.transform).Center(80, 42);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(card.scoreRank.transform);
                        changed = true;
                    }
                if (changed)
                {
                    EditorUtility.SetDirty(select.view);
                    EditorSceneManager.MarkSceneDirty(songScene);
                    EditorSceneManager.SaveScene(songScene);
                }
                var resultScene = EditorSceneManager.OpenScene(ResultPath);
                var result = UnityEngine.Object.FindFirstObjectByType<ResultScene>();
                changed = false;
                if (result.view.scoreRank == null)
                {
                    var view = result.view.scoreRank = CreateRankView(result.stage, 208);
                    ((RectTransform)view.transform).Center(181.1f, 500.7f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view.transform);
                    view.transform.SetSiblingIndex(result.view.crown.transform.GetSiblingIndex());
                    var animation = view.animationView = view.gameObject.AddComponent<ScoreRankAnimation>();
                    animation.timeline = Timeline("rank_result");
                    animation.kiwamiTimeline = Timeline("rank_result_kiwami");
                    var normal = ScoreRankAnimation.Frames.Parse(animation.timeline.text);
                    var kiwami = ScoreRankAnimation.Frames.Parse(animation.kiwamiTimeline.text);
                    animation.shapeIds = new[] { 36, 46 };
                    animation.sprites = animation.shapeIds.Select(id => Sprite("game/rank_result_anim/s" + id)).ToArray();
                    animation.additive = AdditiveUiMaterial();
                    int slots = Math.Max(normal.FramesByIndex.Max(f => f.Length), kiwami.FramesByIndex.Max(f => f.Length));
                    animation.layers = Enumerable.Range(0, slots).Select(i =>
                    {
                        var layer = SkinUi.Image("Layer" + i, view.transform, null);
                        layer.rectTransform.anchorMin = layer.rectTransform.anchorMax = Vector2.one * .5f;
                        layer.raycastTarget = false;
                        return layer;
                    }).ToArray();
                    view.Show(6);
                    view.image.enabled = false;
                    EditorUtility.SetDirty(result.view);
                    changed = true;
                }
                var sound = Clip("result/scorerank_c");
                if (result.rankSound != sound) { result.rankSound = sound; EditorUtility.SetDirty(result); changed = true; }
                if (changed) { EditorSceneManager.MarkSceneDirty(resultScene); EditorSceneManager.SaveScene(resultScene); }
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static void EnsureRankFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureRankFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ImportScoreRanks()
        {
            string skin = Path.GetFullPath("../OurTaikoPlayer/Skins/YataiDONNijiiro");
            foreach (string dir in new[] { "game/rank_result_anim" })
                foreach (string source in Directory.GetFiles(Path.Combine(skin, "Graphics", dir), "*.png"))
                {
                    int shape = int.Parse(Path.GetFileNameWithoutExtension(source).Substring(1));
                    if (!RankShapes.Contains(shape) && shape != 36 && shape != 46) continue;
                    string path = Root + "Art/" + dir + "/" + Path.GetFileName(source);
                    if (File.Exists(path)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(source, path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }
            foreach (string name in new[] { "rank_result", "rank_result_kiwami" })
            {
                string path = Root + "Animations/" + name + ".txt";
                if (!File.Exists(path)) File.Copy(Path.Combine(skin, "Scripts/game/ending_anim/" + name + ".lua"), path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            string audio = Root + "Audio/result/scorerank_c.ogg";
            if (!File.Exists(audio)) File.Copy(Path.Combine(skin, "Sounds/result/scorerank_c.ogg"), audio);
            AssetDatabase.ImportAsset(audio, ImportAssetOptions.ForceSynchronousImport);
        }

        static string RankPrefabPath(int rank) => ScoreRankFolder + rank + "_" + RankNames[rank - 1] + ".prefab";

        static void BuildRankIcon(int rank)
        {
            string path = RankPrefabPath(rank);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var image = SkinUi.Image(RankNames[rank - 1], null, Sprite("game/rank_result_anim/s" + RankShapes[rank - 1]), 208, 208);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = Vector2.one * .5f;
            image.rectTransform.anchoredPosition = Vector2.zero;
            image.raycastTarget = false;
            image.preserveAspect = true;
            try { PrefabUtility.SaveAsPrefabAsset(image.gameObject, path); }
            finally { UnityEngine.Object.DestroyImmediate(image.gameObject); }
        }

        static ScoreRankView CreateRankView(Transform parent, int size)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(RankPrefabPath(6));
            var icon = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            icon.name = "ScoreRank";
            var view = icon.AddComponent<ScoreRankView>();
            view.image = icon.GetComponent<UnityEngine.UI.Image>();
            view.image.rectTransform.sizeDelta = Vector2.one * size;
            view.image.rectTransform.anchorMin = view.image.rectTransform.anchorMax = new Vector2(0, 1);
            view.icons = Enumerable.Range(1, ScoreRank.Count).Select(r => AssetDatabase.LoadAssetAtPath<GameObject>(RankPrefabPath(r))).ToArray();
            view.group = icon.AddComponent<CanvasGroup>();
            view.group.blocksRaycasts = view.group.interactable = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(icon);
            PrefabUtility.RecordPrefabInstancePropertyModifications(view.transform);
            return view;
        }

        static void AttachBoardRank(SongBoardView board)
        {
            board.scoreRank = CreateRankView(board.transform, 72);
            ((RectTransform)board.scoreRank.transform).Center(-425, 27);
            PrefabUtility.RecordPrefabInstancePropertyModifications(board.scoreRank.transform);
            EditorUtility.SetDirty(board);
            if (PrefabUtility.IsPartOfPrefabInstance(board)) PrefabUtility.RecordPrefabInstancePropertyModifications(board);
        }
    }
}
