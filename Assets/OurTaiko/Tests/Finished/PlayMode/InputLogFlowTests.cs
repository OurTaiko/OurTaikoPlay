using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The input diagnostics log (Settings → General → Input Log): devices always, and with the switch
    // on each layer a press passes, from the device event to the lane's judgment or drop.
    public sealed class InputLogFlowTests
    {
        Keyboard keyboard;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        readonly List<string> lines = new List<string>();
        double clock;

        void Collect(string message, string stackTrace, LogType type)
        {
            if (message.StartsWith("[" + InputLog.Tag + "] ")) lines.Add(message.Substring(InputLog.Tag.Length + 3));
        }

        [SetUp]
        public void SetUp()
        {
            var settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
            lines.Clear();
            Application.logMessageReceived += Collect;
            keyboard = InputSystem.AddDevice<Keyboard>("InputLogKeyboard");
            keyboard.MakeCurrent();
            clock = InputState.currentTime;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Collect;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            var settings = InputSystem.settings;
            settings.backgroundBehavior = originalBackground;
            settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
            SettingManager.EnsureInstance().UseUnsaved(new GameSettings());
        }

        static void UseInputLog(bool on)
        {
            var settings = new GameSettings();
            settings.general.inputLog = on;
            SettingManager.EnsureInstance().UseUnsaved(settings);
        }

        IEnumerator EmptyScene()
        {
            var empty = SceneManager.CreateScene("InputLog");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != empty) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            yield return null;
        }

        void Queue(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys), clock += 0.001);

        [UnityTest]
        public IEnumerator DevicesAreLoggedAndPressesOnlyWithTheSwitch()
        {
            yield return EmptyScene();
            Assert.That(lines.Any(l => l.StartsWith("Device Added: InputLogKeyboard#") && l.Contains("layout Keyboard")), Is.True,
                string.Join("\n", lines));

            // Off by default: a press leaves no trace.
            lines.Clear();
            Queue(Key.F); yield return null;
            Queue(); yield return null;
            Assert.That(InputManager.PressesThisFrame, Is.Empty);
            Assert.That(lines, Is.Empty);

            UseInputLog(true);
            Assert.That(InputLog.Enabled, Is.True);
            lines.Clear();
            Queue(Key.F); yield return null;
            Assert.That(lines.Any(l => l.StartsWith("event InputLogKeyboard#") && l.Contains(" f down (LeftDon)")), Is.True, string.Join("\n", lines));
            Assert.That(lines.Any(l => l.StartsWith("publish LeftDon ")), Is.True, string.Join("\n", lines));

            // A second key-down of a key already down (another keyboard pressing it) changes nothing.
            lines.Clear();
            Queue(Key.F); yield return null;
            Assert.That(lines.Any(l => l.Contains(" no button changed | held f")), Is.True, string.Join("\n", lines));
            Assert.That(lines.Any(l => l.StartsWith("publish")), Is.False);

            lines.Clear();
            InputSystem.QueueTextEvent(keyboard, 'f'); yield return null;
            Assert.That(lines.Any(l => l.StartsWith("text 'f' from InputLogKeyboard#")), Is.True, string.Join("\n", lines));

            Queue(); yield return null;
            UseInputLog(false);
            Assert.That(InputLog.Enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator TheLaneLogsWhatItJudgedAndWhatItDropped()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Input Log\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n5,\n0,\n8,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                UseInputLog(true);
                yield return TestPlay.Open(song);
                float deadline = Time.realtimeSinceStartup + 20;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                var play = Object.FindAnyObjectByType<PlayScene>();
                deadline = Time.realtimeSinceStartup + 10;
                while (play.SongTime < 0.3)
                {
                    if (play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }

                lines.Clear();
                Queue(Key.D); Queue(Key.D, Key.F);
                yield return null;
                Assert.That(lines.Any(l => l.StartsWith("judge 1P left ka at chart ")), Is.True, string.Join("\n", lines));
                Assert.That(lines.Any(l => l.StartsWith("drop 1P LeftDon ") && l.Contains("an earlier press this frame was judged")), Is.True,
                    string.Join("\n", lines));
                Queue(); yield return null;
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }
    }
}
