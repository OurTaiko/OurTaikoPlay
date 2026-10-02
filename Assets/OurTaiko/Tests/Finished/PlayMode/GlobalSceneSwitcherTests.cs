using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    public sealed class GlobalSceneSwitcherTests
    {
        [UnityTest]
        public IEnumerator GlobalControlWaitsForPreparationAndOwnsTheEntireSwitch()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.EnsureInstance();
            Assert.That(Resources.Load<SceneSwitcher>("SceneSwitcher"), Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().GetRootGameObjects().Any(root => root.GetComponent<SceneSwitcher>() != null), Is.False);
            Assert.That(Application.CanStreamedLevelBeLoaded("SceneSwitcher"), Is.False);
            Assert.That(switcher.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
            Assert.That(switcher.GetComponent<Canvas>().sortingOrder, Is.EqualTo(short.MaxValue));
            Assert.That(switcher.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(1920, 1080)));

            var preparation = new TaskCompletionSource<bool>();
            int changes = 0;
            bool coveredOnLoad = false;
            EventHandler<(string NewScene, string OldScene)> handler = (_, scenes) =>
            {
                changes++;
                coveredOnLoad = switcher.IsCovered;
                Assert.That(scenes, Is.EqualTo((SceneSwitcher.GameScene, SceneSwitcher.MenuScene)));
            };
            SceneSwitcher.OnSceneChanged += handler;
            float originalScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                var operation = switcher.SwitchSceneAfterTaskAsync(SceneSwitcher.GameScene, (Task)preparation.Task);
                Assert.That(switcher.IsInputBlocked, Is.True);
                Assert.That(switcher.SwitchSceneAsync(SceneSwitcher.MenuScene), Is.SameAs(operation), "Repeated requests join the existing switch.");
                yield return new WaitForSecondsRealtime(1);
                Assert.That(switcher.IsCovered, Is.True, "Closing must run even when timeScale is zero.");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.MenuScene), "The old scene stays loaded until preparation finishes.");
                Assert.That(changes, Is.Zero);
                Assert.That(switcher.GetComponentInChildren<CanvasGroup>(true).blocksRaycasts, Is.True);
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) }, hits);
                Assert.That(hits.First().gameObject.transform.IsChildOf(switcher.transform), Is.True, "The global cover blocks pointer input to scene buttons.");
                preparation.SetResult(true);
                yield return WaitFor(operation);
                Assert.That(operation.IsCompletedSuccessfully, Is.True);
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(coveredOnLoad, Is.True, "The new scene must load behind the closed global cover.");
                Assert.That(SceneSwitcher.CurrentScene, Is.EqualTo(SceneSwitcher.GameScene));
                Assert.That(SceneSwitcher.LastScene, Is.EqualTo(SceneSwitcher.MenuScene));
                Assert.That(SceneSwitcher.MainCamera, Is.SameAs(Camera.main));
                Assert.That(SceneSwitcher.Instance, Is.SameAs(switcher));
                Assert.That(switcher.IsInputBlocked, Is.False);
                Assert.That(Object.FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                yield return null;
                Assert.That(Object.FindFirstObjectByType<PlayScene>().SongTime, Is.LessThan(-1.7), "The gameplay countdown starts after revealing the scene.");
            }
            finally
            {
                SceneSwitcher.OnSceneChanged -= handler;
                Time.timeScale = originalScale;
            }
            yield return WaitFor(switcher.SwitchSceneAsync(SceneSwitcher.MenuScene));
        }

        [UnityTest]
        public IEnumerator CallerCanHoldTheCoverAndRevealAfterInitialization()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.EnsureInstance();
            var value = new TaskCompletionSource<int>();
            var operation = switcher.SwitchSceneAfterTaskAsync(SceneSwitcher.GameScene, value.Task, false);
            value.SetResult(42);
            yield return WaitFor(operation);
            Assert.That(operation.Result, Is.EqualTo(42));
            Assert.That(SceneSwitcher.CurrentScene, Is.EqualTo(SceneSwitcher.GameScene));
            Assert.That(switcher.IsSwitching, Is.False);
            Assert.That(switcher.IsCovered, Is.True);
            Assert.That(switcher.IsInputBlocked, Is.True);
            var play = Object.FindFirstObjectByType<PlayScene>();
            Assert.That(play.Session, Is.Not.Null, "Target initialization runs while covered.");
            Assert.That(play.SongTime, Is.EqualTo(-2));
            yield return WaitFor(switcher.FadeOutAsync());
            yield return null;
            Assert.That(switcher.IsInputBlocked, Is.False);
            Assert.That(play.SongTime, Is.LessThan(-1.7));
            yield return WaitFor(switcher.SwitchSceneAsync(SceneSwitcher.GameScene));
            Assert.That(SceneSwitcher.LastScene, Is.EqualTo(SceneSwitcher.GameScene), "Reloading the same scene still records a scene change.");
            Assert.That(Object.FindFirstObjectByType<PlayScene>(), Is.Not.SameAs(play));
            yield return WaitFor(switcher.SwitchSceneAsync(SceneSwitcher.MenuScene));
        }

        [UnityTest]
        public IEnumerator InvalidFaultedAndCancelledRequestsLeaveTheOldSceneUsable()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.EnsureInstance();
            var invalid = switcher.SwitchSceneAsync("MissingScene");
            Assert.That(invalid.IsFaulted, Is.True);
            Assert.That(invalid.Exception.InnerException, Is.TypeOf<ArgumentException>());
            Assert.That(switcher.IsInputBlocked, Is.False);
            var nullTask = switcher.SwitchSceneAfterTaskAsync(SceneSwitcher.GameScene, (Task)null);
            Assert.That(nullTask.Exception.InnerException, Is.TypeOf<ArgumentNullException>());
            var failed = switcher.SwitchSceneAfterTaskAsync(SceneSwitcher.GameScene, Task.FromException(new InvalidOperationException("Preparation failed")));
            yield return WaitFor(failed);
            Assert.That(failed.Exception.InnerException.Message, Is.EqualTo("Preparation failed"));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.MenuScene));
            Assert.That(switcher.IsInputBlocked, Is.False);
            var cancelled = new TaskCompletionSource<bool>(); cancelled.SetCanceled();
            var cancelSwitch = switcher.SwitchSceneAfterTaskAsync(SceneSwitcher.GameScene, (Task)cancelled.Task);
            yield return WaitFor(cancelSwitch);
            Assert.That(cancelSwitch.IsCanceled, Is.True);
            Assert.That(switcher.IsInputBlocked, Is.False);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.MenuScene));
            yield return WaitFor(switcher.FadeInAsync());
            Assert.That(switcher.IsCovered, Is.True);
            yield return WaitFor(switcher.FadeOutAsync());
            Assert.That(switcher.IsInputBlocked, Is.False);
        }

        [UnityTest]
        public IEnumerator DestroyingGlobalControlCancelsAPendingSwitch()
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            var switcher = SceneSwitcher.EnsureInstance();
            var preparation = new TaskCompletionSource<bool>();
            var operation = switcher.SwitchSceneAfterTaskAsync(SceneSwitcher.GameScene, (Task)preparation.Task);
            yield return new WaitForSecondsRealtime(1);
            Object.Destroy(switcher.gameObject);
            yield return WaitFor(operation);
            Assert.That(operation.IsCanceled, Is.True);
            preparation.SetResult(true);
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.MenuScene));
            Assert.That(SceneSwitcher.EnsureInstance(), Is.Not.SameAs(switcher));
            Assert.That(Object.FindObjectsByType<SceneSwitcher>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        static IEnumerator WaitFor(Task operation)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!operation.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
        }
    }
}
