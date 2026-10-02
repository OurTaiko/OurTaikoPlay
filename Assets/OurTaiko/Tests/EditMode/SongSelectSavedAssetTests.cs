using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class SongSelectSavedAssetTests
    {
        const string ScenePath = "Assets/Scenes/SongSelect.unity";
        const string BoardPath = "Assets/OurTaiko/Generated/SongBoard.prefab";
        const string OptionsPath = "Assets/OurTaiko/Generated/PlayOptions.prefab";

        [Test]
        public void SongSelectContainsItsCompleteLayoutBeforeEnteringPlayMode()
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var select = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SongSelectScene>(true)).Single();
                var view = select.view;
                Assert.That(view, Is.Not.Null, "The scene stores its view rather than constructing it in Awake.");
                Assert.That(view.gameObject.scene, Is.EqualTo(scene));
                Assert.That(view.songBoards.Length, Is.EqualTo(3));
                Assert.That(view.songBoards.Select(board => board.song), Is.EqualTo(select.songs));
                Assert.That(view.songBoards.Distinct().Count(), Is.EqualTo(3));
                foreach (var board in view.songBoards)
                {
                    Assert.That(board.gameObject.scene, Is.EqualTo(scene));
                    AssertReferences(new SerializedObject(board), "group", "contents", "glow", "panel", "crown", "title", "subtitle", "click");
                    Assert.That(board.plates.Select(plate => plate.difficulty), Is.EquivalentTo(new[]
                    {
                        Difficulty.Easy, Difficulty.Normal, Difficulty.Hard, Difficulty.Oni, Difficulty.Ura,
                    }), "Saved plates cover every course; runtime only changes their contents and visibility.");
                }
                var serializedView = new SerializedObject(view);
                AssertReferences(serializedView, "boardPrefab", "courseGroup", "mark", "backboard", "back", "option", "auto", "frame", "glow",
                    "balloon", "uraChange", "header", "headerSub", "options", "nameplate", "overlays");
                var cards = serializedView.FindProperty("cards");
                Assert.That(cards.arraySize, Is.EqualTo(4));
                for (int i = 0; i < cards.arraySize; i++)
                {
                    var card = cards.GetArrayElementAtIndex(i);
                    foreach (var name in new[] { "board", "crown", "star", "level", "bar", "branch", "name", "click" })
                        Assert.That(card.FindPropertyRelative(name).objectReferenceValue, Is.Not.Null, $"Course {i}: {name}");
                    Assert.That(card.FindPropertyRelative("dots").arraySize, Is.EqualTo(10));
                }
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.options), Is.EqualTo(OptionsPath));
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero, child.name);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void SongBoardAndOptionsAreEditablePrefabsWithSavedReferences()
        {
            var board = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPath);
            Assert.That(board, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetType(board), Is.EqualTo(PrefabAssetType.Regular));
            Assert.That(board.GetComponent<SongBoardView>(), Is.Not.Null);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OptionsPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Regular));
            Assert.That(prefab.activeSelf, Is.True, "Opening the prefab shows the menu without running the game.");
            var options = prefab.GetComponent<OptionPanelView>();
            Assert.That(options, Is.Not.Null);
            var serialized = new SerializedObject(options);
            AssertReferences(serialized, "board", "outside", "top", "player", "outsideClick", "title");
            var rows = serialized.FindProperty("rows");
            Assert.That(rows.arraySize, Is.EqualTo(OptionMenu.Rows.Length));
            for (int i = 0; i < rows.arraySize; i++)
            {
                var row = rows.GetArrayElementAtIndex(i);
                foreach (var name in new[] { "row", "highlight", "box", "icon", "leftArrow", "rightArrow", "name", "value", "select", "previous", "next" })
                    Assert.That(row.FindPropertyRelative(name).objectReferenceValue, Is.Not.Null, $"Option {i}: {name}");
            }
        }

        static void AssertReferences(SerializedObject target, params string[] names)
        {
            foreach (string name in names)
                Assert.That(target.FindProperty(name).objectReferenceValue, Is.Not.Null, target.targetObject.name + ": " + name);
        }
    }
}
