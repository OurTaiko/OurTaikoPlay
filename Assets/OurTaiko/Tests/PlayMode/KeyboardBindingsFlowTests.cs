using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class KeyboardBindingsFlowTests
    {
        Keyboard keyboard;
        InputSettings.BackgroundBehavior background;
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
        string directory;

        [SetUp]
        public void Setup()
        {
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            directory = Path.Combine(Application.temporaryCachePath, "keyboard-" + System.Guid.NewGuid());
            SettingManager.EnsureInstance().Load(Path.Combine(directory, "settings.json"));
        }

        [TearDown]
        public void Cleanup()
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        void Press(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

        static void AssertClickable(PointerRelay relay)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)relay.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits.First().gameObject, Is.SameAs(relay.gameObject));
        }

        [UnityTest]
        public IEnumerator CaptureConsumesNavigationKeysAndPersistsEmptyAndMultipleBindings()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
            yield return null;
            var scene = Object.FindFirstObjectByType<GlobalSettingScene>();
            scene.Don(); scene.Ka(1); scene.Don();
            AssertClickable(scene.view.choiceRows[0].click);
            // Open capture with the current Don key. That same press must not be captured.
            Press(Key.F); yield return null;
            Assert.That(scene.Menu.CapturingKey, Is.True);
            yield return null;
            Assert.That(scene.Menu.CapturingKey, Is.True);
            Press(); yield return null;
            Press(Key.Escape); yield return null;
            Assert.That(scene.Menu.CapturingKey, Is.False);
            Assert.That(scene.Menu.Focus, Is.EqualTo(SettingsFocus.Choice));
            Assert.That(scene.Menu.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.Escape }));
            Assert.That(scene.Menu.Settings.general.keyboard.Get(InputKey.Back), Is.Empty);
            Press(); yield return null;
            TestCapture.Capture("KeyboardBindings.png");
            var manager = SettingManager.EnsureInstance();
            manager.Load(manager.FilePath);
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.Escape }));
            // Pointer operations remain available even when all keyboard controls are unbound.
            var empty = manager.Settings.Clone();
            foreach (InputKey action in System.Enum.GetValues(typeof(InputKey))) empty.general.keyboard.Set(action);
            manager.Set(empty);
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SettingScene);
            yield return null;
            scene = Object.FindFirstObjectByType<GlobalSettingScene>();
            scene.view.typeRows[0].click.Clicked();
            scene.view.itemRows[1].click.Clicked(); scene.view.itemRows[1].click.Clicked();
            AssertClickable(scene.view.choiceRows[2].click);
            scene.view.choiceRows[2].click.Clicked();
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
            scene.view.shadeClick.Clicked();
            scene.view.nextItems.Clicked();
            Assert.That(scene.view.FirstItem, Is.GreaterThan(0));
            AssertClickable(scene.view.nextItems);
        }

        [UnityTest]
        public IEnumerator EveryBoundKeyProducesTheActionAndHoldingEitherKeepsItHeld()
        {
            var emptyScene = SceneManager.CreateScene("KeyboardRuntime");
            SceneManager.SetActiveScene(emptyScene);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != emptyScene) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            var settings = new GameSettings();
            settings.general.keyboard.Set(InputKey.LeftDon, Key.A, Key.S);
            settings.general.keyboard.Set(InputKey.RightDon);
            SettingManager.EnsureInstance().UseUnsaved(settings);
            yield return null;
            Press(Key.A); yield return null;
            Assert.That(InputManager.GetKeyDown(InputKey.LeftDon), Is.True);
            Press(Key.A, Key.S); yield return null;
            Assert.That(InputManager.GetKeyDown(InputKey.LeftDon), Is.True);
            Assert.That(InputManager.PressesThisFrame.Count(p => p.Key == InputKey.LeftDon), Is.EqualTo(1));
            Press(Key.S); yield return null;
            Assert.That(InputManager.GetKey(InputKey.LeftDon), Is.True);
            Assert.That(InputManager.GetKeyDown(InputKey.LeftDon), Is.False);
            Press(Key.J); yield return null;
            Assert.That(InputManager.GetKey(InputKey.LeftDon), Is.False);
            Assert.That(InputManager.GetKeyDown(InputKey.RightDon), Is.False);
            Press(); yield return null;
        }
    }
}
