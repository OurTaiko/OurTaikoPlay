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
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
            yield return null;
            var overlay = SettingsOverlay.Open();
            var nav = overlay.Navigator;
            overlay.Move(1); overlay.Confirm(); overlay.Confirm();
            Assert.That(nav.Row.Key, Is.EqualTo(InputKey.LeftDon));
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Sheet));
            yield return new WaitForSecondsRealtime(0.3f);
            AssertClickable(Option(overlay, 0));
            // Open capture with the current Don key. That same press must not be captured.
            Press(Key.F); yield return null;
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Capture));
            yield return null;
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Capture));
            Press(); yield return null;
            Press(Key.Escape); yield return null;
            Assert.That(nav.Zone, Is.EqualTo(SettingsZone.Rows));
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.Escape }));
            Assert.That(nav.Settings.general.keyboard.Get(InputKey.Back), Is.Empty);
            Press(); yield return null;
            var manager = SettingManager.EnsureInstance();
            manager.Load(manager.FilePath);
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.F, Key.Escape }));
            overlay.Close();
            // Pointer operations remain available even when all keyboard controls are unbound.
            var empty = manager.Settings.Clone();
            foreach (InputKey action in System.Enum.GetValues(typeof(InputKey))) empty.general.keyboard.Set(action);
            manager.Set(empty);
            yield return null;
            overlay = SettingsOverlay.Open();
            nav = overlay.Navigator;
            overlay.View.TapPage(1); overlay.View.TapPage(1);
            overlay.View.OpenRow(0);
            Assert.That(nav.SheetOptions, Is.EqualTo(new[] { "Add a Key", "Reset to Default", "Clear All" }));
            yield return new WaitForSecondsRealtime(0.3f);
            AssertClickable(Option(overlay, 1));
            Option(overlay, 1).Clicked();
            Assert.That(InputManager.GetBinding(InputKey.LeftDon), Is.EqualTo(new[] { Key.F }));
            overlay.Close();
            Object.Destroy(overlay.gameObject);
        }

        static PointerRelay Option(SettingsOverlay overlay, int index)
            => overlay.View.GetComponentsInChildren<PointerRelay>().Single(r => r.name == "Option" + index);

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
