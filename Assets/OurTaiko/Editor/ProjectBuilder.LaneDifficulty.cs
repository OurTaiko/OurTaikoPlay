using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        // Player::draw: lane/lane_difficulty frame = difficulty, one 132x96 row each from the top (Easy..Ura).
        static Sprite[] LaneDifficultySprites() => SliceSheet("game/lane/lane_difficulty", Enumerable.Range(0, 5)
            .Select(i => ("LaneDifficulty" + i, 0, i * 96, 132, 96)).ToArray());

        [MenuItem("OurTaiko/Apply Lane Difficulty")]
        public static void ApplyLaneDifficulty()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var sprites = LaneDifficultySprites();
            foreach (string name in new[] { "SinglePlayScene", "PracticeScene" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var icon = play.laneDifficulty != null ? play.laneDifficulty
                    : scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Image>(true))
                        .Single(image => image.name == "Difficulty" && image.transform.parent.name == "NoteLane");
                if (play.laneDifficulty == icon && play.laneDifficultySprites != null && play.laneDifficultySprites.SequenceEqual(sprites)) continue;
                play.laneDifficulty = icon;
                play.laneDifficultySprites = sprites;
                // The saved preview stays Oni; Start shows the chart's own difficulty.
                icon.sprite = sprites[(int)Difficulty.Oni];
                EditorUtility.SetDirty(icon);
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
